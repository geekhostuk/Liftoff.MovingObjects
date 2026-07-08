using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Liftoff.MovingObjects.Player;
using Liftoff.MovingObjects.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.UIElements;
using static Liftoff.MovingObjects.Shared.Editor;
using Button = UnityEngine.UIElements.Button;
using Logger = BepInEx.Logging.Logger;
using Toggle = UnityEngine.UIElements.Toggle;

namespace Liftoff.MovingObjects;

internal class PlacementUtilsWindow : MonoBehaviour
{
    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{PluginInfo.PLUGIN_NAME}.{nameof(PlacementUtilsWindow)}");

    private IDisposable _fakeGroupContext;

    private VisualElement _root => _uiDocument.rootVisualElement;
    private ItemInfo _selectedItem;

    private UIDocument _uiDocument;

    private TextField _posXField;
    private TextField _posYField;
    private TextField _posZField;
    private TextField _rotXField;
    private TextField _rotYField;
    private TextField _rotZField;
    private Button _lintButton;
    private Label _lintLabel;
    private Button _refreshStatsButton;
    private Button _triggerLinksButton;
    private GameObject _triggerLinkObject;
    private bool _triggerLinksEnabled;
    private Button _duplicateButton;
    private TextField _arrayCountField;
    private Button _arrayButton;
    private int _arrayCount = 3;
    private Button _selectAllButton;
    private Button _copySelectionButton;
    private Button _pasteSelectionButton;
    private Button _duplicateSelectionButton;
    private Button _deleteSelectionButton;
    private Button _mirrorButton;
    private TextField _stampNameField;
    private Button _saveStampButton;
    private Button _insertStampButton;
    private string _stampName = "stamp";

    public Assets assets;

    private void Awake()
    {
        _uiDocument = gameObject.AddComponent<UIDocument>();
        _uiDocument.visualTreeAsset = assets.VisualTreeAsset;
        _uiDocument.panelSettings = assets.PanelSettings;
        _uiDocument.rootVisualElement.StretchToParentSize();

        Shared.Editor.OnItemSelected += OnItemSelected;
        Shared.Editor.OnItemCleared += OnItemCleared;

        // Statistics polling disabled: the per-second InvokeRepeating("UpdateStats")
        // recomputed the object count and (more expensively) the triangle count across
        // every object on the map each second, causing a visible stutter every second on
        // large maps. The counting is now disabled; the labels just show "0". See UpdateStats.
        // InvokeRepeating("UpdateStats", 1f, 1f);
    }

    private void OnDestroy()
    {
        Shared.Editor.OnItemSelected -= OnItemSelected;
        Shared.Editor.OnItemCleared -= OnItemCleared;

        if (_triggerLinkObject != null)
            Destroy(_triggerLinkObject);
    }

    private void OnItemCleared()
    {
        if (_selectedItem == null)
            return;

        _fakeGroupContext?.Dispose();

        _selectedItem = null;
        DeselectAll();
    }

    private void OnItemSelected(ItemInfo selectedItem)
    {
        _selectedItem = selectedItem;

        DeselectAll();
        if (!Shared.PlacementUtils.EnchantedEditor || string.IsNullOrEmpty(selectedItem.blueprint.mo_groupId))
            return;
        _fakeGroupContext?.Dispose();

        var members = FindItemsByGroupId(selectedItem.blueprint.mo_groupId);
        var childs = members.Select(info => info.gameObject)
            .Where(obj => obj != selectedItem.gameObject).ToList();
        _fakeGroupContext = FakeGroup.GroupObjects(selectedItem.gameObject, childs, false);

        // Tag each highlighted member with ITS OWN blueprint (previously every member got the clicked
        // item's blueprint, so the pink group collapsed to a single item when copied). This makes the
        // auto-highlighted group a real multi-item selection that copy/stamp/duplicate capture whole.
        // The clicked root carries the game's own selection and is folded in via _selectedItem in
        // GetSelectedPlacedItems, so it isn't re-highlighted here.
        foreach (var info in members)
        {
            if (info.gameObject == selectedItem.gameObject)
                continue;
            var groupHighlightObj = Highlight(info.gameObject);
            if (groupHighlightObj != null)
                groupHighlightObj.AddComponent<GroupSelectionInfo>().trackBlueprint = info.blueprint;
        }
    }

    // On-demand stats: the old code polled this every second (InvokeRepeating in Awake), which
    // stuttered on large maps. It's now driven by a "Refresh stats" button so the counts are
    // available without the per-second cost. Triangle counts use GetIndexCount (no per-mesh array
    // allocation).
    private void UpdateStats()
    {
        if (_root == null)
            return;

        _root.Q<Label>("object-count").text = EditorUtils.FindAllFlags().Count.ToString();

        long triangles = 0;
        foreach (var meshFilter in FindObjectsOfType<MeshFilter>())
        {
            var mesh = meshFilter.sharedMesh;
            if (mesh == null)
                continue;
            for (var submesh = 0; submesh < mesh.subMeshCount; submesh++)
                triangles += mesh.GetIndexCount(submesh) / 3;
        }

        _root.Q<Label>("triangle-count").text = triangles.ToString();
    }

    private void OnEnable()
    {
        // Dirty hack for add focus support
        GameObject.Find("UtilsWindowPanelSettings").AddComponent<InputField>().interactable = false;

        GuiUtils.SetVisible(_root, false);

        GuiUtils.ConvertToFloatField(_root.Q<TextField>("grid-align-value"),
            f => Shared.PlacementUtils.GridRound = f, Shared.PlacementUtils.GridRound);
        GuiUtils.ConvertToFloatField(_root.Q<TextField>("drag-grid-align-value"),
            f => Shared.PlacementUtils.DragGridRound = f, Shared.PlacementUtils.DragGridRound);
        _root.Q<Button>("grid-align").clicked += RoundGizmoLocation;

        _root.Q<Toggle>("enchanted-editor")
            .RegisterValueChangedCallback(evt => Shared.PlacementUtils.EnchantedEditor = evt.newValue);

        RefreshGui();

        // Stats are NOT computed on open: UpdateStats walks every MeshFilter in the scene and sums
        // submesh index counts, which hitches on large maps. It runs only when the user clicks
        // "Refresh stats" (see UpdateStats / _refreshStatsButton). The labels stay at their default
        // until then. This is the on-demand replacement for the old per-second InvokeRepeating poll.
    }

    private void RefreshGui()
    {
        _root.Q<TextField>("grid-align-value").value = GuiUtils.FloatToString(Shared.PlacementUtils.GridRound);
        _root.Q<TextField>("drag-grid-align-value").value = GuiUtils.FloatToString(Shared.PlacementUtils.DragGridRound);
        _root.Q<Toggle>("enchanted-editor").value = Shared.PlacementUtils.EnchantedEditor;

        EnsureTransformControls();
    }

    // The compiled window nests its content in a bordered panel (the "Placement utils" box).
    // Code-added controls must live inside that panel, not on the full-screen root visual element
    // (which is StretchToParentSize) — otherwise they render across the whole screen. Find the
    // panel as the ancestor of a known bundle control that is a direct child of the root.
    private VisualElement PanelContainer()
    {
        VisualElement known = _root.Q<Toggle>("enchanted-editor");
        known ??= _root.Q<TextField>("grid-align-value");
        if (known == null)
            return _root;

        var element = known.parent;
        while (element != null && element.parent != null && element.parent != _root)
            element = element.parent;
        return element ?? _root;
    }

    // Code-added numeric transform entry for the selected gizmo (the compiled UI bundle has no such
    // fields). Same idempotent, guard-on-.parent contract as the animation window's Ensure* methods.
    private void EnsureTransformControls()
    {
        if (_root == null)
            return;

        var container = PanelContainer();
        if (_posXField != null && _posXField.parent == container)
            return;

        _posXField = MakeFloatField("Pos X:", v => SetGizmo(t => { var p = t.position; p.x = v; t.position = p; }));
        _posYField = MakeFloatField("Pos Y:", v => SetGizmo(t => { var p = t.position; p.y = v; t.position = p; }));
        _posZField = MakeFloatField("Pos Z:", v => SetGizmo(t => { var p = t.position; p.z = v; t.position = p; }));
        _rotXField = MakeFloatField("Rot X:", v => SetGizmo(t => { var e = t.eulerAngles; e.x = v; t.eulerAngles = e; }));
        _rotYField = MakeFloatField("Rot Y:", v => SetGizmo(t => { var e = t.eulerAngles; e.y = v; t.eulerAngles = e; }));
        _rotZField = MakeFloatField("Rot Z:", v => SetGizmo(t => { var e = t.eulerAngles; e.z = v; t.eulerAngles = e; }));

        foreach (var field in new[] { _posXField, _posYField, _posZField, _rotXField, _rotYField, _rotZField })
            container.Add(field);

        _refreshStatsButton = new Button(UpdateStats) { text = "Refresh stats", focusable = false };
        container.Add(_refreshStatsButton);

        _lintButton = new Button(RunLint) { text = "Validate triggers", focusable = false };
        container.Add(_lintButton);

        _lintLabel = new Label(string.Empty) { style = { whiteSpace = WhiteSpace.Normal } };
        container.Add(_lintLabel);

        _triggerLinksButton = new Button(ToggleTriggerLinks) { text = "Toggle trigger links", focusable = false };
        container.Add(_triggerLinksButton);

        _duplicateButton = new Button(DuplicateSelected) { text = "Duplicate item", focusable = false };
        container.Add(_duplicateButton);

        _arrayCountField = new TextField("Array count:") { maxLength = 4 };
        GuiUtils.ConvertToIntField(_arrayCountField, i => _arrayCount = i, _arrayCount);
        container.Add(_arrayCountField);

        _arrayButton = new Button(ArraySelected) { text = "Array item", focusable = false };
        container.Add(_arrayButton);

        _selectAllButton = new Button(SelectAll) { text = "Select all objects", focusable = false };
        container.Add(_selectAllButton);

        _copySelectionButton = new Button(CopySelection) { text = "Copy selection", focusable = false };
        container.Add(_copySelectionButton);

        _pasteSelectionButton = new Button(PasteSelection) { text = "Paste selection", focusable = false };
        container.Add(_pasteSelectionButton);

        _duplicateSelectionButton = new Button(DuplicateSelectionInPlace)
            { text = "Duplicate selection in place (F5)", focusable = false };
        container.Add(_duplicateSelectionButton);

        _deleteSelectionButton = new Button(DeleteSelection)
            { text = "Delete selection (F9)", focusable = false };
        container.Add(_deleteSelectionButton);

        _mirrorButton = new Button(MirrorSelection) { text = "Mirror selection", focusable = false };
        container.Add(_mirrorButton);

        _stampNameField = new TextField("Stamp name:") { maxLength = 40 };
        _stampNameField.SetValueWithoutNotify(_stampName);
        _stampNameField.RegisterValueChangedCallback(evt => _stampName = evt.newValue);
        container.Add(_stampNameField);

        _saveStampButton = new Button(SaveStamp) { text = "Save selection to stamp", focusable = false };
        container.Add(_saveStampButton);

        _insertStampButton = new Button(InsertStamp) { text = "Insert stamp", focusable = false };
        container.Add(_insertStampButton);
    }

    private static Vector3 GizmoPosition()
    {
        var gizmo = GameObject.Find("TrackEditorGizmo");
        return gizmo != null ? gizmo.transform.position : Vector3.zero;
    }

    // The live flag Components of the current selection, deduped by blueprint. Membership is taken
    // from the AUTHORITATIVE source — mo_groupId — not from the pink highlight markers.
    //
    // The markers (GroupSelectionInfo) only exist where Highlight() found the game's
    // "<name>(Clone)_Overlay" child; a freshly spawned copy hasn't been through the game's placement
    // flow, so some members have no overlay yet and got no marker. Capturing by marker therefore
    // dropped those members, so copying a copy came back missing pieces — and copying THAT lost even
    // more (honk's "only the handle left", degenerating each generation). Seeding from the selection
    // and then expanding every seed's whole group by id captures the group whole, overlay or not.
    private List<Component> CollectSelectionFlags()
    {
        var flags = new List<Component>();
        var seenBlueprints = new HashSet<TrackBlueprint>();
        var seenGroups = new HashSet<string>();

        void AddFlag(Component flag)
        {
            if (flag == null)
                return;
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);
            if (blueprint == null || !seenBlueprints.Add(blueprint))
                return;
            flags.Add(flag);

            // Pull in the rest of this member's group by id (once per group). A loose item has no
            // group id, so it stays a selection of one.
            if (!string.IsNullOrEmpty(blueprint.mo_groupId) && seenGroups.Add(blueprint.mo_groupId))
                foreach (var member in EditorUtils.FindFlagsByGroupId(blueprint.mo_groupId))
                    AddFlag(member);
        }

        // Seeds: the clicked item plus any manual middle-click multi-select markers. Each seed's
        // whole group is expanded in AddFlag, so a pink group-click copies/stamps as one cohesive unit.
        if (_selectedItem?.blueprint != null)
            AddFlag(EditorUtils.FindFlagByBlueprint(_selectedItem.blueprint));
        foreach (var info in FindObjectsOfType<GroupSelectionInfo>())
            AddFlag(EditorUtils.FindFlagByBlueprint(info.trackBlueprint));

        return flags;
    }

    // The current selection as PlacedItems: each a deep-cloned blueprint plus the item's LIVE world
    // transform (blueprint position/rotation are stale mid-edit, so we read the transform).
    private List<PlacedItem> GetSelectedPlacedItems()
    {
        var items = new List<PlacedItem>();
        foreach (var flag in CollectSelectionFlags())
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);
            if (blueprint != null)
                items.Add(PlacedItem.FromLive(CloneUtils.DeepClone(blueprint), flag.transform));
        }

        return items;
    }

    private void CopySelection()
    {
        var items = GetSelectedPlacedItems();
        if (items.Count == 0)
            return;

        Shared.ItemClipboard.items = items;
    }

    private void PasteSelection()
    {
        if (!Shared.ItemClipboard.HasData)
            return;

        // Paste at the gizmo, as one fresh group when it's more than one item — the same rule stamp
        // uses, so copying an ungrouped multi-selection now comes back grouped just like a stamp does.
        var items = Shared.ItemClipboard.items;
        ItemSpawner.Paste(items, ItemSpawner.Centroid(items), GizmoPosition(),
            Shared.PlacementUtils.GridRound, FreshGroupId(items));
        Shared.Editor.RequestRefreshGui();
    }

    // Duplicate the current selection (a pink group counts as one selection — see OnItemSelected)
    // exactly where it stands: anchoring the captured centroid onto itself makes the paste
    // translation zero, so each copy lands on top of its original instead of at the gizmo. The copies
    // arrive as one fresh group, ready to be clicked and dragged off. gridStep 0 = no snap, so they
    // stay exactly coincident with the originals.
    private void DuplicateSelectionInPlace()
    {
        var items = GetSelectedPlacedItems();
        if (items.Count == 0)
            return;

        var centroid = ItemSpawner.Centroid(items);
        ItemSpawner.Paste(items, centroid, centroid, 0f, FreshGroupId(items));
        Shared.Editor.RequestRefreshGui();
    }

    // A paste/stamp/duplicate of more than one item comes in as a single fresh group so it can be
    // grabbed (and ungrouped with Ctrl+G) as a unit — one rule shared by all three so they behave the
    // same. A lone item stays ungrouped (GroupFlags ignores single-member groups anyway).
    private static string FreshGroupId(IReadOnlyCollection<PlacedItem> items)
        => items.Count > 1 ? Guid.NewGuid().ToString("D") : null;

    // Delete the current selection (a pink group counts as one selection — see OnItemSelected). Each
    // item is removed via the editor's own RemoveTrackItem so it's gone from the saved track too, not
    // just hidden. The selection overlay (highlight clones + fake-group components) references the
    // objects we're about to destroy, so it's torn down first.
    private void DeleteSelection()
    {
        var flags = GetSelectedFlags();
        if (flags.Count == 0)
            return;

        _fakeGroupContext?.Dispose();
        _fakeGroupContext = null;
        DeselectAll();
        _selectedItem = null;

        foreach (var flag in flags)
            ItemSpawner.RemoveItem(flag);

        // The game still had the deleted item selected in gizmo mode, which would leave its transform
        // gizmo floating with nothing attached — drop back to place mode so the game clears it.
        ItemSpawner.ClearGizmoSelection();

        Shared.Editor.RequestRefreshGui();
    }

    // The live flag Components of the current selection — the delete-side counterpart to
    // GetSelectedPlacedItems (which clones blueprints for spawning; delete needs the live objects to
    // remove). Same group-id-authoritative capture, so deleting a group removes it whole.
    private List<Component> GetSelectedFlags() => CollectSelectionFlags();

    private void MirrorSelection()
    {
        var items = GetSelectedPlacedItems();
        if (items.Count == 0)
            return;

        // Mirror across the vertical plane through the gizmo (left/right).
        ItemSpawner.Mirror(items, GizmoPosition(), Vector3.right);
        Shared.Editor.RequestRefreshGui();
    }

    private void SaveStamp()
    {
        var items = GetSelectedPlacedItems();
        if (items.Count == 0)
            return;

        // Bake each item's live world transform into the blueprint fields so the on-disk stamp
        // records where things actually are (blueprint position/rotation are stale mid-edit). The
        // blueprints here are already deep clones, so this doesn't touch the live items.
        var blueprints = items.Select(item =>
        {
            item.blueprint.position = new SerializableVector3(item.position);
            item.blueprint.rotation = new SerializableVector3(item.rotation.eulerAngles);
            return item.blueprint;
        }).ToList();

        StampIO.Save(_stampName, blueprints);
    }

    private void InsertStamp()
    {
        var blueprints = StampIO.Load(_stampName);
        if (blueprints == null || blueprints.Count == 0)
            return;

        // Disk stamps have no live object, so their blueprint position/rotation ARE the truth. A stamp
        // is a reusable prefab: insert it as one cohesive fresh group (see FreshGroupId) so it can be
        // nudged into place and ungrouped with Ctrl+G as a unit.
        var items = blueprints.Select(PlacedItem.FromBlueprint).ToList();
        ItemSpawner.Paste(items, ItemSpawner.Centroid(items), GizmoPosition(),
            Shared.PlacementUtils.GridRound, FreshGroupId(items));
        Shared.Editor.RequestRefreshGui();
    }

    // Single-item duplicate built on the ItemSpawner spike: clone the selected item at a one-grid
    // offset, carrying its MO config with a fresh group id. The primitive that unblocks
    // multi-object copy/paste and array/mirror.
    private void DuplicateSelected()
    {
        if (_selectedItem?.blueprint == null)
            return;

        var step = Shared.PlacementUtils.GridRound > 0 ? Shared.PlacementUtils.GridRound : 1f;
        ItemSpawner.Duplicate(_selectedItem.blueprint, new Vector3(step, 0f, 0f));
        Shared.Editor.RequestRefreshGui();
    }

    private void ArraySelected()
    {
        if (_selectedItem?.blueprint == null || _arrayCount < 1)
            return;

        var step = Shared.PlacementUtils.GridRound > 0 ? Shared.PlacementUtils.GridRound : 1f;
        ItemSpawner.Array(_selectedItem.blueprint, _arrayCount, new Vector3(step, 0f, 0f));
        Shared.Editor.RequestRefreshGui();
    }

    private void RunLint()
    {
        if (_lintLabel != null)
            _lintLabel.text = string.Join("\n", EditorUtils.ValidateTriggers());
    }

    private void ToggleTriggerLinks()
    {
        _triggerLinksEnabled = !_triggerLinksEnabled;
        UpdateTriggerLinks();
    }

    // Snapshot the entrance -> named-exit links and render them. Re-toggle after moving objects to
    // refresh. Pairs with the trigger lint (same name matching) and PathPreview (same overlay style).
    private void UpdateTriggerLinks()
    {
        if (!_triggerLinksEnabled)
        {
            if (_triggerLinkObject != null)
            {
                Destroy(_triggerLinkObject);
                _triggerLinkObject = null;
            }
            return;
        }

        var byName = new Dictionary<string, List<(Vector3 pos, Vector3 forward)>>();
        var entrances = new List<(Vector3 pos, string target)>();

        foreach (var flag in EditorUtils.FindAllFlags())
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);
            var trigger = blueprint?.mo_triggerOptions;
            if (trigger == null)
                continue;

            var transform = flag.gameObject.transform;
            if (!string.IsNullOrEmpty(trigger.triggerName))
            {
                if (!byName.TryGetValue(trigger.triggerName, out var list))
                    byName[trigger.triggerName] = list = new List<(Vector3, Vector3)>();
                list.Add((transform.position, transform.forward));
            }

            if (!string.IsNullOrEmpty(trigger.triggerTarget))
                entrances.Add((transform.position, trigger.triggerTarget));
        }

        var links = new List<TriggerLinkPreview.Link>();
        foreach (var entrance in entrances)
            if (byName.TryGetValue(entrance.target, out var targets))
                foreach (var target in targets)
                    links.Add(new TriggerLinkPreview.Link
                    {
                        From = entrance.pos,
                        To = target.pos,
                        Forward = target.forward
                    });

        if (_triggerLinkObject == null)
            _triggerLinkObject = new GameObject("MO_TriggerLinks");
        var preview = _triggerLinkObject.GetComponent<TriggerLinkPreview>() ??
                      _triggerLinkObject.AddComponent<TriggerLinkPreview>();
        preview.SetLinks(links);
    }

    private static TextField MakeFloatField(string label, Action<float> onChange)
    {
        var field = new TextField(label) { maxLength = 16 };
        GuiUtils.ConvertToFloatField(field, onChange);
        return field;
    }

    private static void SetGizmo(Action<Transform> apply)
    {
        var gizmo = GameObject.Find("TrackEditorGizmo");
        if (gizmo != null)
            apply(gizmo.transform);
    }

    // Reflect the gizmo's current transform into the numeric fields, skipping any the user is
    // editing so their typing isn't clobbered.
    private void RefreshTransformFields()
    {
        if (_posXField == null)
            return;
        var gizmo = GameObject.Find("TrackEditorGizmo");
        if (gizmo == null)
            return;

        var pos = gizmo.transform.position;
        var rot = gizmo.transform.eulerAngles;
        SetIfNotFocused(_posXField, pos.x);
        SetIfNotFocused(_posYField, pos.y);
        SetIfNotFocused(_posZField, pos.z);
        SetIfNotFocused(_rotXField, rot.x);
        SetIfNotFocused(_rotYField, rot.y);
        SetIfNotFocused(_rotZField, rot.z);
    }

    private static void SetIfNotFocused(TextField field, float value)
    {
        if (field.panel?.focusController?.focusedElement == field)
            return;
        field.SetValueWithoutNotify(GuiUtils.FloatToString(value));
    }

    // Freeze/thaw the editor fly-camera while the nudge modifier (Alt) is held, so the arrow keys
    // move the gizmo without also flying the avatar. We can't swallow the arrow input (the game reads
    // it through Unity's shared movement axes), so we hold the avatar body still: disable its mover
    // (LiftoffFirstPersonController) and the editor camera wrapper (which would otherwise re-enable
    // physics), zero and freeze its rigidbody, and restore all of that on release. Mouse-look pauses
    // while held, which is harmless during a nudge. Everything is captured on the Alt-press edge and
    // null-guarded, so a torn-down avatar just leaves the freeze inert.
    private bool _nudgeFrozen;
    private Behaviour _frozenMover;
    private Behaviour _frozenCameraWrapper;
    private Rigidbody _frozenRb;
    private bool _frozenRbPrevKinematic;

    private void UpdateNudgeCameraFreeze(bool freeze)
    {
        if (freeze == _nudgeFrozen)
            return;

        if (freeze)
        {
            var mover = FindObjectOfType<LiftoffFirstPersonController>();
            if (mover == null)
                return;

            _frozenMover = mover;
            _frozenCameraWrapper = FindObjectOfType<EditorCameraFirstPersonController>();
            _frozenRb = mover.GetComponent<Rigidbody>();
            if (_frozenRb != null)
            {
                _frozenRbPrevKinematic = _frozenRb.isKinematic;
                _frozenRb.velocity = Vector3.zero;
                _frozenRb.angularVelocity = Vector3.zero;
                _frozenRb.isKinematic = true;
            }
            if (_frozenCameraWrapper != null)
                _frozenCameraWrapper.enabled = false;
            _frozenMover.enabled = false;
            _nudgeFrozen = true;
        }
        else
        {
            if (_frozenMover != null)
                _frozenMover.enabled = true;
            if (_frozenCameraWrapper != null)
                _frozenCameraWrapper.enabled = true;
            if (_frozenRb != null)
                _frozenRb.isKinematic = _frozenRbPrevKinematic;
            _frozenMover = null;
            _frozenCameraWrapper = null;
            _frozenRb = null;
            _nudgeFrozen = false;
        }
    }

    private void OnDisable()
    {
        // Don't leave the avatar frozen if this window is torn down mid-nudge.
        UpdateNudgeCameraFreeze(false);
    }

    // Is the user editing any of the mod's text fields right now? The mod runs two independent UI
    // Toolkit panels (this window and the animation editor), each with its own focusController, so we
    // scan every live panel. We walk UP from the focused element because focus can land on a
    // TextField's inner input child rather than the TextField itself — an `is TextField` check on the
    // focused element alone would miss that and treat typing as not-editing.
    private static bool IsEditingText()
    {
        foreach (var doc in FindObjectsOfType<UIDocument>())
        {
            var element = doc.rootVisualElement?.panel?.focusController?.focusedElement as VisualElement;
            while (element != null)
            {
                if (element is TextField)
                    return true;
                element = element.parent;
            }
        }

        return false;
    }

    // Arrow-key nudge of the gizmo by the grid step (Shift = vertical). Only reached while the Alt
    // nudge modifier is held AND no text field is focused — Update gates both this and the camera
    // freeze on !IsEditingText(), so while you're typing the arrows stay entirely with the field
    // (matching vanilla Liftoff: arrows edit text and don't move the avatar) and the mod touches
    // neither the gizmo nor the fly-camera.
    private void NudgeGizmo()
    {
        var step = Shared.PlacementUtils.GridRound > 0 ? Shared.PlacementUtils.GridRound : 1f;
        var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
        var delta = Vector3.zero;

        if (Input.GetKeyDown(KeyCode.LeftArrow))
            delta.x -= step;
        if (Input.GetKeyDown(KeyCode.RightArrow))
            delta.x += step;
        if (Input.GetKeyDown(KeyCode.UpArrow))
            delta += shift ? Vector3.up * step : Vector3.forward * step;
        if (Input.GetKeyDown(KeyCode.DownArrow))
            delta += shift ? Vector3.down * step : Vector3.back * step;

        if (delta == Vector3.zero)
            return;

        var gizmo = GameObject.Find("TrackEditorGizmo");
        if (gizmo == null)
            return;

        gizmo.transform.position += delta;
    }

    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.F1))
            RoundGizmoLocation();
        else if (Input.GetKeyDown(KeyCode.F3))
            ToggleNoClip();
        else if (Input.GetKeyDown(KeyCode.F4))
            ToggleWireframe();
        else if (_root != null && Input.GetKeyDown(KeyCode.F2))
            GuiUtils.ToggleVisible(_root);
        else if (Input.GetKeyDown(KeyCode.F5))
            DuplicateSelectionInPlace();
        else if (Input.GetKeyDown(KeyCode.F9))
            DeleteSelection();

        RefreshTransformFields();

        // Arrow-key gizmo nudge is a "hold Alt" mode. The editor fly-camera also moves on the arrow
        // keys (Unity aliases them with WASD through its shared movement axes, which the mod can't
        // intercept), so pressing arrows used to move the gizmo AND the avatar at once. Now the nudge
        // only fires while Alt is held, and while Alt is held we freeze the fly-camera so the arrows
        // move the gizmo only. Plain arrows keep flying the avatar for creators who navigate that way.
        //
        // Crucially we bail entirely while a text field is focused: the freeze disables the avatar's
        // own controller, which is what vanilla Liftoff relies on to keep arrows in the field (not
        // moving the avatar) while typing. Freezing it there broke that — arrows leaked to the avatar
        // and kicked focus out of the field (honk). Gating on !IsEditingText() leaves typing untouched.
        var nudgeHeld = (Input.GetKey(KeyCode.LeftAlt) || Input.GetKey(KeyCode.RightAlt)) && !IsEditingText();
        UpdateNudgeCameraFreeze(nudgeHeld);
        if (nudgeHeld)
            NudgeGizmo();

        if (!Shared.PlacementUtils.EnchantedEditor)
            return;

        if (Input.GetKey(KeyCode.LeftControl))
            HandleEnchantedKeys();

        if (Input.GetMouseButtonDown((int)MouseButton.MiddleMouse))
        {
            var shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
            if (shift && _selectedItem != null)
                // Shift+MMB while something is selected edits the selected group's membership.
                HandleGroupMembershipEdit();
            else if (_selectedItem == null)
                // Plain MMB with nothing selected toggles an item into the multi-select set.
                HandleSelection();
        }
    }

    private void ToggleWireframe()
    {
        var playerCamera = GameObject.Find("PlayerCamera")?.GetComponent<Camera>();
        if (playerCamera == null)
            return;

        var wireframe = playerCamera.gameObject.GetComponent<WireframeCamera>();
        if (wireframe == null)
        {
            wireframe = playerCamera.gameObject.AddComponent<WireframeCamera>();
            wireframe.enabled = false;
            wireframe.orignalClearFlags = playerCamera.clearFlags;
        }

        wireframe.enabled = !wireframe.enabled;
        playerCamera.clearFlags = wireframe.enabled?  CameraClearFlags.SolidColor: wireframe.orignalClearFlags;
    }

    private void ToggleNoClip()
    {
        var controller = GameObject.Find("FirstPersonController");
        var rigidBody = controller?.GetComponent<Rigidbody>();
        if (rigidBody != null)
            rigidBody.detectCollisions = !rigidBody.detectCollisions;
    }

    private List<ItemInfo> FindItemsByGroupId(string groupId)
    {
        var items = new List<ItemInfo>();
        foreach (var trackItemFlag in EditorUtils.FindFlagsByGroupId(groupId))
        {
            var info = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(trackItemFlag);
            if (info != null && string.Equals(groupId, info.mo_groupId))
                items.Add(new ItemInfo { blueprint = info, gameObject = trackItemFlag.gameObject });
        }

        return items;
    }

    private void HandleEnchantedKeys()
    {
        if (!Input.GetKeyDown(KeyCode.G))
            return;
        if (_selectedItem == null)
        {
            var groupId = Guid.NewGuid().ToString("D");
            foreach (var info in FindObjectsOfType<GroupSelectionInfo>())
                info.trackBlueprint.mo_groupId = groupId;
        }
        else if (!string.IsNullOrEmpty(_selectedItem.blueprint.mo_groupId))
        {
            foreach (var itemInfo in FindItemsByGroupId(_selectedItem.blueprint.mo_groupId))
            {
                itemInfo.blueprint.mo_groupId = null;
                itemInfo.gameObject.transform.parent = null;
            }
        }

        DeselectAll();
        Shared.Editor.RequestRefreshGui();
    }

    private void DeselectAll()
    {
        // Include inactive (the `true`): a highlight clone / selection marker whose carrier got
        // deactivated — e.g. a group member hidden during a preview, or a clone left parented under an
        // object that a group edit deactivated — is skipped by the default active-only search, so it
        // survives teardown and leaves a "ghost" overlay lingering until the next reload. Sweeping
        // inactive carriers too destroys those leftovers.
        foreach (var info in FindObjectsOfType<GroupSelectionInfo>(true))
            Destroy(info.gameObject);
    }

    // The magenta group highlight below is a clone of the game's own hover overlay child
    // "<name>(Clone)_Overlay", which the game builds LAZILY the first time the cursor passes over an
    // object. So a freshly loaded, never-hovered group member had no overlay to clone and stayed
    // uncoloured until hovered (honk: "group objects only get their pink highlight after being hovered
    // at least once"). Here we force the game to build that overlay up front by invoking the flag's
    // own public highlight method — the same one hover calls, which instantiates the overlay child,
    // activates it and tints it — then hide the game's copy again so only our magenta clone shows.
    //
    // The method name is obfuscated, so we locate it by shape (public instance void taking a single
    // bool, on the TrackItem overlay base) and cache it per type. The two matching methods on that
    // base both create the overlay, so either is fine. We verify the overlay actually appeared before
    // touching it; if the game build ever changes shape this simply no-ops and Highlight() falls back
    // to the previous hover-gated behaviour — no crash, just no pre-hover highlight.
    private static readonly Dictionary<Type, MethodInfo> _highlightMethods = new();

    private void EnsureGameOverlay(GameObject targetObject)
    {
        var overlayName = targetObject.name + "(Clone)_Overlay";
        if (targetObject.transform.Find(overlayName) != null)
            return;

        var flag = EditorUtils.FindFlagInParent(targetObject);
        if (flag == null)
            return;

        try
        {
            var method = ResolveHighlightMethod(flag.GetType());
            if (method == null)
                return;

            method.Invoke(flag, new object[] { true });

            // Hide the game's own (placeable-colour) overlay it just activated — our magenta clone is
            // what we want on screen. Transform.Find still locates the now-inactive child.
            var overlay = targetObject.transform.Find(overlayName);
            if (overlay != null)
                overlay.gameObject.SetActive(false);
        }
        catch (Exception ex)
        {
            Log.LogDebug($"EnsureGameOverlay failed for {targetObject.name}: {ex.Message}");
        }
    }

    private static MethodInfo ResolveHighlightMethod(Type flagType)
    {
        if (_highlightMethods.TryGetValue(flagType, out var cached))
            return cached;

        var candidates = flagType
            .GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .Where(m => !m.IsAbstract
                        && m.ReturnType == typeof(void)
                        && m.GetParameters().Length == 1
                        && m.GetParameters()[0].ParameterType == typeof(bool)
                        && m.DeclaringType != typeof(MonoBehaviour)
                        && typeof(MonoBehaviour).IsAssignableFrom(m.DeclaringType))
            .ToList();

        // The game's overlay-highlight entry points are virtual and live on a shared base class;
        // prefer virtual-on-base, then any virtual, then anything matching the shape.
        var method = candidates.FirstOrDefault(m => m.IsVirtual && m.DeclaringType != flagType)
                     ?? candidates.FirstOrDefault(m => m.IsVirtual)
                     ?? candidates.FirstOrDefault();

        _highlightMethods[flagType] = method;
        return method;
    }

    private GameObject Highlight(GameObject targetObject)
    {
        EnsureGameOverlay(targetObject);

        var highlightObj = targetObject.transform.Find(targetObject.name + "(Clone)_Overlay");
        if (highlightObj == null)
            return null;

        var groupHighlightName = highlightObj.gameObject.name + "_Overlay_MO";

        var groupHighlightObj = Instantiate(highlightObj.gameObject, targetObject.transform);
        groupHighlightObj.name = groupHighlightName;
        groupHighlightObj.transform.localScale = highlightObj.localScale;
        groupHighlightObj.transform.localPosition = highlightObj.localPosition;
        groupHighlightObj.transform.localRotation = highlightObj.localRotation;

        var renderers = new List<Renderer>();
        var shaderOverride = groupHighlightObj.GetComponent<TrackEditorOverlayShaderOverride>();
        if (shaderOverride != null)
            renderers.AddRange(shaderOverride.affectedRenderers);
        renderers.AddRange(groupHighlightObj.GetComponentsInChildren<Renderer>());

        foreach (var t in renderers.SelectMany(renderer => renderer.materials))
            t.SetColor("_OverlayColor", Color.magenta);

        groupHighlightObj.SetActive(true);
        return groupHighlightObj;
    }


    // Select every placed item on the map as one flat multi-selection, so the whole track can be
    // copied/mirrored/stamped/deleted — or grabbed and moved — in one go. Existing groups are ignored
    // on purpose: each object is marked individually regardless of its mo_groupId, and no group ids
    // are touched. That leaves the choice to the user — move the lot with each sub-group still intact,
    // or Ctrl+G to weld everything into one group.
    //
    // Like HandleSelection's one-at-a-time MMB toggle, this works from the GroupSelectionInfo marker
    // set, so it runs in the "nothing individually selected" mode: the game's single selection and any
    // existing markers / auto-highlighted group are cleared first, then a marker is dropped on each
    // item. (The blueprint set just guards against marking the same item twice.)
    private void SelectAll()
    {
        _fakeGroupContext?.Dispose();
        _fakeGroupContext = null;
        _selectedItem = null;
        DeselectAll();

        var seen = new HashSet<TrackBlueprint>();
        foreach (var flag in EditorUtils.FindAllFlags())
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);
            if (blueprint == null || !seen.Add(blueprint))
                continue;

            MarkSelected(flag.gameObject, blueprint);
        }
    }

    // Add this object to the multi-selection. The GroupSelectionInfo marker — NOT the magenta
    // overlay — is what CollectSelectionFlags reads, so the marker is what actually makes the item
    // part of the selection. The game builds each object's hover-overlay lazily (only once the cursor
    // has passed over it), so a freshly loaded, never-hovered block has no overlay for Highlight() to
    // clone — the cause of "select all only caught the blocks I'd moused over". We therefore carry the
    // marker on the game overlay when it exists (which also gives the magenta highlight) and otherwise
    // on a bare child GameObject, so the item is captured either way. DeselectAll destroys the
    // marker's gameObject in both cases, and HandleSelection's GetComponentInChildren toggle finds it
    // in both cases. The bare carrier has no TrackItem component, so the game's save ignores it.
    private void MarkSelected(GameObject target, TrackBlueprint blueprint)
    {
        var carrier = Highlight(target);
        if (carrier == null)
        {
            carrier = new GameObject(target.name + "_Selection_MO");
            carrier.transform.SetParent(target.transform, false);
        }

        carrier.AddComponent<GroupSelectionInfo>().trackBlueprint = blueprint;
    }

    private void HandleSelection()
    {
        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (EventSystem.current.IsPointerOverGameObject())
            return;

        if (!Physics.Raycast(ray, out var raycastHit))
        {
            DeselectAll();
            return;
        }

        var trackItemFlag = EditorUtils.FindFlagInParent(raycastHit.transform.gameObject);
        if (trackItemFlag == null)
        {
            DeselectAll();
            return;
        }

        var selectedObject = trackItemFlag.gameObject;
        // Include inactive (the `true`): if the existing marker sits on a deactivated carrier the
        // default search misses it, so this toggle would stack a second highlight on top instead of
        // removing the first — leaving a duplicate overlay behind. Matching inactive markers too keeps
        // the middle-click toggle a true toggle.
        var existsGroupInfo = selectedObject.GetComponentInChildren<GroupSelectionInfo>(true);
        if (existsGroupInfo != null)
        {
            Destroy(existsGroupInfo.gameObject);
            return;
        }

        var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(trackItemFlag);
        var groupHighlightObj = Highlight(selectedObject);
        if (groupHighlightObj == null)
            return;

        groupHighlightObj.AddComponent<GroupSelectionInfo>().trackBlueprint = blueprint;
    }

    // Shift+MMB while an item/group is selected: toggle the clicked object's membership in the
    // selected group. Click a loose object to ADD it (expanding the group); click a current member to
    // REMOVE it. Grouping in the editor is driven entirely by mo_groupId, so add/remove is just a
    // write to that field. If the selected item isn't grouped yet, a fresh group id is seeded onto it
    // first, so shift-clicking a second object starts a group from a lone selection. Adding an object
    // that was in another group moves it into this one. The anchor (the selected item itself) is left
    // alone. The pink highlight + fake group are rebuilt afterwards so the change shows immediately and
    // the group stays selected.
    private void HandleGroupMembershipEdit()
    {
        if (_selectedItem?.blueprint == null || _selectedItem.gameObject == null)
            return;
        if (EventSystem.current.IsPointerOverGameObject())
            return;

        var ray = Camera.main.ScreenPointToRay(Input.mousePosition);
        if (!Physics.Raycast(ray, out var raycastHit))
            return;

        var trackItemFlag = EditorUtils.FindFlagInParent(raycastHit.transform.gameObject);
        if (trackItemFlag == null)
            return;

        var clickedBlueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(trackItemFlag);
        if (clickedBlueprint == null || ReferenceEquals(clickedBlueprint, _selectedItem.blueprint))
            return;

        var groupId = _selectedItem.blueprint.mo_groupId;
        if (string.IsNullOrEmpty(groupId))
        {
            groupId = Guid.NewGuid().ToString("D");
            _selectedItem.blueprint.mo_groupId = groupId;
        }

        if (string.Equals(clickedBlueprint.mo_groupId, groupId))
        {
            clickedBlueprint.mo_groupId = null;
            trackItemFlag.gameObject.transform.parent = null;
        }
        else
        {
            clickedBlueprint.mo_groupId = groupId;
        }

        OnItemSelected(_selectedItem);
        Shared.Editor.RequestRefreshGui();
    }


    private static void RoundGizmoLocation()
    {
        var gizmo = GameObject.Find("TrackEditorGizmo");
        if (gizmo == null)
            return;

        gizmo.transform.position =
            GridUtils.RoundVectorToStep(gizmo.transform.position, Shared.PlacementUtils.GridRound);
    }

    private class GroupSelectionInfo : MonoBehaviour
    {
        public TrackBlueprint trackBlueprint;
    }

    public struct Assets
    {
        public VisualTreeAsset VisualTreeAsset { get; set; }
        public PanelSettings PanelSettings { get; set; }
    }

    private class WireframeCamera : MonoBehaviour
    {
        public CameraClearFlags orignalClearFlags;

        void OnPreRender()
        {
            GL.wireframe = true;
        }
        void OnPostRender()
        {
            GL.wireframe = false;
        }
    }

}