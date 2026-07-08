using System.Reflection;
using UnityEngine;

namespace Liftoff.MovingObjects;

// Attached to an animated/physics object when killOnContact is set. Crashes the drone when it
// touches the object, turning moving platforms (swinging blades, crushing walls) into genuine
// hazards rather than scenery. Uses the game's FlightManager.CrashDrone, which is non-public and
// so driven by reflection.
internal class HazardContact : MonoBehaviour
{
    private static readonly MethodInfo CrashMethod = typeof(FlightManager)
        .GetMethod("CrashDrone", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    private int _droneLayer;

    private void Start()
    {
        _droneLayer = LayerMask.NameToLayer("Drone");
    }

    private void OnCollisionEnter(Collision collision)
    {
        TryKill(collision.gameObject);
    }

    private void OnTriggerEnter(Collider other)
    {
        TryKill(other.gameObject);
    }

    private void TryKill(GameObject other)
    {
        if (other.layer != _droneLayer || CrashMethod == null)
            return;

        var flightManager = Plugin.HookedFlightManager != null
            ? Plugin.HookedFlightManager
            : FindObjectOfType<FlightManager>();
        if (flightManager != null)
            CrashMethod.Invoke(flightManager, null);
    }
}
