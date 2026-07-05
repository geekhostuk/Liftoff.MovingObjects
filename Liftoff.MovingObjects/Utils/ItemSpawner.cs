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
        ApplyMoConfig(item, source, null);
        return item.gameObject;
    }

    // Copy the mod's injected config onto a freshly spawned item's OWN blueprint. The game's
    // ApplyBlueprint only knows the stock track fields, so the mo_* fields (animation / trigger /
    // group id) never ride along on spawn — they have to be written here or the paste silently drops
    // every object's animation, trigger, and grouping. Deep-cloned so repeated spawns from one source
    // don't share config objects. groupId is passed in (remapped to a fresh group, or null).
    private static void ApplyMoConfig(Component item, TrackBlueprint source, string groupId)
    {
        var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(item);
        if (blueprint == null)
            return;

        blueprint.mo_animationOptions = CloneUtils.DeepClone(source.mo_animationOptions);
        blueprint.mo_animationSteps = CloneUtils.DeepClone(source.mo_animationSteps);
        blueprint.mo_triggerOptions = CloneUtils.DeepClone(source.mo_triggerOptions);
        blueprint.mo_groupId = groupId;
    }

    // Array (8f): stamp N duplicates, each offset a further step from the source. Same primitive
    // as Duplicate with an offset pattern; mirror and multi-object paste layer on the same way.
    public static void Array(TrackBlueprint source, int count, Vector3 step)
    {
        for (var i = 1; i <= count; i++)
            Duplicate(source, step * i);
    }

    // Multi-object paste: spawn a set of items translated so their captured centroid lands at
    // targetAnchor (the cursor/gizmo). The final world transform is set explicitly after spawn
    // from each item's captured live transform — the first cut wrote blueprint.position and trusted
    // ApplyBlueprint to place it, but those fields are only synced at track save/load, so mid-edit
    // they're stale and every item collapsed onto the anchor with default orientation. Grouped items
    // keep their grouping but under fresh group ids so a pasted group never silently merges with the
    // source group.
    public static void Paste(List<PlacedItem> sources, Vector3 sourceCentroid, Vector3 targetAnchor,
        float gridStep)
    {
        var groupMap = new Dictionary<string, string>();

        // Snap the whole-selection translation to the grid so pasted items keep the alignment the
        // source had. An even-count selection's centroid sits half a cell off-grid, so anchoring the
        // centroid straight onto the (off-grid) gizmo drops the paste slightly out of alignment.
        // Quantizing only the translation preserves each item's exact relative layout AND grid phase.
        // gridStep 0 = free placement, no snap.
        var delta = GridUtils.RoundVectorToStep(targetAnchor - sourceCentroid, gridStep);

        foreach (var source in sources)
        {
            var item = SpawnFromBlueprint(CloneUtils.DeepClone(source.blueprint));
            if (item == null)
                continue;

            item.transform.SetPositionAndRotation(source.position + delta, source.rotation);
            if (source.scale.HasValue)
                item.transform.localScale = source.scale.Value;

            ApplyMoConfig(item, source.blueprint, RemapGroup(source.blueprint.mo_groupId, groupMap));
        }
    }

    // Mirror (8f): spawn a mirrored copy of the source items reflected across the plane through
    // `pivot` with the given normal, under fresh group ids. Rotation is mirrored by reflecting the
    // forward/up vectors (a proper Quaternion can't encode the handedness flip, so this is the
    // standard best-effort mirror). As with Paste, the world transform is set on the live object.
    public static void Mirror(List<PlacedItem> sources, Vector3 pivot, Vector3 normal)
    {
        normal = normal.normalized;
        var groupMap = new Dictionary<string, string>();

        foreach (var source in sources)
        {
            var item = SpawnFromBlueprint(CloneUtils.DeepClone(source.blueprint));
            if (item == null)
                continue;

            var pos = pivot + Reflect(source.position - pivot, normal);
            var mirrored = Quaternion.LookRotation(Reflect(source.rotation * Vector3.forward, normal),
                Reflect(source.rotation * Vector3.up, normal));

            item.transform.SetPositionAndRotation(pos, mirrored);
            if (source.scale.HasValue)
                item.transform.localScale = source.scale.Value;

            ApplyMoConfig(item, source.blueprint, RemapGroup(source.blueprint.mo_groupId, groupMap));
        }
    }

    // Centroid of a set of captured item positions — the anchor a copied selection pastes relative to.
    public static Vector3 Centroid(List<PlacedItem> items)
    {
        if (items.Count == 0)
            return Vector3.zero;

        var sum = Vector3.zero;
        foreach (var item in items)
            sum += item.position;
        return sum / items.Count;
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
}

// A blueprint paired with the world transform to place it at. Copy/mirror capture each selected
// item's LIVE transform (position/rotation/localScale) rather than the blueprint's own position/
// rotation fields, which are only synced to the object at track save/load and are stale mid-edit.
// Stamps loaded from disk have no live object, so they fall back to the blueprint's serialized
// fields via FromBlueprint (scale null → leave whatever the spawned prefab uses).
internal struct PlacedItem
{
    public TrackBlueprint blueprint;
    public Vector3 position;
    public Quaternion rotation;
    public Vector3? scale;

    public static PlacedItem FromLive(TrackBlueprint blueprint, Transform transform)
    {
        return new PlacedItem
        {
            blueprint = blueprint,
            position = transform.position,
            rotation = transform.rotation,
            scale = transform.localScale,
        };
    }

    public static PlacedItem FromBlueprint(TrackBlueprint blueprint)
    {
        return new PlacedItem
        {
            blueprint = blueprint,
            position = new Vector3(blueprint.position.x, blueprint.position.y, blueprint.position.z),
            rotation = Quaternion.Euler(blueprint.rotation.x, blueprint.rotation.y, blueprint.rotation.z),
            scale = null,
        };
    }
}
