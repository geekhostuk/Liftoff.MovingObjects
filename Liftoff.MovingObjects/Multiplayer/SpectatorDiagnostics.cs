using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx.Logging;
using HarmonyLib;
using Liftoff.MovingObjects.Utils;
using Photon.Pun;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

// Photon.Realtime.Player collides with this mod's own Liftoff.MovingObjects.Player namespace, so
// bind the Photon one explicitly rather than importing the namespace.
using PhotonPlayer = Photon.Realtime.Player;

namespace Liftoff.MovingObjects.Multiplayer;

// TEMPORARY investigation scaffolding for the multiplayer spectator desync (JMT00084) — this whole
// file is expected to be deleted or demoted to LogDebug once the questions below are answered. It
// only ever logs; it never changes game or mod behaviour.
//
// Why it exists: when you spectate another pilot, your client never fires the local drone-reset
// events (FlightManager is entirely local-player scoped), so moving objects keep running on their
// own timeline and drift from what the pilot sees. Several facts needed to fix that proved
// impossible to establish by inspecting Assembly-CSharp, because the game encrypts its string
// literals — every ldstr in the assembly is empty and ~10k call sites decrypt byte arrays at
// runtime. So the following can only be settled by capturing one live spectate session:
//
//   Q1. Do spectators actually RECEIVE the game's RPCPlayerReset? It's a [PunRPC], but the sender's
//       RpcTarget is an encrypted string we can't read, so "All/Others" (spectators get it) vs
//       "MasterClient" (they don't) is unknown. This decides the whole fix.
//   Q2. Which of the two RPCPlayerReset declaring types is live in a normal race room.
//   Q3. What layer do remote/spectated drones sit on? Triggers gate on the "Drone" layer, so if
//       remote drones aren't on it, waitForTrigger objects never start for a spectator. The layer
//       table has no remote-drone layer and the game never calls LayerMask.NameToLayer, so this is
//       purely empirical.
//   Q4. Can we correlate the player in an RPC with the current spectate target?
//   Q5. Does the old v1.2.2 log marker ("Attached spectator camera to") fire at all on this build?
//   Q6. Does RPCPlayerReset also fire for the LOCAL player's own reset (would double-fire with
//       onDroneResetStart)?
//
// Capture protocol: fly solo a lap -> spectate a second pilot -> let them reset twice -> switch
// spectate target -> press F10 while spectating -> have them fly through a trigger object.
internal static class SpectatorDiagnostics
{
    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{PluginInfo.PLUGIN_NAME}.Diag");

    internal static bool Enabled { get; private set; }

    private static GameObject _pump;

    // Q3/Q5 produce a line per frame if left unguarded; these keep one session's log readable.
    private static readonly HashSet<string> LoggedLogMarkers = new();
    private static readonly Dictionary<string, float> LastTriggerLog = new();
    private const float TriggerLogIntervalSeconds = 1f;

    internal static void Install(Harmony harmony, bool enabled)
    {
        Enabled = enabled;
        if (!Enabled)
            return;

        // Awake tolerates CreateAndPatchAll failing (it only logs), so don't assume we got a Harmony.
        if (harmony == null)
        {
            Enabled = false;
            Log.LogWarning("No Harmony instance — spectator diagnostics disabled");
            return;
        }

        Log.LogInfo("=== SPECTATOR DIAGNOSTICS ACTIVE (temporary; logging only) ===");
        Log.LogInfo("Press F10 during flight/spectate to dump drone-layer info.");

        PatchAllPunRpcs(harmony);
        PatchSpectateHooks(harmony);

        // Q5: does the v1.2.2 marker exist on this build at all? Non-threaded event = main thread.
        Application.logMessageReceived += OnGameLogMessage;
    }

    // Q1/Q2/Q6. The declaring types of the game's RPCs are obfuscated, so they can't be named with
    // typeof(...). But PUN dispatches RPCs by *method-name string* at runtime, which means an
    // obfuscator cannot rename them without breaking the game's own netcode — so binding by name +
    // [PunRPC] is actually a sturdier hook than the readable-type ones the rest of the mod uses.
    private static void PatchAllPunRpcs(Harmony harmony)
    {
        var postfix = new HarmonyMethod(typeof(SpectatorDiagnostics), nameof(OnPunRpcPostfix));
        var patched = 0;
        var failed = 0;

        foreach (var method in PunRpcScanner.All())
        {
            try
            {
                harmony.Patch(method, postfix: postfix);
                patched++;
            }
            catch (Exception ex)
            {
                failed++;
                Log.LogWarning($"Could not patch [PunRPC] {Describe(method)}: {ex.Message}");
            }
        }

        Log.LogInfo($"Patched {patched} [PunRPC] methods for diagnostics ({failed} failed)");
    }

    // ReplayControls.SpectateNewDrone was hooked here originally on the theory that live spectate is
    // built on the replay camera. It never fired once across a full session, so it isn't the live
    // spectate path — dropped rather than left as misleading noise.
    private static void PatchSpectateHooks(Harmony harmony)
    {
        TryPatch(harmony, typeof(InGamePlayerActivityIndicator), "SetSpectating", nameof(OnSetSpectating));
        TryPatch(harmony, typeof(InGameMenuMainPanel), "OnSpectate", nameof(OnSpectateClicked));
    }

    private static void TryPatch(Harmony harmony, Type type, string methodName, string postfixName)
    {
        try
        {
            var target = AccessTools.Method(type, methodName);
            if (target == null)
            {
                Log.LogWarning($"Hook target missing: {type.Name}.{methodName}");
                return;
            }

            harmony.Patch(target, postfix: new HarmonyMethod(typeof(SpectatorDiagnostics), postfixName));
            Log.LogInfo($"Hooked {type.Name}.{methodName}");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to hook {type.Name}.{methodName}: {ex.Message}");
        }
    }

    // ---- probes -------------------------------------------------------------------------------

    private static void OnPunRpcPostfix(MethodBase __originalMethod, object[] __args)
    {
        try
        {
            var sb = new StringBuilder();
            sb.Append("[RPC] ").Append(__originalMethod.Name)
                .Append("  onType=").Append(TypeLabel(__originalMethod.DeclaringType))
                .Append("  photonTime=").Append(SafePhotonTime());

            if (__args != null)
                for (var i = 0; i < __args.Length; i++)
                    sb.Append("  arg").Append(i).Append('=').Append(DescribeArg(__args[i]));

            Log.LogInfo(sb.ToString());
        }
        catch (Exception ex)
        {
            Log.LogWarning($"RPC probe failed: {ex.Message}");
        }
    }

    private static void OnSetSpectating(bool __0)
    {
        Log.LogInfo($"[SPECTATE] SetSpectating({__0})");
    }

    private static void OnSpectateClicked()
    {
        Log.LogInfo("[SPECTATE] InGameMenuMainPanel.OnSpectate clicked");
    }

    // Q5. The v1.2.2 sync watched for "Attached spectator camera to". Log any spectator-ish line
    // once per distinct message so we learn the real wording (if any) without flooding the log.
    private static void OnGameLogMessage(string condition, string stackTrace, LogType type)
    {
        if (condition == null ||
            condition.IndexOf("spectat", StringComparison.OrdinalIgnoreCase) < 0)
            return;

        if (!LoggedLogMarkers.Add(condition))
            return;

        Log.LogInfo($"[LOGPROBE] game log line mentioning 'spectat': \"{condition}\"");
    }

    // Q3. Called from TriggerBehavior.OnTriggerEnter, before its drone-layer gate rejects the
    // collider — so this reports what actually enters trigger volumes, including remote drones that
    // the gate may currently be discarding.
    internal static void ReportTriggerEnter(Collider other, int droneLayer)
    {
        if (!Enabled || other == null)
            return;

        try
        {
            var key = other.gameObject.name;
            var now = Time.unscaledTime;
            if (LastTriggerLog.TryGetValue(key, out var last) &&
                now - last < TriggerLogIntervalSeconds)
                return;
            LastTriggerLog[key] = now;

            var layer = other.gameObject.layer;
            Log.LogInfo(
                $"[TRIGGER] entered by '{other.gameObject.name}' layer={layer}"
                + $" ({LayerMask.LayerToName(layer)})"
                + $" passesDroneGate={(layer == droneLayer ? "YES" : "NO")}"
                + $" body={(other.attachedRigidbody != null ? other.attachedRigidbody.name : "<none>")}");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Trigger probe failed: {ex.Message}");
        }
    }

    // Q3, on demand. Plugin's own MonoBehaviour is destroyed early by the game (see Plugin.OnDestroy)
    // and the editor windows that own the other F-keys aren't present in flight, so the dump needs
    // its own pump. Re-ensured from the FlightManager.Start postfix, which runs once per flight.
    internal static void EnsurePump()
    {
        if (!Enabled || _pump != null)
            return;

        _pump = new GameObject("MO_SpectatorDiagnostics");
        _pump.AddComponent<DiagnosticsPump>();
        UnityEngine.Object.DontDestroyOnLoad(_pump);
    }

    internal static void LogPhotonState(string when)
    {
        if (!Enabled)
            return;

        try
        {
            if (!PhotonNetwork.InRoom)
            {
                Log.LogInfo($"[PHOTON] {when}: not in a room");
                return;
            }

            var room = PhotonNetwork.CurrentRoom;
            Log.LogInfo(
                $"[PHOTON] {when}: inRoom players={room?.PlayerCount} "
                + $"local={DescribePlayer(PhotonNetwork.LocalPlayer)} time={PhotonNetwork.Time:F3}");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Photon state probe failed: {ex.Message}");
        }
    }

    // Q3. Every rigidbody-backed collider in the scene with its layer, so we can see exactly what a
    // remote/spectated drone looks like locally and whether it would pass TriggerBehavior's gate.
    internal static void DumpDroneLayers()
    {
        var droneLayer = LayerMask.NameToLayer("Drone");
        var localDrone = SafeLocalDroneName();

        Log.LogInfo($"=== F10 LAYER DUMP === droneLayer={droneLayer} localDrone={localDrone}");
        LogPhotonState("layer dump");

        // Deliberately NOT filtered to rigidbody-backed colliders. A remote drone may well be a
        // visual replica with no Rigidbody, and a kinematic trigger volume still raises
        // OnTriggerEnter against a rigidbody-less collider — so filtering on Rigidbody here would
        // hide exactly the object we're looking for.
        var colliders = UnityEngine.Object.FindObjectsOfType<Collider>();
        var byLayer = new Dictionary<int, int>();
        foreach (var c in colliders)
        {
            byLayer.TryGetValue(c.gameObject.layer, out var n);
            byLayer[c.gameObject.layer] = n + 1;
        }

        Log.LogInfo($"  -- collider census ({colliders.Length} colliders, rigidbody or not) --");
        foreach (var kv in byLayer.OrderByDescending(kv => kv.Value))
            Log.LogInfo(
                $"     layer={kv.Key} ({LayerMask.LayerToName(kv.Key)}) count={kv.Value}"
                + (kv.Key == droneLayer ? "   <-- DRONE LAYER" : ""));

        // Every collider on the drone layer, plus anything that merely looks drone-ish by name or
        // component — the remote drone could be on any layer, so name is the fallback identifier.
        Log.LogInfo("  -- drone-layer / drone-named colliders --");
        var hits = 0;
        foreach (var c in colliders)
        {
            var go = c.gameObject;
            var path = HierarchyPath(go.transform);
            var nameLooksDrone = path.IndexOf("drone", StringComparison.OrdinalIgnoreCase) >= 0;
            if (go.layer != droneLayer && !nameLooksDrone)
                continue;

            var body = c.attachedRigidbody;
            Log.LogInfo(
                $"     '{go.name}' layer={go.layer} ({LayerMask.LayerToName(go.layer)})"
                + $" onDroneLayer={(go.layer == droneLayer ? "YES" : "no")}"
                + $" isTrigger={c.isTrigger}"
                + $" body={(body == null ? "<NONE>" : body.name + (body.isKinematic ? " (kinematic)" : ""))}"
                + $" path={path}");
            hits++;
        }

        if (hits == 0)
            Log.LogInfo("     (none — no drone-layer or drone-named collider exists on this client)");

        // Separate question from the collider census above: does a remote drone exist locally *at
        // all*? If the spectated pilot's drone is a visual replica with no collider, it won't show
        // up above, but it still has a Transform. Names follow "Drone_<pilot>" (seen in earlier
        // logs), so scan every transform by name.
        Log.LogInfo("  -- drone-named GameObjects (collider or not) --");
        var objects = 0;
        foreach (var t in UnityEngine.Object.FindObjectsOfType<Transform>())
        {
            if (t.name.IndexOf("drone", StringComparison.OrdinalIgnoreCase) < 0)
                continue;

            Log.LogInfo(
                $"     '{t.name}' layer={t.gameObject.layer} ({LayerMask.LayerToName(t.gameObject.layer)})"
                + $" colliders={t.GetComponentsInChildren<Collider>(true).Length}"
                + $" bodies={t.GetComponentsInChildren<Rigidbody>(true).Length}"
                + $" active={t.gameObject.activeInHierarchy}"
                + $" path={HierarchyPath(t)}");
            objects++;
        }

        if (objects == 0)
            Log.LogInfo("     (none — no drone-named GameObject exists on this client)");

        Log.LogInfo(
            $"=== END LAYER DUMP ({colliders.Length} colliders, {hits} drone-ish, {objects} drone-named) ===");
    }

    // ---- helpers ------------------------------------------------------------------------------

    // Q4. The RPC argument is an obfuscated struct, but it carries a Photon.Realtime.Player field —
    // a readable type — which is the only identity we can correlate against the spectate target.
    private static string DescribeArg(object arg)
    {
        if (arg == null)
            return "<null>";

        if (arg is PhotonPlayer directPlayer)
            return DescribePlayer(directPlayer);

        var nested = ReflectionUtils.GetFieldValueByType<PhotonPlayer>(arg);
        if (nested != null)
            return $"{TypeLabel(arg.GetType())}{{{DescribePlayer(nested)}}}";

        if (arg is string || arg.GetType().IsPrimitive)
            return $"{arg}";

        return TypeLabel(arg.GetType());
    }

    private static string DescribePlayer(PhotonPlayer player)
    {
        if (player == null)
            return "<no player>";
        return $"actor={player.ActorNumber} nick='{player.NickName}' isLocal={player.IsLocal}";
    }

    // Obfuscated type names are long runs of quote characters — useless in a log and they wreck
    // readability. Collapse them to a stable short identity we can still correlate across lines.
    private static string TypeLabel(Type type)
    {
        if (type == null)
            return "<null type>";

        var name = type.Name;
        if (name.Length > 0 && name.All(c => c == '\'' || c == '"'))
            return $"<obf#{name.Length}:{name.GetHashCode():X8}>";

        return name;
    }

    private static string Describe(MethodBase method) =>
        $"{TypeLabel(method.DeclaringType)}.{method.Name}";

    private static string SafePhotonTime()
    {
        try
        {
            return PhotonNetwork.InRoom ? PhotonNetwork.Time.ToString("F3") : "<not in room>";
        }
        catch
        {
            return "<unavailable>";
        }
    }

    private static string SafeLocalDroneName()
    {
        try
        {
            var drone = Plugin.HookedFlightManager?.CurrentDrone;
            return drone == null ? "<none>" : drone.ToString();
        }
        catch (Exception ex)
        {
            return $"<error: {ex.Message}>";
        }
    }

    private static string HierarchyPath(Transform t)
    {
        var sb = new StringBuilder(t.name);
        for (var p = t.parent; p != null; p = p.parent)
            sb.Insert(0, p.name + "/");
        return sb.ToString();
    }

    private sealed class DiagnosticsPump : MonoBehaviour
    {
        private void Update()
        {
            if (Input.GetKeyDown(KeyCode.F10))
                DumpDroneLayers();
        }
    }
}
