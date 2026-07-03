using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Liftoff.MovingObjects.Player;

internal enum MO_TriggerAction
{
    Restart = 0,
    Stop = 1
}

internal enum MO_Easing
{
    Linear = 0,
    SmoothStep = 1,
    EaseIn = 2,
    EaseOut = 3,
    EaseInOut = 4
}

internal sealed class AnimationPlayer : MonoBehaviour
{
    private Coroutine _animationCoroutine;
    private Vector3 _initPosition;
    private Quaternion _initRotation;
    private Rigidbody _rigidBody;

    private Step[] _stepsCached;
    private Step[] _reversedCached;

    private bool _continuousActive;
    private float _spinAngle;
    private float _orbitAngle;
    private Coroutine _startDelayCoroutine;

    public MO_AnimationOptions options;
    public List<MO_Animation> steps;
    public bool waitForTrigger;

    // Continuous procedural motion (spinner / orbit) is driven every frame from Update() rather
    // than the keyframe coroutine, so endless rotation or a circular path doesn't require dozens
    // of tiny steps.
    private bool ContinuousMode => options.spinnerEnabled || options.orbitEnabled;

    private void Start()
    {
        _stepsCached = steps.Select(animation => new Step(animation)).ToArray();
        _reversedCached = BuildReversed(_stepsCached);
        _rigidBody = gameObject.AddComponent<Rigidbody>();
        _rigidBody.isKinematic = true;

        _initPosition = transform.position;
        _initRotation = transform.rotation;

        if (!waitForTrigger)
            StartMotion();
    }

    // Route between the keyframe coroutine and continuous procedural motion, applying a one-time
    // phase offset first. The offset (optionally randomized within [0, phaseOffset]) desyncs a
    // field of identical objects so they don't all move in lockstep.
    private void StartMotion()
    {
        var delay = options.phaseOffset;
        if (options.randomizePhase && delay > 0f)
            delay = Random.Range(0f, delay);

        if (delay > 0f)
            _startDelayCoroutine = StartCoroutine(StartMotionAfter(delay));
        else
            BeginMotion();
    }

    private IEnumerator StartMotionAfter(float delay)
    {
        yield return new WaitForSeconds(delay);
        _startDelayCoroutine = null;
        BeginMotion();
    }

    private void BeginMotion()
    {
        if (ContinuousMode)
        {
            _spinAngle = 0f;
            _orbitAngle = 0f;
            _continuousActive = true;
        }
        else
        {
            StartAnimationLoop();
        }
    }

    private void Update()
    {
        if (!_continuousActive || _rigidBody == null)
            return;

        var position = _initPosition;
        var rotation = _initRotation;

        // Orbit: revolve about a center placed so the authored position is a point on the circle
        // (angle 0 == _initPosition, so there's no start-of-motion jump).
        if (options.orbitEnabled)
        {
            _orbitAngle += options.orbitSpeed * Time.deltaTime;

            var axis = ToVector3(options.orbitAxis);
            if (axis.sqrMagnitude < 1e-6f)
                axis = Vector3.up;
            axis = axis.normalized;

            var radial = Vector3.Cross(axis, Vector3.up);
            if (radial.sqrMagnitude < 1e-6f)
                radial = Vector3.Cross(axis, Vector3.right);
            radial = radial.normalized;

            var arm = radial * options.orbitRadius;
            var center = _initPosition - arm;
            var offset = Quaternion.AngleAxis(_orbitAngle, axis) * arm;
            position = center + offset;

            if (options.orbitFacePath)
            {
                var tangent = Vector3.Cross(axis, offset);
                if (tangent.sqrMagnitude > 1e-6f)
                    rotation = Quaternion.LookRotation(tangent.normalized, axis);
            }
        }

        // Spinner composes on top of whatever rotation orbit produced.
        if (options.spinnerEnabled)
        {
            _spinAngle += options.spinSpeed * Time.deltaTime;

            var spinAxis = ToVector3(options.spinAxis);
            if (spinAxis.sqrMagnitude < 1e-6f)
                spinAxis = Vector3.up;
            rotation *= Quaternion.AngleAxis(_spinAngle, spinAxis.normalized);
        }

        MoveRigidBody(position, rotation);
    }

    private static Vector3 ToVector3(SerializableVector3 v)
    {
        return new Vector3(v.x, v.y, v.z);
    }

    private void StartAnimationLoop()
    {
        _animationCoroutine = StartCoroutine(AnimationLoop());
    }

    private IEnumerator AnimationLoop()
    {
        for (var i = 0; options.animationRepeats == 0 || i < options.animationRepeats; i++)
        {
            yield return PlayAnimation(_stepsCached, false);
            // Ping-pong: after the forward pass, retrace the visited poses back to the start so
            // one authored half-animation yields the return leg for free. Counts as one repeat.
            if (options.pingPong && _reversedCached.Length > 0)
                yield return PlayAnimation(_reversedCached, true);
        }
    }

    private IEnumerator PlayAnimation(Step[] stepsArray, bool isReverse)
    {
        // Warmup and the teleport-to-start snap only apply to the outbound pass; the return leg
        // should flow continuously from wherever the forward pass ended.
        if (!isReverse && options.animationWarmupDelay > 0)
            yield return new WaitForSeconds(options.animationWarmupDelay);

        for (var i = 0; i < stepsArray.Length; i++)
        {
            var step = stepsArray[i];
            if (step.Time <= 0f || (!isReverse && i == 0 && options.teleportToStart))
            {
                MoveRigidBody(step.Position, step.Rotation);
                yield return null;
            }
            else
            {
                if (step.Delay > 0)
                    yield return new WaitForSeconds(step.Delay);
                yield return MoveObject(step.Position, step.Rotation, step.Time);
            }
        }
    }

    // Build the return leg for ping-pong: retrace the poses s[n-2]..s[0], reusing the time/delay
    // of the forward hop that produced each next pose so the return mirrors the outbound timing.
    private static Step[] BuildReversed(Step[] forward)
    {
        if (forward.Length < 2)
            return System.Array.Empty<Step>();

        var reversed = new Step[forward.Length - 1];
        for (var k = 0; k < reversed.Length; k++)
        {
            var j = forward.Length - 2 - k;
            reversed[k] = new Step(forward[j].Position, forward[j].Rotation,
                forward[j + 1].Time, forward[j + 1].Delay);
        }

        return reversed;
    }

    private IEnumerator MoveObject(Vector3 targetPosition, Quaternion targetRotation, float duration)
    {
        var elapsed = 0f;
        var startPosition = transform.position;
        var startRotation = transform.rotation;

        while (elapsed < duration)
        {
            var t = Ease(options.easingMode, elapsed / duration);
            MoveRigidBody(Vector3.Lerp(startPosition, targetPosition, t),
                Quaternion.Lerp(startRotation, targetRotation, t));
            elapsed += Time.deltaTime;
            yield return null;
        }

        MoveRigidBody(targetPosition, targetRotation);
        yield return null;
    }

    private void MoveRigidBody(Vector3 targetPosition, Quaternion targetRotation)
    {
        _rigidBody.MovePosition(targetPosition);
        _rigidBody.MoveRotation(targetRotation);
    }

    // Remap a linear 0..1 interpolation parameter through the selected easing curve. Linear (the
    // default, value 0) is a no-op so existing maps are unaffected.
    private static float Ease(int mode, float t)
    {
        switch ((MO_Easing)mode)
        {
            case MO_Easing.SmoothStep:
                return t * t * (3f - 2f * t);
            case MO_Easing.EaseIn:
                return t * t;
            case MO_Easing.EaseOut:
                return t * (2f - t);
            case MO_Easing.EaseInOut:
                return t < 0.5f ? 2f * t * t : -1f + (4f - 2f * t) * t;
            default:
                return t;
        }
    }

    public void Restart(bool triggered = false)
    {
        Stop();

        if (waitForTrigger && !triggered)
            return;
        StartMotion();
    }

    public void Trigger()
    {
        switch ((MO_TriggerAction)options.triggerAction)
        {
            case MO_TriggerAction.Stop:
                StopAtCurrent();
                break;
            default: // Restart (also covers any unknown value)
                Restart(true);
                break;
        }
    }

    // Halt the running loop where it is, without snapping back to the start pose (unlike Stop()).
    // Used by the Stop trigger action so a "running → stop on trigger" object freezes in place.
    public void StopAtCurrent()
    {
        if (_animationCoroutine != null)
        {
            StopCoroutine(_animationCoroutine);
            _animationCoroutine = null;
        }

        // Freeze continuous motion where it is (no snap back), mirroring the keyframe behaviour.
        _continuousActive = false;
        CancelStartDelay();
    }

    private void CancelStartDelay()
    {
        if (_startDelayCoroutine == null)
            return;
        StopCoroutine(_startDelayCoroutine);
        _startDelayCoroutine = null;
    }

    private void Stop()
    {
        if (_animationCoroutine != null)
            StopCoroutine(_animationCoroutine);
        CancelStartDelay();

        _continuousActive = false;
        _spinAngle = 0f;
        _orbitAngle = 0f;

        // Snap the transform directly, not just the rigidbody. Setting position/rotation on a
        // kinematic Rigidbody is deferred to the next physics step, so transform.position would
        // still read the pre-reset (mid-animation) value this frame. The restarted loop samples
        // transform.position as its lerp start (see MoveObject), so without this immediate snap a
        // reset makes objects glide back from wherever they were instead of starting from the top.
        transform.SetPositionAndRotation(_initPosition, _initRotation);
        _rigidBody.position = _initPosition;
        _rigidBody.rotation = _initRotation;
    }

    public void OnDestroy()
    {
        Stop();
        Destroy(_rigidBody);
    }

    private struct Step
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly float Time;
        public readonly float Delay;

        private static Vector3 ToVector3(SerializableVector3 serializableVector3)
        {
            return new Vector3(serializableVector3.x, serializableVector3.y, serializableVector3.z);
        }

        public Step(MO_Animation animation)
        {
            Position = ToVector3(animation.position);
            Rotation = Quaternion.Euler(ToVector3(animation.rotation));
            Time = animation.time;
            Delay = animation.delay;
        }

        public Step(Vector3 position, Quaternion rotation, float time, float delay)
        {
            Position = position;
            Rotation = rotation;
            Time = time;
            Delay = delay;
        }
    }
}