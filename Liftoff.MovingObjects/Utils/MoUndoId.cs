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
    // Return the item's undo id, creating one on first use.
    public static string Ensure(Component flag)
    {
        if (flag == null)
            return null;
        var marker = flag.gameObject.GetComponent<MoUndoId>() ?? flag.gameObject.AddComponent<MoUndoId>();
        if (string.IsNullOrEmpty(marker.id))
            marker.id = System.Guid.NewGuid().ToString("D");
        return marker.id;
    }

    // Re-stamp a specific id onto a (re-spawned) item so existing edits keep resolving to it.
    public static void Attach(Component flag, string id)
    {
        if (flag == null || string.IsNullOrEmpty(id))
            return;
        var marker = flag.gameObject.GetComponent<MoUndoId>() ?? flag.gameObject.AddComponent<MoUndoId>();
        marker.id = id;
    }

    // Find the live flag currently carrying this id, or null. Scan-based on purpose — a cached
    // dictionary would hold destroyed Unity objects after a respawn. FindAllFlags is the mod's
    // canonical "all live items" query.
    public static Component Resolve(string id)
    {
        if (string.IsNullOrEmpty(id))
            return null;
        foreach (var flag in EditorUtils.FindAllFlags())
        {
            var marker = flag.GetComponent<MoUndoId>();
            if (marker != null && marker.id == id)
                return flag;
        }

        return null;
    }
}
