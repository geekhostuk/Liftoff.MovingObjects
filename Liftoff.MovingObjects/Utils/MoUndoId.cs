using System.Collections.Generic;
using UnityEngine;

namespace Liftoff.MovingObjects.Utils;

// A stable, runtime-only identity for a placed track item, used by the undo/redo history.
//
// Undo can't hold live Component/GameObject references across an undo->redo cycle: undoing a delete
// re-spawns a BRAND NEW GameObject and blueprint (see ItemSpawner.SpawnExact), so any cached
// reference to the old object is a destroyed Unity object. Instead every tracked item carries this
// marker with a Guid; edits store the id and resolve the current live flag by scanning
// EditorUtils.FindAllFlags at apply time. On re-spawn the SAME id is re-attached (AttachUndoId), so
// a later redo/move/group edit still finds the object.
//
// Like GroupSelectionInfo (see PlacementUtilsWindow), this is a plain MonoBehaviour and is invisible
// to the game's save: the track is serialised from Track.blueprints (TrackBlueprint list), not from
// scene components, so tagging the flag GameObject never reaches disk.
internal sealed class MoUndoId : MonoBehaviour
{
    public string id;
}

internal static class UndoId
{
    // id -> the live flag currently carrying it. This is the hot-path accelerator for Resolve: a
    // group move commits by re-resolving every affected item's id, and the numeric fields commit a
    // move on almost every keystroke, so the old "scan FindAllFlags on every Resolve" cost 7 whole-
    // scene FindObjectsOfType sweeps PER ITEM PER EDIT — an O(scene) hitch on every nudge that made
    // the editor unusable on large maps (Honk, v1.3.8).
    //
    // The comment this replaces feared a cache would hold destroyed objects after a respawn. It can't:
    // a destroyed Unity object compares == null (Unity's overloaded operator), so Resolve treats a
    // dead entry as a miss and falls back to the canonical scan — which re-seeds the whole cache in
    // one pass, so a burst of post-respawn resolves still pays only a single scan.
    private static readonly Dictionary<string, Component> _cache = new();

    // Return the item's undo id, creating one on first use.
    public static string Ensure(Component flag)
    {
        if (flag == null)
            return null;
        var marker = flag.gameObject.GetComponent<MoUndoId>() ?? flag.gameObject.AddComponent<MoUndoId>();
        if (string.IsNullOrEmpty(marker.id))
            marker.id = System.Guid.NewGuid().ToString("D");
        _cache[marker.id] = flag;
        return marker.id;
    }

    // Re-stamp a specific id onto a (re-spawned) item so existing edits keep resolving to it.
    public static void Attach(Component flag, string id)
    {
        if (flag == null || string.IsNullOrEmpty(id))
            return;
        var marker = flag.gameObject.GetComponent<MoUndoId>() ?? flag.gameObject.AddComponent<MoUndoId>();
        marker.id = id;
        _cache[id] = flag;
    }

    // Find the live flag currently carrying this id, or null.
    public static Component Resolve(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;

        // Fast path: a cached flag that's still alive (== null catches destroyed objects) and still
        // carries this id (guards against the id having been re-stamped elsewhere).
        if (_cache.TryGetValue(id, out var cached) && cached != null)
        {
            var cachedMarker = cached.GetComponent<MoUndoId>();
            if (cachedMarker != null && cachedMarker.id == id)
                return cached;
        }

        // Miss or stale entry: one scan repairs the whole cache, so a run of resolves right after a
        // respawn/load pays a single scan rather than one each.
        Component match = null;
        foreach (var flag in EditorUtils.FindAllFlags())
        {
            var marker = flag.GetComponent<MoUndoId>();
            if (marker == null || string.IsNullOrEmpty(marker.id))
                continue;
            _cache[marker.id] = flag;
            if (marker.id == id)
                match = flag;
        }

        if (match == null)
            _cache.Remove(id);
        return match;
    }

    // Drop all cached entries. Called when a new editor session starts so ids from a previous track
    // (now destroyed) don't linger.
    public static void Clear()
    {
        _cache.Clear();
    }
}
