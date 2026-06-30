using System.Collections.Generic;
using System.IO;
using BepInEx;
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

    private static AssetBundle _assetBundle;
    private static AnimationEditorWindow.Assets _editorAssets;
    private static PlacementUtilsWindow.Assets _placementAssets;

    private Harmony _harmony;

    private void Awake()
    {
        Log.LogInfo($"{PluginInfo.PLUGIN_NAME} {PluginInfo.PLUGIN_VERSION} loaded");

        try { DontDestroyOnLoad(gameObject); }
        catch (System.Exception ex) { Log.LogWarning($"DontDestroyOnLoad failed: {ex.Message}"); }

        try { _harmony = Harmony.CreateAndPatchAll(typeof(Plugin)); }
        catch (System.Exception ex) { Log.LogError($"Harmony.CreateAndPatchAll failed: {ex}"); }

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

    // PopupShareContent.ShareItem patch removed: signature changed in the current
    // game (now takes a third parameter), and HarmonyX throws on a missing target,
    // which would abort CreateAndPatchAll and prevent every other patch attaching.
    // The feature (overriding the Workshop preview image with a local preview.png)
    // can be re-enabled once the new third parameter type is referenceable.

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TrackEditorEditWindow), "AtLeastOneItemAvailable", typeof(TrackItemCategory))]
    private static void AtLeastOneItemAvailable(ref bool __result)
    {
        if (!__result)
            __result = true;
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

    [HarmonyPostfix]
    [HarmonyPatch(typeof(FlightManager), "Start")]
    private static void OnFlightManagerStart(FlightManager __instance)
    {
        if (_hookedFlightManager == __instance)
            return;
        _hookedFlightManager = __instance;
        __instance.onDroneResetStart += OnDroneResetStart;
        __instance.onDroneResetDone += OnDroneResetDone;
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
    }

    private static void OnDroneResetDone()
    {
        // Run injection on every drone-reset-done. The Add* / GroupFlags helpers
        // below are individually idempotent (each checks for the component or
        // group GameObject it would create and bails out if it's already there),
        // so this is cheap on subsequent resets and only does real work when the
        // track has changed and new flag GameObjects are present.
        var flags = EditorUtils.FindAllFlags();
        GroupFlags(flags);
        InjectPlayers(flags);

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
        if (!string.IsNullOrEmpty(blueprint.mo_groupId))
            return; // TODO: Fix group physics
        if (flag.gameObject.GetComponent<PhysicsPlayer>() != null)
            return;

        Log.LogInfo($"Item with physics detected: {blueprint}, {flag}");

        var player = flag.gameObject.AddComponent<PhysicsPlayer>();
        player.options = blueprint.mo_animationOptions;
        player.waitForTrigger = waitForTrigger;

        var collider = flag.GetComponentInChildren<Collider>();
        if (collider?.enabled == false)
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
        player.steps = blueprint.mo_animationSteps;
        player.options = blueprint.mo_animationOptions;
        player.waitForTrigger = waitForTrigger;
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

        if (!string.IsNullOrEmpty(options.triggerTarget))
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
            }
        }

        return waitForTrigger;
    }

    private static void InjectPlayers(IEnumerable<Component> flags)
    {
        foreach (var flag in flags)
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);

            var waitForTrigger = false;
            if (blueprint?.mo_triggerOptions != null)
                waitForTrigger = AddTrigger(blueprint, flag);

            if (blueprint?.mo_animationOptions?.simulatePhysics == true)
                AddPhysics(blueprint, flag, waitForTrigger);
            else if (blueprint?.mo_animationSteps?.Count > 0)
                AddAnimation(blueprint, flag, waitForTrigger);
        }
    }

    private static void GroupFlags(IEnumerable<Component> flags)
    {
        var groups = new Dictionary<string, List<GameObject>>();
        var rootObjects = new Dictionary<string, GameObject>();

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

            if (blueprint.mo_animationOptions != null)
                rootObjects[groupId] = flag.gameObject;
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
                o.transform.parent = groupObject.transform;
        }
    }
}
