using System.Collections.Generic;
using UnityEngine;

namespace Liftoff.MovingObjects;

// Attached to each drone rigidbody. Records the drone's position at the start of each physics
// step. TriggerBehavior uses the previous->current segment to sweep-test against trigger volumes,
// which catches fast passes that tunnel straight through a trigger between two physics steps and
// would otherwise never raise OnTriggerEnter.
//
// It deliberately leaves the rigidbody's collisionDetectionMode alone. Up to 1.3.11 it forced
// ContinuousSpeculative every step, on every track. Speculative contacts make "ghost collisions":
// a fast drone bounces off colliders it only passes close to, such as a gate edge. It also changed
// how the drone collides for everyone with the mod, even on tracks with nothing of ours in them.
// Speculative detection doesn't raise trigger events for a pass it predicts, so the swept check
// was always what kept triggers reliable.
internal class DroneTrajectory : MonoBehaviour
{
    // All currently-enabled drone samplers. Maintained via OnEnable/OnDisable so it never
    // holds destroyed drones (the game spawns a fresh drone object on every reset).
    internal static readonly List<DroneTrajectory> Active = new();

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

        // Sample the trajectory once per physics step. Positions read in FixedUpdate are the
        // start-of-step positions (integration happens after all FixedUpdates), so consecutive
        // samples form the drone's actual path between steps.
        PreviousPosition = CurrentPosition;
        CurrentPosition = _body.position;
    }
}
