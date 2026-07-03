using System;
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
}
