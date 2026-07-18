using System;
using System.Collections.Generic;
using BepInEx.Logging;
using Liftoff.MovingObjects.Utils;
using UnityEngine;
using Logger = BepInEx.Logging.Logger;

namespace Liftoff.MovingObjects;

// Editor-wide undo/redo for the track builder.
//
// Every reversible edit is captured as an IUndoEdit that stores DATA (deep-cloned blueprint
// snapshots + transforms + stable MoUndoId ids), never live object references — undoing a delete
// re-spawns brand-new GameObjects, so references would dangle. Edits resolve the current live item
// by id at apply time (see UndoId.Resolve).
//
// Capture is uniform across the game's native edits and the mod's own operations because both funnel
// through the same chokepoints: TrackEditor.AssignIDToTrackItem for every add and
// TrackEditor.RemoveTrackItem for every remove (patched in Plugin). Object moves come from the drag
// postfixes and the numeric transform fields.
//
// Philosophy matches the rest of the mod: everything is guarded and fails safe to a no-op — a missed
// or malformed edit never breaks the editor.
internal static class UndoHistory
{
    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{PluginInfo.PLUGIN_NAME}.{nameof(UndoHistory)}");

    private const int MaxDepth = 50;

    // Consecutive transform tweaks on the same object within this window fold into one undo step, so
    // a gizmo drag followed by a couple of numeric nudges is a single Ctrl+Z.
    private const float CoalesceWindow = 0.6f;

    // A single-frame burst of un-bracketed adds this large can only be a track load (mod bulk ops are
    // always bracketed in a transaction; a native palette placement is exactly one item). Dropped so
    // opening a map doesn't seed a giant "undo the whole track" entry.
    private const int LoadBurstThreshold = 8;

    // After a new editor session (TrackEditorGUI.Start) the game spawns every item on the loaded
    // track; suppress capture for a short window, ended early on the first calm frame.
    private const int LoadWindowFrameBudget = 30;

    private static readonly List<IUndoEdit> _undo = new();
    private static readonly Stack<IUndoEdit> _redo = new();

    // >0 while we are applying an undo/redo (or otherwise want capture silenced) so the respawns and
    // removals it performs don't record new history.
    private static int _suppressDepth;
    private static bool Suppressed => _suppressDepth > 0;

    // Non-null while inside a mod bulk operation: its adds/removes accumulate here and commit as ONE
    // edit. Un-bracketed (native) events buffer in the pending lists and flush once per frame.
    //
    // Adds buffer LIVE FLAG references, not snapshots: an item's final transform and mo_* config are
    // set AFTER it registers through AssignIDToTrackItem (see ItemSpawner.Paste), so we must snapshot
    // it later — at transaction commit / frame flush — when it is fully configured, or a redo would
    // recreate it bare at the origin. Removes are snapshotted immediately (in the RemoveTrackItem
    // prefix), which is the only moment the item still exists.
    private static Batch _open;
    private static readonly List<Component> _pendingAddFlags = new();
    private static readonly List<TrackedSnapshot> _pendingRemoves = new();

    private static int _loadWindowFrames;

    // Move capture is done by watching the currently selected item's transform each frame, rather than
    // by hooking a specific drag behaviour — moving a placed item can happen via the transform gizmo,
    // the numeric fields, or a drag behaviour, and only polling the transform catches all of them
    // uniformly. When the selected item's pose starts changing we snapshot the "before" (of it and its
    // group members), then commit a MoveEdit once the pose settles.
    private static string _watchId;
    private static TransformPose _watchLast;
    private static bool _moveActive;
    private static int _watchStillFrames;
    private static List<MoveSample> _moveSamples;

    // Set after an undo/redo so the transform changes IT caused aren't re-detected as a new user move;
    // the next WatchSelection call rebaselines instead of detecting.
    private static bool _watchRebaseline;

    // Idle frames of no pose change before a move is considered finished and committed.
    private const int MoveSettleFrames = 2;

    private static float _lastMovePushTime;

    // ---- public API -------------------------------------------------------------------------

    // Postfix on TrackEditor.AssignIDToTrackItem: an item was added (native placement or mod spawn).
    // Buffer the live flag; it's snapshotted later once fully configured (see the field comment).
    public static void NotifyAdded(Component flag)
    {
        if (Suppressed || flag == null)
            return;
        (_open?.addFlags ?? _pendingAddFlags).Add(flag);
    }

    // Prefix on TrackEditor.RemoveTrackItem: an item is about to be destroyed — snapshot it now, while
    // its GameObject/transform are still valid.
    public static void NotifyRemoving(Component flag)
    {
        if (Suppressed || flag == null)
            return;
        var snapshot = SnapshotOf(flag);
        if (snapshot.HasValue)
            (_open?.removes ?? _pendingRemoves).Add(snapshot.Value);
    }

    // Bracket a mod bulk operation so its many spawns/removes become one undo entry.
    public static IDisposable Transaction(string label)
    {
        if (Suppressed || _open != null)
            return NoOpScope; // never nest; a nested op just rides the outer batch's frame flush
        _open = new Batch { label = label };
        return new Scope(CommitTransaction);
    }

    // Called first thing each frame (PlacementUtilsWindow.Update) to commit native single edits.
    public static void FlushPending()
    {
        if (_open != null || Suppressed)
            return;

        // Post-load suppression window: drop everything the load spawned, end early once calm.
        if (_loadWindowFrames > 0)
        {
            var quiet = _pendingAddFlags.Count == 0 && _pendingRemoves.Count == 0;
            _pendingAddFlags.Clear();
            _pendingRemoves.Clear();
            _loadWindowFrames = quiet ? 0 : _loadWindowFrames - 1;
            return;
        }

        if (_pendingAddFlags.Count == 0 && _pendingRemoves.Count == 0)
            return;

        // Safety net for a mid-session load that didn't re-run Start: a big un-bracketed add burst is
        // a load, not a user action, so drop it rather than record it.
        if (_pendingRemoves.Count == 0 && _pendingAddFlags.Count >= LoadBurstThreshold)
        {
            _pendingAddFlags.Clear();
            return;
        }

        if (_pendingRemoves.Count > 0)
            Push(new RemoveEdit(new List<TrackedSnapshot>(_pendingRemoves)));
        var adds = SnapshotFlags(_pendingAddFlags);
        if (adds.Count > 0)
            Push(new AddEdit(adds));

        _pendingAddFlags.Clear();
        _pendingRemoves.Clear();
    }

    // Called every frame with the currently selected item's transform (or null when nothing is
    // selected). Detects when the item starts moving, captures the pre-move pose of it and its group,
    // and commits a MoveEdit once the movement settles. Mechanism-agnostic: works for the transform
    // gizmo, the numeric fields, and drag behaviours alike.
    public static void WatchSelection(Transform selectedTransform)
    {
        if (Suppressed)
            return;

        var flag = selectedTransform != null
            ? EditorUtils.FindFlagInParent(selectedTransform.gameObject)
            : null;

        // Rebaseline after an undo/redo so the change it applied isn't captured as a fresh move.
        if (_watchRebaseline)
        {
            _watchRebaseline = false;
            _moveActive = false;
            _moveSamples = null;
            _watchStillFrames = 0;
            _watchId = flag != null ? UndoId.Ensure(flag) : null;
            _watchLast = flag != null ? TransformPose.Of(flag.transform) : default;
            return;
        }

        if (flag == null)
        {
            if (_moveActive)
                CommitMove();
            _watchId = null;
            return;
        }

        var id = UndoId.Ensure(flag);
        var pose = TransformPose.Of(flag.transform);

        // Selection changed: commit any in-progress move on the previous item, then track the new one.
        if (id != _watchId)
        {
            if (_moveActive)
                CommitMove();
            _watchId = id;
            _watchLast = pose;
            _moveActive = false;
            _watchStillFrames = 0;
            return;
        }

        if (!pose.Approximately(_watchLast))
        {
            // Movement this frame. On the first frame of a move, snapshot the "before" of the whole
            // affected set (the item, plus its group members which follow it).
            if (!_moveActive)
            {
                _moveActive = true;
                _moveSamples = CaptureAffected(flag);
            }

            _watchLast = pose;
            _watchStillFrames = 0;
        }
        else if (_moveActive)
        {
            _watchStillFrames++;
            if (_watchStillFrames >= MoveSettleFrames)
                CommitMove();
        }
    }

    public static void Undo()
    {
        if (_undo.Count == 0)
        {
            ShowTextOverlay.Instance.Show("Nothing to undo", 1f);
            return;
        }

        var edit = _undo[_undo.Count - 1];
        _undo.RemoveAt(_undo.Count - 1);
        using (Suppress())
        {
            try { edit.Undo(); }
            catch (Exception ex) { Log.LogWarning($"Undo '{edit.Label}' failed: {ex.Message}"); }
        }

        _redo.Push(edit);
        _watchRebaseline = true;
        Shared.Editor.RequestRefreshGui();
        ShowTextOverlay.Instance.Show($"Undo: {edit.Label}", 1f);
    }

    public static void Redo()
    {
        if (_redo.Count == 0)
        {
            ShowTextOverlay.Instance.Show("Nothing to redo", 1f);
            return;
        }

        var edit = _redo.Pop();
        using (Suppress())
        {
            try { edit.Redo(); }
            catch (Exception ex) { Log.LogWarning($"Redo '{edit.Label}' failed: {ex.Message}"); }
        }

        AppendToUndo(edit); // do NOT go through Push — that would clear the redo stack
        _watchRebaseline = true;
        Shared.Editor.RequestRefreshGui();
        ShowTextOverlay.Instance.Show($"Redo: {edit.Label}", 1f);
    }

    // New editor session: clear history and arm the post-load suppression window.
    public static void ResetForNewSession()
    {
        _undo.Clear();
        _redo.Clear();
        _pendingAddFlags.Clear();
        _pendingRemoves.Clear();
        _watchId = null;
        _moveActive = false;
        _moveSamples = null;
        _watchStillFrames = 0;
        _watchRebaseline = false;
        _open = null;
        _suppressDepth = 0;
        _loadWindowFrames = LoadWindowFrameBudget;
        UndoId.Clear();
    }

    // ---- internals --------------------------------------------------------------------------

    private static void CommitTransaction()
    {
        var batch = _open;
        _open = null;
        if (batch == null)
            return;

        if (batch.removes.Count > 0)
            Push(new RemoveEdit(batch.removes, batch.label));
        var adds = SnapshotFlags(batch.addFlags);
        if (adds.Count > 0)
            Push(new AddEdit(adds, batch.label));
    }

    // Snapshot a set of just-added live flags (now fully configured with their final transform and
    // mo_* config). Destroyed/invalid flags are skipped.
    private static List<TrackedSnapshot> SnapshotFlags(List<Component> flags)
    {
        var snapshots = new List<TrackedSnapshot>();
        foreach (var flag in flags)
        {
            var snapshot = SnapshotOf(flag);
            if (snapshot.HasValue)
                snapshots.Add(snapshot.Value);
        }

        return snapshots;
    }

    // Record a brand-new edit: coalesce consecutive single-item moves of the same object (e.g. a run
    // of numeric nudges), trim to depth, invalidate redo.
    private static void Push(IUndoEdit edit)
    {
        if (edit is MoveEdit move && move.SingleId != null
            && _undo.Count > 0 && _undo[_undo.Count - 1] is MoveEdit prev && prev.SingleId == move.SingleId
            && Time.unscaledTime - _lastMovePushTime < CoalesceWindow)
        {
            prev.MergeAfter(move);
        }
        else
        {
            AppendToUndo(edit);
        }

        if (edit is MoveEdit)
            _lastMovePushTime = Time.unscaledTime;

        _redo.Clear();
    }

    private static void AppendToUndo(IUndoEdit edit)
    {
        _undo.Add(edit);
        if (_undo.Count > MaxDepth)
            _undo.RemoveAt(0);
    }

    // Snapshot the pre-move poses of the item being moved plus, if it's grouped, its group members
    // (which ride along with it). The moved item's own "before" is the last still pose (previous
    // frame, accurate); members are captured at first movement (a frame in — negligibly off).
    private static List<MoveSample> CaptureAffected(Component flag)
    {
        var samples = new List<MoveSample> { new MoveSample { id = _watchId, before = _watchLast } };

        var blueprint = GetBlueprint(flag);
        if (blueprint != null && !string.IsNullOrEmpty(blueprint.mo_groupId))
        {
            foreach (var member in EditorUtils.FindFlagsByGroupId(blueprint.mo_groupId))
            {
                var memberId = UndoId.Ensure(member);
                if (memberId == _watchId)
                    continue;
                samples.Add(new MoveSample { id = memberId, before = TransformPose.Of(member.transform) });
            }
        }

        return samples;
    }

    // Movement has settled: push one MoveEdit for whatever actually changed pose.
    private static void CommitMove()
    {
        var samples = _moveSamples;
        _moveActive = false;
        _moveSamples = null;
        _watchStillFrames = 0;
        if (samples == null)
            return;

        var changes = new List<MoveChange>();
        foreach (var sample in samples)
        {
            var flag = UndoId.Resolve(sample.id);
            if (flag == null)
                continue;
            var after = TransformPose.Of(flag.transform);
            if (!sample.before.Approximately(after))
                changes.Add(new MoveChange { id = sample.id, before = sample.before, after = after });
        }

        if (changes.Count > 0)
            Push(new MoveEdit(changes));
    }

    private static TrackedSnapshot? SnapshotOf(Component flag)
    {
        var blueprint = GetBlueprint(flag);
        if (blueprint == null)
            return null;
        return new TrackedSnapshot
        {
            id = UndoId.Ensure(flag),
            item = PlacedItem.FromLive(CloneUtils.DeepClone(blueprint), flag.transform)
        };
    }

    // Re-create a captured item and re-stamp its id so later edits keep resolving to it.
    private static void Respawn(TrackedSnapshot snapshot)
    {
        var item = ItemSpawner.SpawnExact(snapshot.item);
        if (item == null)
            return; // guarded: undo of a delete simply doesn't reappear rather than corrupt the track
        UndoId.Attach(item, snapshot.id);
    }

    private static void Despawn(string id)
    {
        var flag = UndoId.Resolve(id);
        if (flag != null)
            ItemSpawner.RemoveItem(flag);
    }

    private static void SetGroup(string id, string groupId)
    {
        var flag = UndoId.Resolve(id);
        var blueprint = GetBlueprint(flag);
        if (blueprint == null)
            return;
        blueprint.mo_groupId = groupId;
        // Ungrouping unparents (mirrors HandleEnchantedKeys); the flight-time GroupFlags rebuilds
        // parenting for a set group id, so we only need to break it here.
        if (string.IsNullOrEmpty(groupId))
            flag.transform.parent = null;
    }

    private static TrackBlueprint GetBlueprint(Component flag)
    {
        if (flag == null)
            return null;
        try { return ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag); }
        catch { return null; }
    }

    private static IDisposable Suppress()
    {
        _suppressDepth++;
        return new Scope(() => _suppressDepth--);
    }

    // ---- edit records -----------------------------------------------------------------------

    private interface IUndoEdit
    {
        string Label { get; }
        void Undo();
        void Redo();
    }

    // One item's identity plus its full captured state.
    private struct TrackedSnapshot
    {
        public string id;
        public PlacedItem item;
    }

    private readonly struct TransformPose
    {
        public readonly Vector3 Position;
        public readonly Quaternion Rotation;
        public readonly Vector3 Scale;

        private TransformPose(Vector3 position, Quaternion rotation, Vector3 scale)
        {
            Position = position;
            Rotation = rotation;
            Scale = scale;
        }

        public static TransformPose Of(Transform t) => new(t.position, t.rotation, t.localScale);

        public bool Approximately(TransformPose other) =>
            Position == other.Position && Rotation == other.Rotation && Scale == other.Scale;
    }

    // Items were created: undo removes them, redo re-creates them.
    private sealed class AddEdit : IUndoEdit
    {
        private readonly List<TrackedSnapshot> _items;
        private readonly string _label;
        public AddEdit(List<TrackedSnapshot> items, string label = null)
        {
            _items = items;
            _label = label;
        }

        public string Label => _label ?? (_items.Count > 1 ? $"Add {_items.Count}" : "Add");

        public void Undo()
        {
            foreach (var s in _items)
                Despawn(s.id);
        }

        public void Redo()
        {
            foreach (var s in _items)
                Respawn(s);
        }
    }

    // Items were destroyed: undo re-creates them, redo removes them (exact inverse of AddEdit).
    private sealed class RemoveEdit : IUndoEdit
    {
        private readonly List<TrackedSnapshot> _items;
        private readonly string _label;
        public RemoveEdit(List<TrackedSnapshot> items, string label = null)
        {
            _items = items;
            _label = label;
        }

        public string Label => _label ?? (_items.Count > 1 ? $"Delete {_items.Count}" : "Delete");

        public void Undo()
        {
            foreach (var s in _items)
                Respawn(s);
        }

        public void Redo()
        {
            foreach (var s in _items)
                Despawn(s.id);
        }
    }

    // The pre-move snapshot of one affected item.
    private struct MoveSample
    {
        public string id;
        public TransformPose before;
    }

    // One affected item's full before/after for a committed move.
    private struct MoveChange
    {
        public string id;
        public TransformPose before;
        public TransformPose after;
    }

    // One or more objects moved together (gizmo drag, numeric fields, or a group move). Each affected
    // item resolves live by id and is restored to its before/after pose.
    private sealed class MoveEdit : IUndoEdit
    {
        private readonly List<MoveChange> _changes;

        public MoveEdit(List<MoveChange> changes) => _changes = changes;

        public string Label => _changes.Count > 1 ? $"Move {_changes.Count}" : "Move";

        // The single item this move affects, or null if it moved several — only single-item moves
        // coalesce (so a run of numeric nudges collapses, but group moves stay distinct).
        public string SingleId => _changes.Count == 1 ? _changes[0].id : null;

        // Coalesce a follow-on single-item nudge: keep the original before, adopt the latest after.
        public void MergeAfter(MoveEdit newer)
        {
            var c = _changes[0];
            c.after = newer._changes[0].after;
            _changes[0] = c;
        }

        public void Undo()
        {
            foreach (var c in _changes)
                Apply(c.id, c.before);
        }

        public void Redo()
        {
            foreach (var c in _changes)
                Apply(c.id, c.after);
        }

        private static void Apply(string id, TransformPose pose)
        {
            var flag = UndoId.Resolve(id);
            if (flag == null)
                return;
            flag.transform.SetPositionAndRotation(pose.Position, pose.Rotation);
            flag.transform.localScale = pose.Scale;

            var blueprint = GetBlueprint(flag);
            if (blueprint == null)
                return;
            blueprint.position = new SerializableVector3(pose.Position);
            blueprint.rotation = new SerializableVector3(pose.Rotation.eulerAngles);
        }
    }

    // Group membership changed (Ctrl+G group/ungroup, Shift+MMB add/remove).
    private sealed class GroupEdit : IUndoEdit
    {
        public struct Change
        {
            public string id;
            public string oldGroup;
            public string newGroup;
        }

        private readonly List<Change> _changes;
        public GroupEdit(List<Change> changes) => _changes = changes;
        public string Label => "Group";

        public void Undo()
        {
            foreach (var c in _changes)
                SetGroup(c.id, c.oldGroup);
        }

        public void Redo()
        {
            foreach (var c in _changes)
                SetGroup(c.id, c.newGroup);
        }
    }

    // Open transaction accumulator. Adds are live flags (snapshotted at commit); removes are already
    // snapshotted (captured in the remove prefix before destruction).
    private sealed class Batch
    {
        public string label;
        public readonly List<Component> addFlags = new();
        public readonly List<TrackedSnapshot> removes = new();
    }

    private static readonly IDisposable NoOpScope = new Scope(null);

    private sealed class Scope : IDisposable
    {
        private Action _onDispose;
        public Scope(Action onDispose) => _onDispose = onDispose;

        public void Dispose()
        {
            var action = _onDispose;
            _onDispose = null;
            action?.Invoke();
        }
    }

    // ---- group-edit capture helper (used by PlacementUtilsWindow) ---------------------------

    // Snapshot the current group ids of a set of flags, run `mutate` (which changes mo_groupId), then
    // push a single GroupEdit for whatever actually changed. Keeps the group-edit call sites in
    // PlacementUtilsWindow to a one-line wrap.
    public static void RecordGroupChange(IEnumerable<Component> affected, Action mutate)
    {
        if (Suppressed)
        {
            mutate();
            return;
        }

        var ids = new List<string>();
        var before = new List<string>();
        foreach (var flag in affected)
        {
            var blueprint = GetBlueprint(flag);
            if (blueprint == null)
                continue;
            ids.Add(UndoId.Ensure(flag));
            before.Add(blueprint.mo_groupId);
        }

        mutate();

        var changes = new List<GroupEdit.Change>();
        for (var i = 0; i < ids.Count; i++)
        {
            var flag = UndoId.Resolve(ids[i]);
            var blueprint = GetBlueprint(flag);
            var newGroup = blueprint?.mo_groupId;
            if (newGroup != before[i])
                changes.Add(new GroupEdit.Change { id = ids[i], oldGroup = before[i], newGroup = newGroup });
        }

        if (changes.Count > 0)
            Push(new GroupEdit(changes));
    }
}
