using System.Collections.Generic;
using UnityEngine;

namespace Liftoff.MovingObjects;

// Attached to each drone rigidbody. Two jobs:
//
// 1. Keep the rigidbody in continuous (speculative) collision detection. A one-shot set
//    is not enough: the game rebuilds/reconfigures the drone around reset time and resets
//    collisionDetectionMode back to Discrete, so we re-assert it every FixedUpdate.
//
// 2. Record the drone's position at the start of each physics step. TriggerBehavior uses
//    the previous->current segment to sweep-test against trigger volumes, which catches
//    fast passes that tunnel straight through a trigger between two physics steps and would
//    otherwise never raise OnTriggerEnter. Continuous detection alone is not reliable enough
//    for trigger overlaps in this build, so the swept check is the actual guarantee.
internal class DroneContinuousCollision : MonoBehaviour
{
    // All currently-enabled drone watchdogs. Maintained via OnEnable/OnDisable so it never
    // holds destroyed drones (the game spawns a fresh drone object on every reset).
    internal static readonly List<DroneContinuousCollision> Active = new();

    private Rigidbody _body;

    public Rigidbody Body => _body;
    public Vector3 PreviousPosition { get; private set; }
    public Vector3 CurrentPosition { get; private set; }

    private void Awake()
    {
        _body = GetComponent<Rigidbody>();
        CurrentPosition = PreviousPosition = _body != null ? _body.position : transform.position;
    }

    private void OnEnable()
    {
        if (!Active.Contains(this))
            Active.Add(this);
    }

    // Collapse the recorded trajectory to a single point. Called after a teleport moves the drone:
    // without this the next physics step samples a previous->current segment that spans the whole
    // teleport jump (entrance to exit), which the swept trigger check in TriggerBehavior would treat
    // as a real fast pass and spuriously re-fire triggers (re-teleporting / re-rotating the drone).
    public void ResetTrajectory()
    {
        CurrentPosition = PreviousPosition = _body != null ? _body.position : transform.position;
    }

    private void OnDisable()
    {
        Active.Remove(this);
    }

    private void FixedUpdate()
    {
        if (_body == null)
        {
            _body = GetComponent<Rigidbody>();
            if (_body == null)
                return;
        }

        if (_body.collisionDetectionMode != CollisionDetectionMode.ContinuousSpeculative)
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // Sample the trajectory once per physics step. Positions read in FixedUpdate are the
        // start-of-step positions (integration happens after all FixedUpdates), so consecutive
        // samples form the drone's actual path between steps.
        PreviousPosition = CurrentPosition;
        CurrentPosition = _body.position;
    }
}
