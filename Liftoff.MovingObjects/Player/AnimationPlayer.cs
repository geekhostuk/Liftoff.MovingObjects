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

    public MO_AnimationOptions options;
    public List<MO_Animation> steps;
    public bool waitForTrigger;

    private void Start()
    {
        _stepsCached = steps.Select(animation => new Step(animation)).ToArray();
        _rigidBody = gameObject.AddComponent<Rigidbody>();
        _rigidBody.isKinematic = true;

        _initPosition = transform.position;
        _initRotation = transform.rotation;

        if (!waitForTrigger)
            StartAnimationLoop();
    }

    private void StartAnimationLoop()
    {
        _animationCoroutine = StartCoroutine(AnimationLoop());
    }

    private IEnumerator AnimationLoop()
    {
        for (var i = 0; options.animationRepeats == 0 || i < options.animationRepeats; i++)
            yield return PlayAnimation();
    }

    private IEnumerator PlayAnimation()
    {
        if (options.animationWarmupDelay > 0)
            yield return new WaitForSeconds(options.animationWarmupDelay);

        for (var i = 0; i < _stepsCached.Length; i++)
        {
            var step = _stepsCached[i];
            if (step.Time <= 0f || (i == 0 && options.teleportToStart))
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
        StartAnimationLoop();
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
    }

    private void Stop()
    {
        if (_animationCoroutine != null)
            StopCoroutine(_animationCoroutine);

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
    }
}