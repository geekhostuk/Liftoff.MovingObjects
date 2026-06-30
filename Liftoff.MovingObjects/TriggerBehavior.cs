using System.Linq;
using BepInEx.Logging;
using Liftoff.MovingObjects.Player;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace Liftoff.MovingObjects;

internal class TriggerName : MonoBehaviour
{
    public string triggerName;
}

internal class TriggerBehavior : MonoBehaviour
{
    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{PluginInfo.PLUGIN_NAME}.{nameof(TriggerBehavior)}");

    private AnimationPlayer[] _animationPlayers;
    private PhysicsPlayer[] _physicsPlayers;
    private Transform[] _teleportTargets;

    private Vector3 _teleportPos;
    private Quaternion _teleportRot;
    private Vector3 _teleportVel;
    private bool _teleportApplyMotion;
    private Rigidbody _teleportDrone;

    private Collider[] _colliders;
    private int _droneLayer;

    private bool _triggered;

    // Set when the trigger fired from a swept (tunneling) pass rather than a real
    // OnTriggerEnter. Such a pass produces no matching OnTriggerExit to clear _triggered,
    // so we release it ourselves on the next physics step (the drone is already past).
    private bool _sweptTriggered;

    public float? triggerMaxSpeed;
    public float? triggerMinSpeed;
    public string triggerTarget;
    public bool triggerTeleport;
    public bool seamlessTeleport;
    public float exitSpeed;

    private void Start()
    {
        _droneLayer = LayerMask.NameToLayer("Drone");
        _colliders = GetComponents<Collider>();

        var targetTriggers = FindObjectsByType<TriggerName>(FindObjectsSortMode.None)
            .Where(t => string.Equals(t.triggerName, triggerTarget)).ToArray();
        if (triggerTeleport)
            _teleportTargets = targetTriggers.Select(t => t.transform).ToArray();
        _animationPlayers = targetTriggers.Select(t => t.GetComponent<AnimationPlayer>()).Where(p => p != null)
            .ToArray();
        _physicsPlayers = targetTriggers.Select(t => t.GetComponent<PhysicsPlayer>()).Where(p => p != null).ToArray();

        Log.LogDebug(
            $"Detected {_animationPlayers.Length} animations and {_physicsPlayers.Length} physics for '{triggerTarget}' trigger");
    }

    private static float MpsToKph(float kps)
    {
        return kps * 3.6f;
    }

    // Shared by the normal OnTriggerEnter path and the swept fast-pass path. Returns true if
    // the pass actually triggered (i.e. it was not rejected by the speed gate).
    private bool HandlePass(Rigidbody body)
    {
        var speed = -1f;
        if (body != null)
        {
            speed = MpsToKph(body.velocity.magnitude);
            if (speed < triggerMinSpeed)
            {
                Log.LogDebug($"Trigger ignored {body}, speed {speed} < {triggerMinSpeed}");
                return false;
            }

            if (speed > triggerMaxSpeed)
            {
                Log.LogDebug($"Trigger ignored {body}, speed {speed} > {triggerMaxSpeed}");
                return false;
            }
        }

        _triggered = true;

        Log.LogDebug($"Triggered by {body}, speed {speed}");
        foreach (var player in _animationPlayers)
        {
            Log.LogDebug($"Triggered animation: {player} from '{triggerTarget}'");
            player.Trigger();
        }

        foreach (var player in _physicsPlayers)
        {
            Log.LogDebug($"Triggered physics: {player} from '{triggerTarget}'");
            player.Trigger();
        }

        if (_teleportTargets != null && _teleportTargets.Length > 0 && body != null && _teleportDrone == null)
            QueueTeleport(body);

        return true;
    }

    // Picks a destination marker and computes where/how the drone should arrive. For a plain
    // teleport that is just the destination position, leaving velocity and orientation alone
    // (legacy behaviour). For a seamless ("portal") teleport we re-express the drone's entry
    // velocity and orientation in the destination's frame, so it exits along the destination's
    // facing carrying its momentum — optionally rescaled to exitSpeed (km/h; 0 = keep speed).
    private void QueueTeleport(Rigidbody body)
    {
        var target = _teleportTargets[Random.Range(0, _teleportTargets.Length)];

        _teleportDrone = body;
        _teleportPos = target.position;
        _teleportApplyMotion = false;

        if (!seamlessTeleport)
            return;

        var src = transform.parent != null ? transform.parent : transform;
        var entryToExit = target.rotation * Quaternion.Inverse(src.rotation);

        var exitVel = entryToExit * body.velocity;
        if (exitSpeed > 0f)
        {
            var speedMps = exitSpeed / 3.6f;
            exitVel = exitVel.sqrMagnitude > 1e-6f ? exitVel.normalized * speedMps : target.forward * speedMps;
        }

        _teleportVel = exitVel;
        _teleportRot = entryToExit * body.rotation;
        _teleportApplyMotion = true;
    }

    public void OnTriggerEnter(Collider other)
    {
        if (other.gameObject.layer != _droneLayer || _triggered)
            return;

        HandlePass(other.attachedRigidbody);
    }

    public void OnTriggerExit(Collider other)
    {
        if (_triggered && other.gameObject.layer == _droneLayer)
            _triggered = false;
    }

    // Anti-tunneling: OnTriggerEnter only fires when the drone overlaps a collider on some
    // physics step. A fast drone can jump entirely past a (thin) trigger between two steps, so
    // the event never fires. Here we sweep the drone's path (previous->current position this
    // step) against our own collider(s); if the segment crosses one, the drone went through it
    // regardless of speed. The drone never being "inside" on any step is exactly the slow-case
    // OnTriggerEnter still covers, so the two paths together cover every speed.
    private void FixedUpdate()
    {
        // A swept pass leaves _triggered stuck (no OnTriggerExit follows a tunnel) — release it
        // now that the drone has moved on, so the gate can fire again on a later lap.
        if (_sweptTriggered)
        {
            _sweptTriggered = false;
            _triggered = false;
        }

        if (_triggered || _colliders == null)
            return;

        foreach (var drone in DroneContinuousCollision.Active)
        {
            if (drone == null || drone.Body == null)
                continue;

            var prev = drone.PreviousPosition;
            var seg = drone.CurrentPosition - prev;
            var dist = seg.magnitude;
            if (dist < 1e-4f)
                continue;

            var ray = new Ray(prev, seg / dist);
            var crossed = false;
            foreach (var col in _colliders)
                if (col != null && col.Raycast(ray, out _, dist))
                {
                    crossed = true;
                    break;
                }

            if (!crossed)
                continue;

            if (HandlePass(drone.Body))
            {
                _sweptTriggered = true;
                break;
            }
        }
    }

    private void LateUpdate()
    {
        if (_teleportDrone == null)
            return;

        _teleportDrone.position = _teleportPos;
        if (_teleportApplyMotion)
        {
            _teleportDrone.rotation = _teleportRot;
            _teleportDrone.velocity = _teleportVel;
        }

        // The drone just jumped across the map. Collapse its swept-trajectory sample to the
        // destination so the next physics step doesn't read an entrance->exit segment and let the
        // anti-tunneling check re-fire a trigger (which would teleport/re-rotate it again).
        var continuousCollision = _teleportDrone.GetComponent<DroneContinuousCollision>();
        if (continuousCollision != null)
            continuousCollision.ResetTrajectory();

        _teleportDrone = null;
    }
}
