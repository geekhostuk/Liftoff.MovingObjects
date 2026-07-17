using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

// Experimental multiplayer spectator sync.
//
// The problem: FlightManager is entirely local-player scoped, so while you spectate someone your
// client never fires onDroneResetStart/Done. Moving objects keep running on their own timeline and
// drift from what the pilot sees, without bound.
//
// The signal: the game broadcasts RPCPlayerReset (a [PunRPC]) to the room whenever any pilot resets,
// and spectators do receive it — confirmed by live capture: 11/11 remote resets arrived, each
// carrying a Photon Player. We re-run our own reset path on that.
//
// Why an RPC and not the game log: v1.2.2 watched for the log line "Attached spectator camera to".
// That line is real and does fire, but only when the spectator camera *attaches* — i.e. when you
// switch target — NOT when the pilot you're watching resets. Measured: 6 marker hits against 11
// resets, including 8 consecutive resets it ignored entirely. It was resyncing on the wrong event.
//
// Why binding by method name is safe despite obfuscation: PUN dispatches RPCs by method-name string
// at runtime, so an obfuscator cannot rename RPCPlayerReset without breaking the game's own netcode.
// The declaring types ARE obfuscated, so they're found by scanning rather than named with typeof.
internal static class SpectatorSync
{
    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{PluginInfo.PLUGIN_NAME}.SpectatorSync");

    private const string ResetRpcName = "RPCPlayerReset";

    // A reset can arrive as a burst (RPCPlayerReset may pair with RPCRaceRestart, and a target switch
    // can retrigger); collapse those into one resync.
    private const float DebounceSeconds = 0.5f;

    private static bool _enabled;
    private static float _lastSyncTime = float.NegativeInfinity;

    // Nickname of the pilot currently being spectated, or null when not spectating. Nickname rather
    // than actor number because it's the one identity we can read from both sides: the [PunRPC]
    // carries a Photon Player, and the UI row exposes a player object we can only reach reflectively.
    private static string _spectateTarget;

    // Set once we know whether target identity is resolvable on this build; keeps the log to one line
    // instead of one per reset.
    private static bool _loggedTargetResolution;

    private static SyncPump _pump;

    internal static void Install(Harmony harmony, bool enabled)
    {
        _enabled = enabled;
        if (!_enabled)
            return;

        if (harmony == null)
        {
            _enabled = false;
            Log.LogWarning("No Harmony instance — spectator sync disabled");
            return;
        }

        var resetRpcs = PunRpcScanner.ByName(ResetRpcName).ToList();
        if (resetRpcs.Count == 0)
        {
            _enabled = false;
            Log.LogWarning(
                $"No [PunRPC] {ResetRpcName} found in the game assembly — spectator sync disabled. "
                + "If Liftoff renamed it, re-run tools/PatchHelper and update this hook.");
            return;
        }

        var postfix = new HarmonyMethod(typeof(SpectatorSync), nameof(OnPlayerResetRpc));
        var patched = 0;
        foreach (var rpc in resetRpcs)
        {
            try
            {
                harmony.Patch(rpc, postfix: postfix);
                patched++;
            }
            catch (Exception ex)
            {
                Log.LogWarning($"Could not patch {ResetRpcName}: {ex.Message}");
            }
        }

        // Spectate state comes from the per-player UI row, not a global flag: SetSpectating(bool) is
        // called once per player avatar on every refresh (measured 33 false / 5 true in one session),
        // so the bool alone is meaningless — the row's own player identity is what matters.
        try
        {
            var setSpectating = AccessTools.Method(typeof(InGamePlayerActivityIndicator), "SetSpectating");
            if (setSpectating != null)
                harmony.Patch(setSpectating,
                    postfix: new HarmonyMethod(typeof(SpectatorSync), nameof(OnSetSpectating)));
            else
                Log.LogWarning("InGamePlayerActivityIndicator.SetSpectating missing — "
                               + "cannot track spectate target; will resync on any remote reset");
        }
        catch (Exception ex)
        {
            Log.LogWarning($"Failed to hook SetSpectating: {ex.Message}");
        }

        Log.LogInfo($"Experimental spectator animation sync enabled (hooked {patched} {ResetRpcName})");
    }

    // ---- hooks --------------------------------------------------------------------------------

    private static void OnSetSpectating(object __instance, bool __0)
    {
        if (!_enabled)
            return;

        try
        {
            var nick = ResolveRowNickname(__instance);
            if (nick == null)
                return;

            if (__0)
            {
                if (_spectateTarget != nick)
                    Log.LogInfo($"Spectating '{nick}'");
                _spectateTarget = nick;
            }
            else if (_spectateTarget == nick)
            {
                // The row for the pilot we were watching just went inactive: we stopped spectating
                // them. Other rows' false calls are refresh noise and must not clear the target.
                Log.LogInfo($"Stopped spectating '{nick}'");
                _spectateTarget = null;
            }
        }
        catch (Exception ex)
        {
            Log.LogWarning($"SetSpectating probe failed: {ex.Message}");
        }
    }

    private static void OnPlayerResetRpc(object[] __args)
    {
        if (!_enabled)
            return;

        try
        {
            var player = ExtractPlayer(__args);

            // The local player's own reset already drives the real onDroneResetStart/Done path; the
            // capture showed the local actor never appears here, but don't rely on that.
            if (player != null && player.IsLocal)
                return;

            // Unconditional, and deliberately checked BEFORE the target match: _spectateTarget can go
            // stale (its UI row is destroyed on a scene change or on leaving the room, so the
            // SetSpectating(false) that would clear it may never arrive). Gating on target alone would
            // then let a remote reset restart every moving object *while the local pilot is flying* —
            // turning a spectate-only feature into a mid-lap regression for normal play. Being unable
            // to resync is a far cheaper failure than resyncing someone who is racing.
            if (!IsSpectating())
                return;

            // Only resync for the pilot actually on screen. If identity can't be resolved on this
            // build we deliberately fall through and resync on any remote reset: a spurious re-snap
            // is far better than the unbounded drift this feature exists to remove.
            if (_spectateTarget != null && player != null &&
                !string.Equals(player.NickName, _spectateTarget, StringComparison.Ordinal))
                return;

            if (Time.unscaledTime - _lastSyncTime < DebounceSeconds)
                return;
            _lastSyncTime = Time.unscaledTime;

            Log.LogInfo(
                $"Spectated pilot reset ({player?.NickName ?? "unknown"}) — re-syncing moving objects");

            // PUN dispatches RPCs on the main thread, so the reset path's FindObjectsOfType /
            // AddComponent / component-enable calls are legal here. Still deferred by a frame: this
            // runs inside PUN's dispatch loop, and destroying/adding components mid-dispatch is a
            // needless risk when a frame of latency costs nothing at this scale.
            RequestResync();
        }
        catch (Exception ex)
        {
            Log.LogWarning($"{ResetRpcName} handler failed: {ex}");
        }
    }

    // ---- helpers ------------------------------------------------------------------------------

    // The RPC's argument is an obfuscated struct we can't name, but it carries a Photon.Realtime.Player
    // field — a readable type — which is the identity we correlate on.
    private static PhotonPlayer ExtractPlayer(object[] args)
    {
        if (args == null)
            return null;

        foreach (var arg in args)
        {
            if (arg is PhotonPlayer direct)
                return direct;

            var nested = ReflectionUtils.GetFieldValueByType<PhotonPlayer>(arg);
            if (nested != null)
                return nested;
        }

        return null;
    }

    // The UI row's player is the game's own obfuscated player type, so it can't be named — but if it
    // wraps a Photon Player we can read a nickname off it and correlate with the RPC.
    //
    // SetSpectating fires once per player row on every UI refresh (measured 38 calls in one short
    // session), so the member lookup is resolved once and cached rather than re-reflected per call.
    private static string ResolveRowNickname(object indicator)
    {
        if (indicator == null)
            return null;

        var wrapper = GetRowPlayer(indicator);
        var rowPlayer = ReflectionUtils.GetFieldValueByType<PhotonPlayer>(wrapper)
                        ?? ReflectionUtils.GetFieldValueByType<PhotonPlayer>(indicator);

        if (!_loggedTargetResolution)
        {
            _loggedTargetResolution = true;
            Log.LogInfo(rowPlayer != null
                ? "Spectate-target identity resolved — resync is filtered to the watched pilot"
                : "Could not resolve spectate-target identity on this build — falling back to "
                  + "resyncing on any remote pilot's reset while spectating");
        }

        return rowPlayer?.NickName;
    }

    private static bool _rowPlayerLookupResolved;
    private static PropertyInfo _rowPlayerProperty;
    private static FieldInfo _rowPlayerField;

    private static object GetRowPlayer(object indicator)
    {
        if (!_rowPlayerLookupResolved)
        {
            _rowPlayerLookupResolved = true;
            var type = indicator.GetType();
            _rowPlayerProperty = AccessTools.Property(type, "Player");
            _rowPlayerField = _rowPlayerProperty == null ? AccessTools.Field(type, "player") : null;
        }

        return _rowPlayerProperty != null
            ? _rowPlayerProperty.GetValue(indicator)
            : _rowPlayerField?.GetValue(indicator);
    }

    // Are we actually watching someone else fly? Two independent conditions, both required:
    //
    //   * We're in a flight session with no drone of our own. While spectating, the local flight
    //     manager's CurrentDrone is null (measured: localDrone=<none>); while racing it isn't. This
    //     is what keeps a remote pilot's reset from ever disturbing the local pilot's own lap.
    //     A null FlightManager means we're in a menu, not spectating — hence the explicit check
    //     rather than treating "no flight manager" as "not flying".
    //   * There's someone else in the room to watch.
    private static bool IsSpectating()
    {
        try
        {
            if (!PhotonNetwork.InRoom || PhotonNetwork.CurrentRoom?.PlayerCount <= 1)
                return false;

            var flightManager = Plugin.HookedFlightManager;
            return flightManager != null && flightManager.CurrentDrone == null;
        }
        catch
        {
            return false;
        }
    }

    private static void RequestResync()
    {
        EnsurePump();
        if (_pump != null)
            _pump.Pending = true;
        else
            Resync(); // No pump (shouldn't happen) — better a same-frame resync than none.
    }

    private static void EnsurePump()
    {
        if (_pump != null)
            return;

        var go = new GameObject("MO_SpectatorSync");
        UnityEngine.Object.DontDestroyOnLoad(go);
        _pump = go.AddComponent<SyncPump>();
    }

    private static void Resync()
    {
        Plugin.RunDroneResetStart();
        Plugin.RunDroneResetDone();
    }

    private sealed class SyncPump : MonoBehaviour
    {
        internal bool Pending;

        private void LateUpdate()
        {
            if (!Pending)
                return;
            Pending = false;
            Resync();
        }
    }
}
