using System;
using System.Collections.Generic;
using System.Linq;
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

        var childs = FindItemsByGroupId(selectedItem.blueprint.mo_groupId)
            .Select(info => info.gameObject).Where(obj => obj != selectedItem.gameObject).ToList();
        _fakeGroupContext = FakeGroup.GroupObjects(selectedItem.gameObject, childs, false);

        foreach (var child in childs)
        {
            var groupHighlightObj = Highlight(child);
            if (groupHighlightObj != null)
                groupHighlightObj.AddComponent<GroupSelectionInfo>().trackBlueprint = selectedItem.blueprint;
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

        // Count once on open for an immediate figure; thereafter it's refreshed on demand via the
        // "Refresh stats" button rather than the old per-second poll.
        UpdateStats();
    }

    private void RefreshGui()
    {
        _root.Q<TextField>("grid-align-value").value = GuiUtils.FloatToString(Shared.PlacementUtils.GridRound);
        _root.Q<TextField>("drag-grid-align-value").value = GuiUtils.FloatToString(Shared.PlacementUtils.DragGridRound);
        _root.Q<Toggle>("enchanted-editor").value = Shared.PlacementUtils.EnchantedEditor;

        EnsureTransformControls();
    }

    // Code-added numeric transform entry for the selected gizmo (the compiled UI bundle has no such
    // fields). Same idempotent, guard-on-.parent contract as the animation window's Ensure* methods.
    private void EnsureTransformControls()
    {
        if (_root == null)
            return;
        if (_posXField != null && _posXField.parent == _root)
            return;

        _posXField = MakeFloatField("Pos X:", v => SetGizmo(t => { var p = t.position; p.x = v; t.position = p; }));
        _posYField = MakeFloatField("Pos Y:", v => SetGizmo(t => { var p = t.position; p.y = v; t.position = p; }));
        _posZField = MakeFloatField("Pos Z:", v => SetGizmo(t => { var p = t.position; p.z = v; t.position = p; }));
        _rotXField = MakeFloatField("Rot X:", v => SetGizmo(t => { var e = t.eulerAngles; e.x = v; t.eulerAngles = e; }));
        _rotYField = MakeFloatField("Rot Y:", v => SetGizmo(t => { var e = t.eulerAngles; e.y = v; t.eulerAngles = e; }));
        _rotZField = MakeFloatField("Rot Z:", v => SetGizmo(t => { var e = t.eulerAngles; e.z = v; t.eulerAngles = e; }));

        foreach (var field in new[] { _posXField, _posYField, _posZField, _rotXField, _rotYField, _rotZField })
            _root.Add(field);

        _refreshStatsButton = new Button(UpdateStats) { text = "Refresh stats", focusable = false };
        _root.Add(_refreshStatsButton);

        _lintButton = new Button(RunLint) { text = "Validate triggers", focusable = false };
        _root.Add(_lintButton);

        _lintLabel = new Label(string.Empty) { style = { whiteSpace = WhiteSpace.Normal } };
        _root.Add(_lintLabel);

        _triggerLinksButton = new Button(ToggleTriggerLinks) { text = "Toggle trigger links", focusable = false };
        _root.Add(_triggerLinksButton);

        _duplicateButton = new Button(DuplicateSelected) { text = "Duplicate item", focusable = false };
        _root.Add(_duplicateButton);

        _arrayCountField = new TextField("Array count:") { maxLength = 4 };
        GuiUtils.ConvertToIntField(_arrayCountField, i => _arrayCount = i, _arrayCount);
        _root.Add(_arrayCountField);

        _arrayButton = new Button(ArraySelected) { text = "Array item", focusable = false };
        _root.Add(_arrayButton);
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

    // Arrow-key nudge of the gizmo by the grid step (Shift = vertical). Suppressed while a text
    // field is focused so the arrows edit text instead.
    private void NudgeGizmo()
    {
        if (_root?.panel?.focusController?.focusedElement is TextField)
            return;

        var gizmo = GameObject.Find("TrackEditorGizmo");
        if (gizmo == null)
            return;

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

        if (delta != Vector3.zero)
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

        RefreshTransformFields();
        NudgeGizmo();

        if (!Shared.PlacementUtils.EnchantedEditor)
            return;

        if (Input.GetKey(KeyCode.LeftControl))
            HandleEnchantedKeys();

        if (_selectedItem == null && Input.GetMouseButtonDown((int)MouseButton.MiddleMouse)) 
            HandleSelection();
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
        foreach (var info in FindObjectsOfType<GroupSelectionInfo>())
            Destroy(info.gameObject);
    }

    private GameObject Highlight(GameObject targetObject)
    {
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
        var existsGroupInfo = selectedObject.GetComponentInChildren<GroupSelectionInfo>();
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