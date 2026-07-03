using System;
using System.Collections.Generic;
using BepInEx.Logging;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace Liftoff.MovingObjects.Utils;

// The spawn primitive the mod never had: instantiate a live track item from a TrackBlueprint,
// the same capability the game's own track-load uses. Every prior copy/paste attempt stalled here
// because the mod only ever attached components to existing flags. The chain is:
//   TrackEditor.use                         -- the editor singleton
//     .GetTrackItemPrefab(itemID)           -- prefab for this item type
//     .CreateNewTrackItem(prefab)           -- instantiate a live item
//     .AssignIDToTrackItem(item)            -- give it a fresh instance id
//   item.ApplyBlueprint(blueprint)          -- apply transform/scale/config
// The prefab/item types are obfuscated (unnameable), but every member used here is public, so
// `var` lets us call them without naming the types. Everything is guarded so a wrong assumption
// fails gracefully instead of breaking the mod.
//
// NOTE: This is a research spike verified to compile against the game assembly; its runtime
// behaviour needs in-game playtest. Multi-object copy/paste, array/mirror, and save-to-file all
// build on SpawnFromBlueprint once it's confirmed working.
internal static class ItemSpawner
{
    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{PluginInfo.PLUGIN_NAME}.{nameof(ItemSpawner)}");

    // Instantiate a live item from the blueprint. Returns the item component, or null on failure.
    public static Component SpawnFromBlueprint(TrackBlueprint blueprint)
    {
        try
        {
            var editor = TrackEditor.use;
            if (editor == null)
            {
                Log.LogWarning("No active TrackEditor; can't spawn.");
                return null;
            }

            var prefab = editor.GetTrackItemPrefab(blueprint.itemID);
            if (prefab == null)
            {
                Log.LogWarning($"No prefab for item id '{blueprint.itemID}'.");
                return null;
            }

            var item = editor.CreateNewTrackItem(prefab);
            if (item == null)
            {
                Log.LogWarning($"CreateNewTrackItem returned null for '{blueprint.itemID}'.");
                return null;
            }

            editor.AssignIDToTrackItem(item);
            item.ApplyBlueprint(blueprint);
            return item;
        }
        catch (Exception e)
        {
            Log.LogError($"Spawn from blueprint failed: {e}");
            return null;
        }
    }

    // Duplicate an existing item's blueprint at an offset, carrying its MO configuration but a
    // fresh group id (so a duplicate never silently merges into the source's group).
    public static GameObject Duplicate(TrackBlueprint source, Vector3 offset)
    {
        var item = SpawnFromBlueprint(source);
        if (item == null)
            return null;

        item.transform.position += offset;

        var newBlueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(item);
        if (newBlueprint != null)
        {
            newBlueprint.mo_animationOptions = CloneUtils.DeepClone(source.mo_animationOptions);
            newBlueprint.mo_animationSteps = CloneUtils.DeepClone(source.mo_animationSteps);
            newBlueprint.mo_triggerOptions = CloneUtils.DeepClone(source.mo_triggerOptions);
            newBlueprint.mo_groupId = null;
        }

        return item.gameObject;
    }

    // Array (8f): stamp N duplicates, each offset a further step from the source. Same primitive
    // as Duplicate with an offset pattern; mirror and multi-object paste layer on the same way.
    public static void Array(TrackBlueprint source, int count, Vector3 step)
    {
        for (var i = 1; i <= count; i++)
            Duplicate(source, step * i);
    }

    // Multi-object paste: spawn a set of source blueprints translated so their centroid lands at
    // targetAnchor (the cursor/gizmo). Grouped items keep their grouping but under fresh group ids
    // so a pasted group never silently merges with the source group.
    public static void Paste(List<TrackBlueprint> sources, Vector3 sourceCentroid, Vector3 targetAnchor)
    {
        var groupMap = new Dictionary<string, string>();
        var delta = targetAnchor - sourceCentroid;

        foreach (var source in sources)
        {
            var bp = CloneUtils.DeepClone(source);
            bp.position = new SerializableVector3(ToVector3(source.position) + delta);
            bp.mo_groupId = RemapGroup(source.mo_groupId, groupMap);
            SpawnFromBlueprint(bp);
        }
    }

    // Mirror (8f): spawn a mirrored copy of the source blueprints reflected across the plane
    // through `pivot` with the given normal, under fresh group ids. Rotation is mirrored by
    // reflecting the forward/up vectors (a proper Quaternion can't encode the handedness flip, so
    // this is the standard best-effort mirror).
    public static void Mirror(List<TrackBlueprint> sources, Vector3 pivot, Vector3 normal)
    {
        normal = normal.normalized;
        var groupMap = new Dictionary<string, string>();

        foreach (var source in sources)
        {
            var bp = CloneUtils.DeepClone(source);

            var pos = pivot + Reflect(ToVector3(source.position) - pivot, normal);
            var rot = Quaternion.Euler(ToVector3(source.rotation));
            var mirrored = Quaternion.LookRotation(Reflect(rot * Vector3.forward, normal),
                Reflect(rot * Vector3.up, normal));

            bp.position = new SerializableVector3(pos);
            bp.rotation = new SerializableVector3(mirrored.eulerAngles);
            bp.mo_groupId = RemapGroup(source.mo_groupId, groupMap);
            SpawnFromBlueprint(bp);
        }
    }

    // Centroid of a set of blueprint positions — the anchor a copied selection pastes relative to.
    public static Vector3 Centroid(List<TrackBlueprint> blueprints)
    {
        if (blueprints.Count == 0)
            return Vector3.zero;

        var sum = Vector3.zero;
        foreach (var bp in blueprints)
            sum += ToVector3(bp.position);
        return sum / blueprints.Count;
    }

    private static string RemapGroup(string oldId, Dictionary<string, string> map)
    {
        if (string.IsNullOrEmpty(oldId))
            return null;
        if (!map.TryGetValue(oldId, out var newId))
        {
            newId = Guid.NewGuid().ToString("D");
            map[oldId] = newId;
        }

        return newId;
    }

    private static Vector3 Reflect(Vector3 v, Vector3 normal)
    {
        return v - 2f * Vector3.Dot(v, normal) * normal;
    }

    private static Vector3 ToVector3(SerializableVector3 v)
    {
        return new Vector3(v.x, v.y, v.z);
    }
}
