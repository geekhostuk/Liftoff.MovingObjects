using System.Linq;
using System.Reflection;
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

    public bool triggerOnce;
    public float triggerCooldown;
    public bool sequentialTargets;

    public bool boostEnabled;
    public float speedMultiplier;
    public float targetSpeed;

    public bool windEnabled;
    public SerializableVector3 forceVector;
    public int forceMode;
    public bool forceLocalSpace;

    public bool routeBySpeed;
    public float routeSpeedThreshold;

    public bool playSoundOnTrigger;
    private TrackItemPlaySoundTrigger[] _soundTriggers;

    // PlaySoundFile is non-public on the native item, so it's driven by reflection.
    private static readonly MethodInfo PlaySoundMethod = typeof(TrackItemPlaySoundTrigger)
        .GetMethod("PlaySoundFile", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    private bool _hasFiredOnce;
    private float _cooldownUntil;
    private int _sequentialIndex;

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

        // Sound-on-trigger reuses the same name matching: give a native TrackItemPlaySoundTrigger
        // a trigger Name equal to this entrance's Target and its configured sound plays on pass.
        if (playSoundOnTrigger)
            _soundTriggers = targetTriggers.Select(t => t.GetComponent<TrackItemPlaySoundTrigger>())
                .Where(p => p != null).ToArray();

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
        // One-shot / cooldown gating. triggerOnce fires only the first pass per flight (re-armed
        // on drone reset via ResetState); triggerCooldown rate-limits re-firing.
        if (triggerOnce && _hasFiredOnce)
            return false;
        if (triggerCooldown > 0f && Time.time < _cooldownUntil)
            return false;

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
        _hasFiredOnce = true;
        if (triggerCooldown > 0f)
            _cooldownUntil = Time.time + triggerCooldown;

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
            QueueTeleport(body, speed);

        if (boostEnabled && body != null)
            ApplyBoost(body);

        if (playSoundOnTrigger && _soundTriggers != null && PlaySoundMethod != null)
            foreach (var sound in _soundTriggers)
                if (sound != null)
                    PlaySoundMethod.Invoke(sound, null);

        return true;
    }

    // Boost / brake gate: rescale the drone's speed in place (no teleport) on pass. An absolute
    // targetSpeed (km/h) wins when set; otherwise a multiplier scales the current speed. Direction
    // is preserved. Reuses the same km/h convention as the seamless-teleport exitSpeed remap.
    private void ApplyBoost(Rigidbody body)
    {
        var velocity = body.velocity;
        if (targetSpeed > 0f)
        {
            var speedMps = targetSpeed / 3.6f;
            velocity = velocity.sqrMagnitude > 1e-6f
                ? velocity.normalized * speedMps
                : transform.forward * speedMps;
        }
        else if (speedMultiplier > 0f)
        {
            velocity *= speedMultiplier;
        }

        body.velocity = velocity;
    }

    // Picks a destination marker and computes where/how the drone should arrive. For a plain
    // teleport that is just the destination position, leaving velocity and orientation alone
    // (legacy behaviour). For a seamless ("portal") teleport we re-express the drone's entry
    // velocity and orientation in the destination's frame, so it exits along the destination's
    // facing carrying its momentum — optionally rescaled to exitSpeed (km/h; 0 = keep speed).
    private void QueueTeleport(Rigidbody body, float speed)
    {
        var target = SelectTarget(speed);

        _teleportDrone = body;
        _teleportPos = target.position;
        _teleportApplyMotion = false;

        if (!seamlessTeleport)
            return;

        var src = transform.parent != null ? transform.parent : transform;
        var entryToExit = target.rotation * Quaternion.Inverse(src.rotation);

        // Re-express the drone's entry offset in the destination's frame so it exits at the same
        // spot within the portal it entered. The offset is measured about src.position (not
        // transform.position) to match the frame entryToExit is defined about — otherwise a
        // parented trigger rotates the offset about the wrong pivot and lands off-target.
        _teleportPos = target.position + entryToExit * (body.position - src.position);

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

    // Choose which exit marker (when several share the target name) the drone teleports to.
    // Sequential cycles through them in order for predictable multi-exit routing; otherwise a
    // random one is picked (scatter portal).
    private Transform SelectTarget(float speed)
    {
        if (_teleportTargets.Length == 1)
            return _teleportTargets[0];

        // Speed-based routing: below the threshold take the first marker, at/above it the second
        // (skill-gated shortcut). Falls back gracefully when only one marker is authored.
        if (routeBySpeed)
        {
            var index = speed >= routeSpeedThreshold ? Mathf.Min(1, _teleportTargets.Length - 1) : 0;
            return _teleportTargets[index];
        }

        if (sequentialTargets)
            return _teleportTargets[_sequentialIndex++ % _teleportTargets.Length];

        return _teleportTargets[Random.Range(0, _teleportTargets.Length)];
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

    // Wind / force volume: apply a continuous force to the drone every physics step it overlaps
    // the volume (updrafts, wind tunnels, push/pull zones). Acceleration mode is mass-independent;
    // Force mode scales with the drone's mass.
    public void OnTriggerStay(Collider other)
    {
        if (!windEnabled || other.gameObject.layer != _droneLayer)
            return;

        var body = other.attachedRigidbody;
        if (body == null)
            return;

        var force = new Vector3(forceVector.x, forceVector.y, forceVector.z);
        if (forceLocalSpace)
            force = transform.TransformDirection(force);

        body.AddForce(force, forceMode == 1 ? ForceMode.Acceleration : ForceMode.Force);
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

    // Re-arm per-flight trigger state (one-shot / cooldown). Called from the drone-reset hook so a
    // one-shot gate fires again on the next flight without a scene reload.
    public void ResetState()
    {
        _triggered = false;
        _sweptTriggered = false;
        _hasFiredOnce = false;
        _cooldownUntil = 0f;
        _sequentialIndex = 0;
    }
}
