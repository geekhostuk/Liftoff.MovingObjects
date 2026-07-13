using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using Liftoff.MovingObjects.Player;
using Liftoff.MovingObjects.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Liftoff.MovingObjects;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
public sealed class Plugin : BaseUnityPlugin
{
    private static readonly ManualLogSource Log =
        BepInEx.Logging.Logger.CreateLogSource($"{PluginInfo.PLUGIN_NAME}.{nameof(Plugin)}");

    // Forward-compatibility gate. Tracks are stamped on save with the minimum mod version required
    // to play them correctly (mo_minModVersion). At flight time we refuse to animate a track that
    // needs a newer build than the one installed, so it fails as a clean "objects sit still — update
    // your mod" state rather than a confusing half-working animation.
    //
    // MinCompatibleVersion is the value stamped into freshly-saved tracks. Bump it ONLY when a
    // release changes/adds behaviour such that an older mod would mis-play a track authored with it;
    // do NOT bump it for editor-only or bugfix releases. It is deliberately separate from (and lags)
    // PLUGIN_VERSION so non-breaking version bumps don't needlessly block older mods.
    //
    // NOTE: this only protects builds that contain this gate code (this release onward). Mods already
    // shipped without it will ignore the stamp and animate regardless — it cannot be applied
    // retroactively.
    internal static readonly System.Version MinCompatibleVersion = new System.Version("1.3.6");
    private static readonly System.Version RunningVersion = new System.Version(PluginInfo.PLUGIN_VERSION);

    // Guards the "track needs a newer mod" warning so it logs once per blocked track, not every reset.
    private static System.Version _lastVersionBlockLogged;

    private static AssetBundle _assetBundle;
    private static AnimationEditorWindow.Assets _editorAssets;
    private static PlacementUtilsWindow.Assets _placementAssets;

    private Harmony _harmony;

    // Experimental multiplayer spectator sync. When you spectate another pilot, your client never
    // fires the local drone-reset events (FlightManager.onDroneReset*), so moving objects keep
    // running from their own start time and drift out of sync with what the spectated pilot sees.
    // Liftoff logs a line when the spectator camera (re)attaches to a pilot — on their reset, and
    // when you switch spectate target — so we watch the main-thread log stream for that marker and
    // re-run our own reset + re-inject to resync. Off by default: the marker text is version-
    // specific, and this is best-effort (network latency can still cause brief clipping).
    private const string SpectatorAttachMarker = "Attached spectator camera to";
    private static ConfigEntry<bool> _spectatorSyncEnabled;
    private static float _lastSpectatorSyncTime = float.NegativeInfinity;
    private const float SpectatorSyncDebounceSeconds = 0.5f;

    private void Awake()
    {
        Log.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded");

        try { DontDestroyOnLoad(gameObject); }
        catch (System.Exception ex) { Log.LogWarning($"DontDestroyOnLoad failed: {ex.Message}"); }

        try { _harmony = Harmony.CreateAndPatchAll(typeof(Plugin)); }
        catch (System.Exception ex) { Log.LogError($"Harmony.CreateAndPatchAll failed: {ex}"); }

        _spectatorSyncEnabled = Config.Bind(
            "Experimental", "SpectatorAnimationSync", false,
            "EXPERIMENTAL. When spectating another pilot in multiplayer, re-sync moving-object "
            + "animations each time the spectated pilot resets (detected from the game log stream). "
            + "Best-effort and Liftoff-version-specific; network latency can still cause brief "
            + "clipping. Takes effect on the next game start.");

        if (_spectatorSyncEnabled.Value)
        {
            // Subscribe to logMessageReceived, NOT ...Threaded: the non-threaded event is raised on
            // the Unity main thread, so OnGameLogMessage can call the reset path (FindObjectsOfType,
            // AddComponent, component enable/disable) directly with no cross-thread marshalling. The
            // handler is static, so it keeps working even after this MonoBehaviour is torn down early
            // (see OnDestroy) — we never depend on this component's Update running.
            Application.logMessageReceived += OnGameLogMessage;
            Log.LogInfo("Experimental spectator animation sync enabled");
        }

        try
        {
            _assetBundle = AssetBundle.LoadFromMemory(UI.LiftoffUI);
            if (_assetBundle == null)
            {
                Log.LogError("Failed to load embedded UI asset bundle");
                return;
            }

            _editorAssets = new AnimationEditorWindow.Assets
            {
                VisualTreeAsset = _assetBundle.LoadAsset<VisualTreeAsset>(
                    "Assets/Liftoff.MovingObject/AnimationEditorWindow.uxml"),
                AnimationTemplateAsset = _assetBundle.LoadAsset<VisualTreeAsset>(
                    "Assets/Liftoff.MovingObject/AnimationStepTemplate.uxml"),
                PanelSettings = _assetBundle.LoadAsset<PanelSettings>(
                    "Assets/Liftoff.MovingObject/AnimationEditorWindowPanelSettings.asset")
            };

            _placementAssets = new PlacementUtilsWindow.Assets
            {
                VisualTreeAsset = _assetBundle.LoadAsset<VisualTreeAsset>(
                    "Assets/Liftoff.MovingObject/UtilsWindow.uxml"),
                PanelSettings = _assetBundle.LoadAsset<PanelSettings>(
                    "Assets/Liftoff.MovingObject/UtilsWindowPanelSettings.asset")
            };
        }
        catch (System.Exception ex)
        {
            Log.LogError($"Asset bundle setup failed: {ex}");
        }
    }

    private void OnDestroy()
    {
        // Intentionally NOT calling _harmony.UnpatchSelf() / _assetBundle.Unload() here.
        // The current game build destroys this MonoBehaviour very early (during the
        // bootstrap-scene unload, well before flight). Tearing down the Harmony patches
        // and asset bundle at that point silently disabled the entire mod for the rest
        // of the session. The patches are static and continue functioning without us;
        // the asset bundle is referenced by editor windows attached on demand.
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TrackEditorGUI), "Start")]
    private static void OnTrackEditorGuiStart(TrackEditorGUI __instance)
    {
        // Fresh editor session: clear undo history and arm the post-load suppression window so the
        // items the loaded track spawns don't seed the history.
        UndoHistory.ResetForNewSession();

        var trackMenu = ReflectionUtils.GetPrivateFieldValue<TrackEditorMenuManager>(__instance, "trackMenu");
        var trackBuilderPanel =
            ReflectionUtils.GetPrivateFieldValue<TrackEditorEditWindow>(trackMenu, "trackBuilderPanel");

        var animation = trackBuilderPanel.detailPane.gameObject.AddComponent<AnimationEditorWindow>();
        animation.assets = _editorAssets;

        trackBuilderPanel.onItemSelected += animation.OnItemSelected;
        trackBuilderPanel.onItemSelectionCleared += animation.OnItemCleared;

        var placementUtilsObj = new GameObject("MO_PlacementUtils");
        placementUtilsObj.transform.SetParent(trackBuilderPanel.gameObject.transform);

        var placementUtilsWindow = placementUtilsObj.AddComponent<PlacementUtilsWindow>();
        placementUtilsWindow.assets = _placementAssets;
    }

    // Stamp the required-mod-version onto every item that carries MovingObjects config, just before
    // the game serializes the track to disk. This is the single chokepoint the Save button funnels
    // through, so it covers all authoring paths (editor, copy/paste, array/mirror, stamp-insert). We
    // stamp the live item blueprints (the same instances the game serializes) via the existing
    // FindAllFlags/reflection path. A missing stamp is treated as "compatible" at load, so any path
    // we somehow miss under-protects rather than falsely blocking.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TrackEditorMenuManager), "SaveTrack")]
    private static void StampTrackVersionOnSave()
    {
        var version = MinCompatibleVersion.ToString();
        foreach (var flag in EditorUtils.FindAllFlags())
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);
            if (HasMoContent(blueprint))
                blueprint.mo_minModVersion = version;
        }
    }

    // A blueprint carries MovingObjects config if it has animation options/steps, trigger options,
    // or a group id — mirrors the null-checks InjectPlayers uses.
    private static bool HasMoContent(TrackBlueprint blueprint)
    {
        return blueprint != null
               && (blueprint.mo_animationOptions != null
                   || blueprint.mo_triggerOptions != null
                   || blueprint.mo_animationSteps?.Count > 0
                   || !string.IsNullOrEmpty(blueprint.mo_groupId));
    }

    // Highest mo_minModVersion stamped across the track's items, or null if none is stamped
    // (older/un-stamped tracks — always treated as compatible). Unparseable stamps are ignored.
    private static System.Version RequiredVersion(IEnumerable<Component> flags)
    {
        System.Version required = null;
        foreach (var flag in flags)
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);
            var stamp = blueprint?.mo_minModVersion;
            if (string.IsNullOrEmpty(stamp) || !System.Version.TryParse(stamp, out var version))
                continue;
            if (required == null || version > required)
                required = version;
        }

        return required;
    }

    // Customizable Show-Text display time. Liftoff's native show-text trigger flashes its message for
    // a fixed ~1s with no exposed duration knob (the display is driven by an obfuscated runtime
    // manager whose show method takes only the text). When an author sets mo_textDisplayTime > 0 we
    // take over the display: render the same message ourselves for that duration (ShowTextOverlay) and
    // skip the game's default show. Left at 0, the game's default behaviour is untouched. Patched by
    // name only — there is a single OnDroneEnter overload, and its drone parameter is an obfuscated
    // (unnameable) type. Wrapped defensively: on any failure we fall back to the game's own display.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TrackItemShowTextTrigger), "OnDroneEnter")]
    private static bool ShowTextOnDroneEnter(TrackItemShowTextTrigger __instance)
    {
        try
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(__instance);
            var seconds = blueprint?.mo_textDisplayTime ?? 0f;
            if (seconds <= 0f)
                return true; // no custom duration → let the game show its default message

            var action = ReflectionUtils.GetPrivateFieldValueByType<ShowTextTrackItemAction>(__instance);
            var text = action?.displayText;
            if (string.IsNullOrEmpty(text))
                return true;

            ShowTextOverlay.Instance.Show(text, seconds);
            return false; // we rendered it ourselves; skip the game's default show
        }
        catch (System.Exception ex)
        {
            Log.LogWarning($"Show-Text custom duration failed, using game default: {ex.Message}");
            return true;
        }
    }

    // Override the Workshop preview image with a local preview.png when sharing a track.
    // The current game's ShareItem gained a third parameter of an obfuscated (unnameable) type,
    // which is why the old two-type-argument patch could no longer bind. There is only one
    // ShareItem overload, so we patch it by name alone (no parameter types) — HarmonyX resolves
    // the single method without us naming the obfuscated type — and reach the Sprite preview
    // positionally via __1 (the second parameter).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PopupShareContent), "ShareItem")]
    private static void ShareItem(ref Sprite __1)
    {
        var overwritePreview = Path.Combine(Paths.GameRootPath, "preview.png");
        if (!File.Exists(overwritePreview))
        {
            Log.LogInfo($"Preview overwrite not found {overwritePreview}, skip");
            return;
        }

        var preview = new Texture2D(2, 2);
        preview.LoadImage(File.ReadAllBytes(overwritePreview));
        __1 = Sprite.Create(preview, new Rect(0, 0, preview.width, preview.height), new Vector2(0.5f, 0.5f));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TrackEditorEditWindow), "AtLeastOneItemAvailable", typeof(TrackItemCategory))]
    private static void AtLeastOneItemAvailable(ref bool __result)
    {
        if (!__result)
            __result = true;
    }

    // Add chokepoint: every new track item — native palette placement AND the mod's own spawns
    // (ItemSpawner.SpawnFromBlueprint calls this to register the item into Track.blueprints) — passes
    // through here. Recording adds here captures both uniformly. __0 is the obfuscated item type,
    // bound positionally as the Component it derives from. Guarded: never break placement.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TrackEditor), "AssignIDToTrackItem")]
    private static void OnTrackItemAssignedId(Component __0)
    {
        try { UndoHistory.NotifyAdded(__0); }
        catch (System.Exception ex) { Log.LogWarning($"Undo add-capture failed: {ex.Message}"); }
    }

    // Remove chokepoint: the game's native erase and the mod's F9 delete both funnel through
    // TrackEditor.RemoveTrackItem (see ItemSpawner.RemoveItem). This is a PREFIX so we snapshot the
    // item before its GameObject is destroyed. Guarded: never break deletion.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TrackEditor), "RemoveTrackItem")]
    private static void OnTrackItemRemoving(Component __0)
    {
        try { UndoHistory.NotifyRemoving(__0); }
        catch (System.Exception ex) { Log.LogWarning($"Undo remove-capture failed: {ex.Message}"); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TrackDragCenterOnCamera), "OnDragHold")]
    [HarmonyPatch(typeof(TrackDragBehaviorSnap), "OnDragHold")]
    [HarmonyPatch(typeof(TrackDragBehaviorRibbon), "OnDragHold")]
    private static void OnDragHold(MonoBehaviour __instance)
    {
        if (Shared.PlacementUtils.DragGridRound <= 0)
            return;
        var parent = __instance.gameObject.transform.parent;
        if (parent == null)
            return;
        parent.position = GridUtils.RoundVectorToStep(parent.position, Shared.PlacementUtils.DragGridRound);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TrackDragBehaviorSnap), "OnDragRelease")]
    [HarmonyPatch(typeof(TrackDragCenterOnCamera), "OnDragRelease")]
    [HarmonyPatch(typeof(TrackDragBehaviorRibbon), "OnDragRelease")]
    private static void OnDragRelease(MonoBehaviour __instance)
    {
        if (!Shared.PlacementUtils.EnchantedEditor)
            return;

        var rot = __instance.gameObject.transform.rotation.eulerAngles;
        __instance.gameObject.transform.rotation =
            Quaternion.Euler(GridUtils.SmartRound(rot));
    }

    // Drone-reset hooks were originally a Harmony patch on FlightManager.ResetDroneRoutine.
    // That coroutine still exists in the current Assembly-CSharp.dll but is dead code: the
    // refactored flight manager drives reset through Reset() / IDroneResetHandle and raises
    // the parameterless Action events below instead. We subscribe to those events from a
    // postfix on FlightManager.Start, which is reliably called once per flight session.
    private static FlightManager _hookedFlightManager;

    // Exposed so HazardContact can crash the drone via the active flight manager.
    internal static FlightManager HookedFlightManager => _hookedFlightManager;

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FlightManager), "Start")]
    private static void OnFlightManagerStart(FlightManager __instance)
    {
        if (_hookedFlightManager == __instance)
            return;
        _hookedFlightManager = __instance;
        __instance.onDroneResetStart += OnDroneResetStart;
        __instance.onDroneResetDone += OnDroneResetDone;

        // Report the physics step actually in force during flight. Trigger/checkpoint
        // detection is capped at this rate (see TriggerBehavior), so knowing it explains
        // detection granularity. Logged here rather than at plugin load because the menu
        // scene may run a different fixedDeltaTime than flight.
        Log.LogInfo(
            $"Physics step: fixedDeltaTime={Time.fixedDeltaTime:F5}s ({1f / Time.fixedDeltaTime:F1} Hz)");
    }

    // Main-thread log handler for experimental spectator sync (see the field comment in Awake).
    // Liftoff emits SpectatorAttachMarker whenever the spectator camera (re)attaches to a pilot;
    // that fires on the spectated pilot's reset and on a spectate-target switch — both cases where
    // our moving objects need to be reset to line back up with the pilot's client.
    private static void OnGameLogMessage(string condition, string stackTrace, LogType type)
    {
        if (condition == null ||
            condition.IndexOf(SpectatorAttachMarker, System.StringComparison.Ordinal) < 0)
            return;

        // A target switch can emit the marker several times in a burst; debounce so we resync once.
        if (Time.unscaledTime - _lastSpectatorSyncTime < SpectatorSyncDebounceSeconds)
            return;
        _lastSpectatorSyncTime = Time.unscaledTime;

        Log.LogInfo("Spectated pilot reset detected — re-syncing moving objects");
        OnDroneResetStart();
        OnDroneResetDone();
    }

    private static void OnDroneResetStart()
    {
        foreach (var p in FindObjectsOfType<AnimationPlayer>())
        {
            p.enabled = false;
            p.Restart();
        }
        foreach (var p in FindObjectsOfType<PhysicsPlayer>())
        {
            p.enabled = false;
            p.Restart();
        }

        // Re-arm per-flight trigger state (one-shot gates, cooldowns, sequential counters).
        foreach (var t in FindObjectsOfType<TriggerBehavior>())
            t.ResetState();
    }

    private static void OnDroneResetDone()
    {
        // Run injection on every drone-reset-done. The Add* / GroupFlags helpers
        // below are individually idempotent (each checks for the component or
        // group GameObject it would create and bails out if it's already there),
        // so this is cheap on subsequent resets and only does real work when the
        // track has changed and new flag GameObjects are present.
        var flags = EditorUtils.FindAllFlags();

        // Forward-compatibility gate: if this track was authored with a newer, potentially breaking
        // mod build than the one installed, don't inject any players — leave every object static
        // rather than mis-play a partial animation. Un-stamped/older tracks return null and run
        // normally.
        var required = RequiredVersion(flags);
        if (required != null && RunningVersion < required)
        {
            if (!required.Equals(_lastVersionBlockLogged))
            {
                Log.LogWarning(
                    $"This track needs {PluginInfo.PLUGIN_NAME} {required} or newer, but "
                    + $"{PluginInfo.PLUGIN_VERSION} is installed — moving-object animation disabled. "
                    + "Please update the mod.");
                _lastVersionBlockLogged = required;
            }
            return;
        }
        _lastVersionBlockLogged = null;

        var groupRoots = ComputeGroupRoots(flags);
        GroupFlags(flags, groupRoots);
        InjectPlayers(flags, groupRoots);

        foreach (var p in FindObjectsOfType<AnimationPlayer>()) p.enabled = true;
        foreach (var p in FindObjectsOfType<PhysicsPlayer>()) p.enabled = true;

        EnableContinuousDroneCollision();
    }

    // Triggers (see TriggerBehavior) are detected via OnTriggerEnter, which only fires
    // when the drone overlaps the trigger collider on some FixedUpdate step. A fast
    // enough drone can tunnel completely through the volume between two physics steps
    // and the trigger (e.g. teleport) never fires. Continuous (speculative) collision
    // detection makes the rigidbody sweep its path each step, so it can no longer pass
    // through a trigger unseen.
    //
    // We can't just set collisionDetectionMode here: the game rebuilds/reconfigures the
    // drone rigidbody around reset time and resets the mode back to Discrete, racing
    // against (and usually beating) a one-shot set. So we attach a DroneContinuousCollision
    // watchdog that re-asserts the mode every FixedUpdate. Attaching is idempotent (one
    // component per drone object). Drones are identified the same way TriggerBehavior does:
    // by the "Drone" layer plus an attached rigidbody.
    private static void EnableContinuousDroneCollision()
    {
        var droneLayer = LayerMask.NameToLayer("Drone");
        if (droneLayer < 0)
            return;

        var seen = new HashSet<Rigidbody>();
        foreach (var collider in FindObjectsOfType<Collider>())
        {
            if (collider.gameObject.layer != droneLayer)
                continue;

            var body = collider.attachedRigidbody;
            if (body == null || !seen.Add(body))
                continue;

            if (body.GetComponent<DroneContinuousCollision>() != null)
                continue;

            body.gameObject.AddComponent<DroneContinuousCollision>();
            Log.LogDebug($"Attached continuous collision watchdog to drone rigidbody '{body.name}'");
        }
    }

    private static void AddPhysics(TrackBlueprint blueprint, Component flag, bool waitForTrigger)
    {
        if (flag.gameObject.GetComponent<PhysicsPlayer>() != null)
            return;

        Log.LogInfo($"Item with physics detected: {blueprint}, {flag}");

        var player = flag.gameObject.AddComponent<PhysicsPlayer>();
        player.options = blueprint.mo_animationOptions;
        player.waitForTrigger = waitForTrigger;

        // Only the group root (the flag carrying mo_animationOptions) reaches here, so it gets the
        // single Rigidbody. GroupFlags has already transform-parented the other members underneath
        // it, so they act as compound colliders of that one body — no nested rigidbodies. Prepare
        // every collider in the assembly so the whole group collides, not just the root.
        if (!string.IsNullOrEmpty(blueprint.mo_groupId))
        {
            foreach (var groupCollider in flag.GetComponentsInChildren<Collider>(true))
                PreparePhysicsCollider(groupCollider);
            return;
        }

        PreparePhysicsCollider(flag.GetComponentInChildren<Collider>());
    }

    // A collider that belongs to a moving object's dynamic (non-kinematic) Rigidbody has two
    // requirements the placed decorative item doesn't meet on its own:
    //  - it must be enabled (placed items often ship with their collider off), and
    //  - a MeshCollider must be convex. Unity silently drops a non-convex MeshCollider from a
    //    non-kinematic Rigidbody, so the body falls through surfaces or slides instead of rolling.
    //    Convex-hulling each half-sphere mesh is what lets a grouped sphere roll as one ball.
    private static void PreparePhysicsCollider(Collider collider)
    {
        if (collider == null)
            return;

        if (collider is MeshCollider meshCollider)
            meshCollider.convex = true;

        if (!collider.enabled)
        {
            collider.enabled = true;
            collider.gameObject.layer = LayerMask.NameToLayer("Ghost");
        }
    }

    private static void AddAnimation(TrackBlueprint blueprint, Component flag, bool waitForTrigger)
    {
        if (flag.gameObject.GetComponent<AnimationPlayer>() != null)
            return;

        Log.LogInfo($"Item with animation detected: {blueprint}, {flag}");

        var player = flag.gameObject.AddComponent<AnimationPlayer>();
        // A spinner/orbit-only object may carry no step list; the player iterates steps on init.
        player.steps = blueprint.mo_animationSteps ?? new List<MO_Animation>();
        player.options = blueprint.mo_animationOptions;

        var action = (MO_TriggerAction)blueprint.mo_animationOptions.triggerAction;
        // Stop-mode targets run from load so the trigger has something to halt; Restart-mode
        // targets stay dormant until triggered.
        player.waitForTrigger = waitForTrigger && action != MO_TriggerAction.Stop;
    }

    private static bool AddTrigger(TrackBlueprint blueprint, Component flag)
    {
        var options = blueprint.mo_triggerOptions;

        var waitForTrigger = !string.IsNullOrEmpty(options.triggerName);

        var existingName = flag.gameObject.GetComponent<TriggerName>();
        if (waitForTrigger && existingName == null)
        {
            flag.gameObject.AddComponent<TriggerName>().triggerName = options.triggerName;
            Log.LogInfo($"Item with trigger detected: {options.triggerTarget}/{options.triggerName}, {flag}");
        }

        // A trigger behaviour is needed for a teleport/animation target OR for a standalone
        // in-place effect (boost/brake gate, wind volume) that has no target.
        if (!string.IsNullOrEmpty(options.triggerTarget) || options.boostEnabled || options.windEnabled)
        {
            var checkpointTrigger = flag.gameObject.transform.Find("CheckpointTrigger");
            if (checkpointTrigger != null && checkpointTrigger.gameObject.GetComponent<TriggerBehavior>() == null)
            {
                var trigger = checkpointTrigger.gameObject.AddComponent<TriggerBehavior>();
                trigger.triggerTarget = options.triggerTarget;
                if (options.triggerMinSpeed > 0)
                    trigger.triggerMinSpeed = options.triggerMinSpeed;
                if (options.triggerMaxSpeed > 0)
                    trigger.triggerMaxSpeed = options.triggerMaxSpeed;
                trigger.triggerTeleport = options.triggerTeleport;
                trigger.seamlessTeleport = options.seamlessTeleport;
                trigger.exitSpeed = options.exitSpeed;
                trigger.triggerOnce = options.triggerOnce;
                trigger.triggerCooldown = options.triggerCooldown;
                trigger.sequentialTargets = options.sequentialTargets;
                trigger.boostEnabled = options.boostEnabled;
                trigger.speedMultiplier = options.speedMultiplier;
                trigger.targetSpeed = options.targetSpeed;
                trigger.windEnabled = options.windEnabled;
                trigger.forceVector = options.forceVector;
                trigger.forceMode = options.forceMode;
                trigger.forceLocalSpace = options.forceLocalSpace;
                trigger.routeBySpeed = options.routeBySpeed;
                trigger.routeSpeedThreshold = options.routeSpeedThreshold;
                trigger.playSoundOnTrigger = options.playSoundOnTrigger;
            }
        }

        return waitForTrigger;
    }

    private static void InjectPlayers(IEnumerable<Component> flags, Dictionary<string, GameObject> groupRoots)
    {
        foreach (var flag in flags)
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);

            var waitForTrigger = false;
            if (blueprint?.mo_triggerOptions != null)
                waitForTrigger = AddTrigger(blueprint, flag);

            // In a group, only the elected root drives motion; the other members are transform-parented
            // under it by GroupFlags and ride along as its compound body. Give every member its own
            // player and each gets its own kinematic Rigidbody, all of them driving the shared body
            // pose toward their own captured _initPosition — they fight and the group locks up. That is
            // exactly why honk's fan (shaft + 4 blades, all five carrying animation config) span in
            // preview, which uses a single follow-driver, yet froze in flight. So skip the motion
            // player for non-root grouped members. Triggers and hazard-on-contact don't add a
            // competing body, so they still apply per member.
            var isNonRootGroupMember =
                !string.IsNullOrEmpty(blueprint?.mo_groupId)
                && groupRoots.TryGetValue(blueprint.mo_groupId, out var root)
                && root != flag.gameObject;

            if (!isNonRootGroupMember)
            {
                if (blueprint?.mo_animationOptions?.simulatePhysics == true)
                    AddPhysics(blueprint, flag, waitForTrigger);
                // Continuous modes (spinner / orbit) run without a step list, so gate on them too —
                // otherwise a spinner-only object gets no AnimationPlayer and never rotates in flight
                // (it still previews in the editor, which attaches a player unconditionally).
                else if (blueprint?.mo_animationSteps?.Count > 0
                         || blueprint?.mo_animationOptions?.spinnerEnabled == true
                         || blueprint?.mo_animationOptions?.orbitEnabled == true)
                    AddAnimation(blueprint, flag, waitForTrigger);
            }

            // Hazard-on-contact turns the moving object into a drone-killer (idempotent).
            if (blueprint?.mo_animationOptions?.killOnContact == true &&
                flag.gameObject.GetComponent<HazardContact>() == null)
                flag.gameObject.AddComponent<HazardContact>();
        }
    }

    // One motion driver per group: the elected member is the root that GroupFlags parents the others
    // under and that InjectPlayers gives the single player. The driver MUST be a member that actually
    // carries motion, i.e. one InjectPlayers will really give a player to (physics / spinner / orbit /
    // keyframe steps). Electing merely "the first member with non-null options" was the bug behind
    // honk's frozen chamber: a group built from copies (copy/paste/stamp deep-clone the whole blueprint,
    // MO config included) can carry a non-null but motionless mo_animationOptions on several pieces. If
    // such a motionless piece won, the root failed InjectPlayers' gate and got no player, while the real
    // spinner/step member was skipped as a non-root member — so the group stood still in flight though
    // every piece previewed fine (the editor drives the selected member directly). Prefer a motion-
    // carrying member; fall back to any non-null-options member so motionless groups behave as before.
    // First-in-stable-order within each tier keeps the choice deterministic across resets, so the same
    // member wins every reset and GroupFlags' idempotent "already grouped?" check stays valid.
    //
    // NOTE: this is still ONE driver per group. Two members with different motions (e.g. a spinner on
    // one piece and a 90-degree step door on another) cannot both play in flight — only the elected
    // root moves. Independent motions need to live in separate groups (or stay ungrouped).
    private static Dictionary<string, GameObject> ComputeGroupRoots(IEnumerable<Component> flags)
    {
        var roots = new Dictionary<string, GameObject>();
        var rootHasMotion = new Dictionary<string, bool>();

        foreach (var flag in flags)
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);
            if (string.IsNullOrEmpty(blueprint?.mo_groupId) || blueprint.mo_animationOptions == null)
                continue;

            var groupId = blueprint.mo_groupId;
            var hasMotion = HasMotion(blueprint);

            // First member seeds a provisional root; a later motion-carrying member upgrades a
            // motionless provisional root (but never displaces an already motion-carrying one).
            if (!roots.ContainsKey(groupId))
            {
                roots[groupId] = flag.gameObject;
                rootHasMotion[groupId] = hasMotion;
            }
            else if (hasMotion && !rootHasMotion[groupId])
            {
                roots[groupId] = flag.gameObject;
                rootHasMotion[groupId] = true;
            }
        }

        return roots;
    }

    // A group member drives motion in flight only if InjectPlayers would give it a player: a physics
    // body, or an animation (continuous spinner / orbit, or a keyframe step list). Mirrors the gate in
    // InjectPlayers exactly so the elected root is guaranteed to receive a player.
    private static bool HasMotion(TrackBlueprint blueprint)
    {
        var options = blueprint.mo_animationOptions;
        if (options == null)
            return false;
        return options.simulatePhysics
               || options.spinnerEnabled
               || options.orbitEnabled
               || blueprint.mo_animationSteps?.Count > 0;
    }

    private static void GroupFlags(IEnumerable<Component> flags, Dictionary<string, GameObject> rootObjects)
    {
        var groups = new Dictionary<string, List<GameObject>>();

        foreach (var flag in flags)
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);
            if (string.IsNullOrEmpty(blueprint?.mo_groupId))
                continue;

            var groupId = blueprint.mo_groupId;
            if (groups.TryGetValue(groupId, out var list))
                list.Add(flag.gameObject);
            else
                groups[groupId] = new List<GameObject> { flag.gameObject };
        }

        foreach (var (groupId, gameObjects) in groups)
        {
            if (gameObjects.Count == 1 || !rootObjects.ContainsKey(groupId))
                continue;

            var rootObj = rootObjects[groupId];

            var groupName = "MO_Group_" + groupId;
            // Idempotency: if this group GameObject already exists under the
            // root, the flags are already parented and there is nothing to do.
            // We can't use GameObject.Find here because group names recur across
            // levels; check the current root's children directly.
            if (rootObj.transform.Find(groupName) != null)
                continue;

            var groupObject = new GameObject(groupName);
            groupObject.transform.parent = rootObj.transform;
            groupObject.transform.position = rootObj.transform.position;
            groupObject.transform.rotation = rootObj.transform.rotation;

            foreach (var o in gameObjects)
            {
                // Skip the root itself: it is the parent of groupObject, so reparenting it under
                // groupObject would ask Unity to make it its own descendant (a transform cycle).
                if (o == rootObj)
                    continue;
                o.transform.parent = groupObject.transform;
            }
        }
    }
}
