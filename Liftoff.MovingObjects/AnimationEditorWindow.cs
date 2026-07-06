using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx.Logging;
using Liftoff.MovingObjects.Player;
using Liftoff.MovingObjects.Utils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using static Liftoff.FlightControllers.FlightMode;
using Button = UnityEngine.UIElements.Button;
using Logger = BepInEx.Logging.Logger;
using Slider = UnityEngine.UIElements.Slider;
using Toggle = UnityEngine.UIElements.Toggle;

namespace Liftoff.MovingObjects;

internal class AnimationEditorWindow : MonoBehaviour
{
    public enum Type
    {
        None,
        Animation,
        Physics
    }

    private const string PlayButtonText = "Play";
    private const string StopButtonText = "Stop";

    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{PluginInfo.PLUGIN_NAME}.{nameof(AnimationEditorWindow)}");

    private TrackBlueprint _blueprint;
    private MonoBehaviour _item;
    private VisualElement _root;
    private ScrollView _panelScroll;
    private GameObject _tempAnimationObject;

    private GameObject _tempPhysicsObject;

    private UIDocument _uiDocument;

    private Toggle _seamlessTeleportToggle;
    private TextField _exitSpeedField;
    private Toggle _triggerOnceToggle;
    private TextField _triggerCooldownField;
    private Toggle _sequentialTargetsToggle;
    private Toggle _boostToggle;
    private TextField _speedMultiplierField;
    private TextField _targetSpeedField;
    private Toggle _windToggle;
    private TextField _forceXField;
    private TextField _forceYField;
    private TextField _forceZField;
    private DropdownField _forceModeField;
    private Toggle _forceLocalToggle;
    private Toggle _routeBySpeedToggle;
    private TextField _routeSpeedThresholdField;
    private Toggle _playSoundToggle;
    private DropdownField _triggerActionField;
    private DropdownField _easingField;
    private Toggle _pingPongToggle;
    private Toggle _spinnerToggle;
    private TextField _spinAxisXField;
    private TextField _spinAxisYField;
    private TextField _spinAxisZField;
    private TextField _spinSpeedField;
    private Toggle _orbitToggle;
    private TextField _orbitRadiusField;
    private TextField _orbitSpeedField;
    private TextField _orbitAxisXField;
    private TextField _orbitAxisYField;
    private TextField _orbitAxisZField;
    private Toggle _orbitFacePathToggle;
    private TextField _phaseOffsetField;
    private Toggle _randomizePhaseToggle;
    private Toggle _killOnContactAnimToggle;
    private Toggle _killOnContactPhysToggle;
    private Button _copyConfigButton;
    private Button _pasteConfigButton;
    private Button _pathPreviewButton;
    private GameObject _pathPreviewObject;
    private bool _pathPreviewEnabled;
    private Slider _scrubSlider;
    private GameObject _scrubObject;
    private AnimationPlayer _scrubPlayer;
    private TextField _launchImpulseXField;
    private TextField _launchImpulseYField;
    private TextField _launchImpulseZField;
    private TextField _launchTorqueXField;
    private TextField _launchTorqueYField;
    private TextField _launchTorqueZField;
    private Toggle _overrideGravityToggle;
    private TextField _gravityScaleField;
    private TextField _linearDragField;
    private TextField _angularDragField;
    private TextField _massField;

    public Assets assets;

    private MO_TriggerOptions trigger
    {
        get => _blueprint.mo_triggerOptions;
        set => _blueprint.mo_triggerOptions = value;
    }

    private MO_AnimationOptions options => _blueprint.mo_animationOptions;
    private List<MO_Animation> steps => _blueprint.mo_animationSteps;

    private void Awake()
    {
        _uiDocument = gameObject.AddComponent<UIDocument>();
        _uiDocument.visualTreeAsset = assets.VisualTreeAsset;
        _uiDocument.panelSettings = assets.PanelSettings;
        _uiDocument.rootVisualElement.StretchToParentSize();

        Shared.Editor.OnRefreshGuiRequest += RefreshGui;
    }

    private void OnEnable()
    {
        // Dirty hack for add focus support
        GameObject.Find("AnimationEditorWindowPanelSettings").AddComponent<InputField>().interactable = false;

        _root = _uiDocument.rootVisualElement;

        _root.Q<Toggle>("trigger-enabled")
            .RegisterValueChangedCallback(evt =>
            {
                if (!evt.newValue)
                    trigger = null;
                else if (trigger == null)
                    trigger = new MO_TriggerOptions();
                RefreshGui();
            });

        _root.Q<TextField>("trigger-name").RegisterValueChangedCallback(evt => trigger.triggerName = evt.newValue);
        _root.Q<TextField>("trigger-target").RegisterValueChangedCallback(evt => trigger.triggerTarget = evt.newValue);
        GuiUtils.ConvertToFloatField(_root.Q<TextField>("trigger-target-speed-min"),
            f => trigger.triggerMinSpeed = f);
        GuiUtils.ConvertToFloatField(_root.Q<TextField>("trigger-target-speed-max"),
            f => trigger.triggerMaxSpeed = f);
        _root.Q<Toggle>("trigger-teleport").RegisterValueChangedCallback(evt => trigger.triggerTeleport = evt.newValue);
        EnsureTeleportControls();

        _root.Q<DropdownField>("type")
            .RegisterValueChangedCallback(evt =>
            {
                SetType(Enum.Parse<Type>(evt.newValue, true));
                RefreshGui();
            });
        _root.Q<Toggle>("animation-teleport-to-start")
            .RegisterValueChangedCallback(evt => options.teleportToStart = evt.newValue);
        _root.Q<Button>("animation-add").clicked += () =>
        {
            steps.Add(new MO_Animation
            {
                delay = 0f,
                time = 1f,
                position = new SerializableVector3(_item.transform.position),
                rotation = new SerializableVector3(_item.transform.rotation.eulerAngles)
            });
            RefreshGui();
        };
        GuiUtils.ConvertToFloatField(_root.Q<TextField>("animation-warmup"),
            f => options.animationWarmupDelay = f);
        GuiUtils.ConvertToIntField(_root.Q<TextField>("animation-repeats"),
            i => options.animationRepeats = i);
        _root.Q<Button>("animation-play").clicked += OnPlayAnimationClicked;

        GuiUtils.ConvertToFloatField(_root.Q<TextField>("physics-time"),
            f => options.simulatePhysicsTime = f);
        GuiUtils.ConvertToFloatField(_root.Q<TextField>("physics-delay"),
            f => options.simulatePhysicsDelay = f);
        GuiUtils.ConvertToFloatField(_root.Q<TextField>("physics-warmup"),
            f => options.simulatePhysicsWarmupDelay = f);
        _root.Q<Button>("physics-play").clicked += OnPlayPhysicsClicked;

        RefreshGui();
    }

    // The compiled UI bundle predates the seamless-teleport options, so these two controls are
    // created in code and appended to the trigger target section (so they inherit the same
    // visibility as the teleport toggle).
    //
    // The UIDocument's visual tree can be rebuilt out from under us (e.g. pressing F1 to align a
    // gate tears down and recreates it). On a rebuild the UXML-defined controls come back for free,
    // but our code-added ones are dropped while the field references survive — pointing at the now
    // detached old tree. So we can't guard on "field is non-null"; we guard on whether the control
    // is actually attached to the *current* target section, recreating it otherwise. This stays
    // idempotent (no duplicates) when called repeatedly against an unchanged tree.
    private void EnsureTeleportControls()
    {
        var targetSection = _root.Q<VisualElement>("trigger-target-section");
        if (targetSection == null)
            return;

        if (_seamlessTeleportToggle != null && _seamlessTeleportToggle.parent == targetSection)
            return;

        _seamlessTeleportToggle = new Toggle("Seamless teleport") { focusable = false };
        _seamlessTeleportToggle.RegisterValueChangedCallback(evt => trigger.seamlessTeleport = evt.newValue);
        targetSection.Add(_seamlessTeleportToggle);

        _exitSpeedField = new TextField("Exit speed:") { maxLength = 16 };
        GuiUtils.ConvertToFloatField(_exitSpeedField, f => trigger.exitSpeed = f);
        targetSection.Add(_exitSpeedField);

        _triggerOnceToggle = new Toggle("Trigger once (per flight)") { focusable = false };
        _triggerOnceToggle.RegisterValueChangedCallback(evt => trigger.triggerOnce = evt.newValue);
        targetSection.Add(_triggerOnceToggle);

        _triggerCooldownField = new TextField("Cooldown (s):") { maxLength = 16 };
        GuiUtils.ConvertToFloatField(_triggerCooldownField, f => trigger.triggerCooldown = f);
        targetSection.Add(_triggerCooldownField);

        _sequentialTargetsToggle = new Toggle("Sequential targets") { focusable = false };
        _sequentialTargetsToggle.RegisterValueChangedCallback(evt => trigger.sequentialTargets = evt.newValue);
        targetSection.Add(_sequentialTargetsToggle);

        _boostToggle = new Toggle("Boost / brake gate") { focusable = false };
        _boostToggle.RegisterValueChangedCallback(evt => trigger.boostEnabled = evt.newValue);
        targetSection.Add(_boostToggle);

        _speedMultiplierField = new TextField("Speed multiplier:") { maxLength = 16 };
        GuiUtils.ConvertToFloatField(_speedMultiplierField, f => trigger.speedMultiplier = f);
        targetSection.Add(_speedMultiplierField);

        _targetSpeedField = new TextField("Target speed (km/h):") { maxLength = 16 };
        GuiUtils.ConvertToFloatField(_targetSpeedField, f => trigger.targetSpeed = f);
        targetSection.Add(_targetSpeedField);

        _windToggle = new Toggle("Wind / force volume") { focusable = false };
        _windToggle.RegisterValueChangedCallback(evt => trigger.windEnabled = evt.newValue);
        targetSection.Add(_windToggle);

        _forceXField = MakeFloatField("Force X:", f => trigger.forceVector.x = f);
        _forceYField = MakeFloatField("Force Y:", f => trigger.forceVector.y = f);
        _forceZField = MakeFloatField("Force Z:", f => trigger.forceVector.z = f);
        targetSection.Add(_forceXField);
        targetSection.Add(_forceYField);
        targetSection.Add(_forceZField);

        _forceModeField = new DropdownField("Force mode:",
            new List<string> { "Force", "Acceleration" }, 0) { focusable = false };
        _forceModeField.RegisterValueChangedCallback(evt =>
            trigger.forceMode = evt.newValue == "Acceleration" ? 1 : 0);
        targetSection.Add(_forceModeField);

        _forceLocalToggle = new Toggle("Force in local space") { focusable = false };
        _forceLocalToggle.RegisterValueChangedCallback(evt => trigger.forceLocalSpace = evt.newValue);
        targetSection.Add(_forceLocalToggle);

        _routeBySpeedToggle = new Toggle("Route by speed") { focusable = false };
        _routeBySpeedToggle.RegisterValueChangedCallback(evt => trigger.routeBySpeed = evt.newValue);
        targetSection.Add(_routeBySpeedToggle);

        _routeSpeedThresholdField = new TextField("Route threshold (km/h):") { maxLength = 16 };
        GuiUtils.ConvertToFloatField(_routeSpeedThresholdField, f => trigger.routeSpeedThreshold = f);
        targetSection.Add(_routeSpeedThresholdField);

        _playSoundToggle = new Toggle("Play sound on trigger") { focusable = false };
        _playSoundToggle.RegisterValueChangedCallback(evt => trigger.playSoundOnTrigger = evt.newValue);
        targetSection.Add(_playSoundToggle);
    }

    // Same code-added pattern as EnsureTeleportControls, but for the Trigger action dropdown
    // (Restart vs. Stop). It binds to MO_AnimationOptions and lives in the animation box, so it
    // is created here and re-added in RefreshGui if a tree rebuild drops it.
    private void EnsureAnimationActionControl()
    {
        var animationBox = _root.Q<GroupBox>("animation-box");
        if (animationBox == null)
            return;

        if (_triggerActionField != null && _triggerActionField.parent == animationBox)
            return;

        _triggerActionField = new DropdownField("Trigger action:",
            Enum.GetNames(typeof(MO_TriggerAction)).ToList(), 0) { focusable = false };
        _triggerActionField.RegisterValueChangedCallback(evt =>
            options.triggerAction = (int)Enum.Parse<MO_TriggerAction>(evt.newValue, true));
        animationBox.Add(_triggerActionField);
    }

    // Home for all animation-box controls added in code (the compiled UI bundle predates them).
    // Same idempotent, guard-on-.parent contract as EnsureAnimationActionControl; grows as new
    // animation options are added. Re-invoked from RefreshGui so a tree rebuild re-adds them.
    private void EnsureAnimationControls()
    {
        var animationBox = _root.Q<GroupBox>("animation-box");
        if (animationBox == null)
            return;

        if (_easingField != null && _easingField.parent == animationBox)
            return;

        _easingField = new DropdownField("Easing:",
            Enum.GetNames(typeof(MO_Easing)).ToList(), 0) { focusable = false };
        _easingField.RegisterValueChangedCallback(evt =>
            options.easingMode = (int)Enum.Parse<MO_Easing>(evt.newValue, true));
        animationBox.Add(_easingField);

        _pingPongToggle = new Toggle("Ping-pong") { focusable = false };
        _pingPongToggle.RegisterValueChangedCallback(evt => options.pingPong = evt.newValue);
        animationBox.Add(_pingPongToggle);

        _spinnerToggle = new Toggle("Spinner (constant rotation)") { focusable = false };
        _spinnerToggle.RegisterValueChangedCallback(evt => options.spinnerEnabled = evt.newValue);
        animationBox.Add(_spinnerToggle);

        _spinAxisXField = MakeFloatField("Spin axis X:", f => options.spinAxis.x = f);
        _spinAxisYField = MakeFloatField("Spin axis Y:", f => options.spinAxis.y = f);
        _spinAxisZField = MakeFloatField("Spin axis Z:", f => options.spinAxis.z = f);
        animationBox.Add(_spinAxisXField);
        animationBox.Add(_spinAxisYField);
        animationBox.Add(_spinAxisZField);

        _spinSpeedField = MakeFloatField("Spin speed (deg/s):", f => options.spinSpeed = f);
        animationBox.Add(_spinSpeedField);

        _orbitToggle = new Toggle("Orbit (circular path)") { focusable = false };
        _orbitToggle.RegisterValueChangedCallback(evt => options.orbitEnabled = evt.newValue);
        animationBox.Add(_orbitToggle);

        _orbitRadiusField = MakeFloatField("Orbit radius:", f => options.orbitRadius = f);
        _orbitSpeedField = MakeFloatField("Orbit speed (deg/s):", f => options.orbitSpeed = f);
        _orbitAxisXField = MakeFloatField("Orbit axis X:", f => options.orbitAxis.x = f);
        _orbitAxisYField = MakeFloatField("Orbit axis Y:", f => options.orbitAxis.y = f);
        _orbitAxisZField = MakeFloatField("Orbit axis Z:", f => options.orbitAxis.z = f);
        animationBox.Add(_orbitRadiusField);
        animationBox.Add(_orbitSpeedField);
        animationBox.Add(_orbitAxisXField);
        animationBox.Add(_orbitAxisYField);
        animationBox.Add(_orbitAxisZField);

        _orbitFacePathToggle = new Toggle("Face along path") { focusable = false };
        _orbitFacePathToggle.RegisterValueChangedCallback(evt => options.orbitFacePath = evt.newValue);
        animationBox.Add(_orbitFacePathToggle);

        _phaseOffsetField = MakeFloatField("Phase offset (s):", f => options.phaseOffset = f);
        animationBox.Add(_phaseOffsetField);

        _randomizePhaseToggle = new Toggle("Randomize phase") { focusable = false };
        _randomizePhaseToggle.RegisterValueChangedCallback(evt => options.randomizePhase = evt.newValue);
        animationBox.Add(_randomizePhaseToggle);

        _killOnContactAnimToggle = new Toggle("Kill drone on contact") { focusable = false };
        _killOnContactAnimToggle.RegisterValueChangedCallback(evt => options.killOnContact = evt.newValue);
        animationBox.Add(_killOnContactAnimToggle);

        _pathPreviewButton = new Button(TogglePathPreview) { text = "Toggle path preview", focusable = false };
        animationBox.Add(_pathPreviewButton);

        _scrubSlider = new Slider("Scrub:", 0f, 1f) { focusable = false };
        _scrubSlider.RegisterValueChangedCallback(evt => ScrubTo(evt.newValue));
        animationBox.Add(_scrubSlider);
    }

    // Timeline scrubber: drives a dedicated preview clone to the slider's normalized time via
    // AnimationPlayer.SampleAt. The clone captures the steps at creation; deselect/reselect to
    // pick up later step edits. Torn down alongside the other previews.
    private void ScrubTo(float normalizedTime)
    {
        if (_blueprint == null || steps == null || steps.Count == 0)
            return;

        StopAnimation();
        if (_scrubObject == null)
        {
            _scrubObject = CreateTempObj();
            _scrubPlayer = _scrubObject.AddComponent<AnimationPlayer>();
            _scrubPlayer.steps = new List<MO_Animation>(steps);
            _scrubPlayer.options = options;
            _scrubPlayer.waitForTrigger = true; // don't auto-play; we pose it manually
        }

        _scrubPlayer.SampleAt(normalizedTime);
    }

    private void DestroyScrub()
    {
        if (_scrubObject == null)
            return;
        Destroy(_scrubObject);
        _scrubObject = null;
        _scrubPlayer = null;
    }

    private void TogglePathPreview()
    {
        _pathPreviewEnabled = !_pathPreviewEnabled;
        RefreshGui();
    }

    private void UpdatePathPreview()
    {
        if (!_pathPreviewEnabled || steps == null || steps.Count == 0)
        {
            DestroyPathPreview();
            return;
        }

        if (_pathPreviewObject == null)
            _pathPreviewObject = new GameObject("MO_PathPreview");
        var preview = _pathPreviewObject.GetComponent<PathPreview>() ??
                      _pathPreviewObject.AddComponent<PathPreview>();

        var positions = steps.Select(s => new Vector3(s.position.x, s.position.y, s.position.z)).ToList();
        var rotations = steps.Select(s => Quaternion.Euler(s.rotation.x, s.rotation.y, s.rotation.z)).ToList();
        preview.SetPath(positions, rotations);
    }

    private void DestroyPathPreview()
    {
        if (_pathPreviewObject == null)
            return;
        Destroy(_pathPreviewObject);
        _pathPreviewObject = null;
    }

    // Home for all physics-box controls added in code (same idempotent contract as the animation
    // one). Grows as new physics options are added.
    private void EnsurePhysicsControls()
    {
        var physicsBox = _root.Q<GroupBox>("physics-box");
        if (physicsBox == null)
            return;

        if (_launchImpulseXField != null && _launchImpulseXField.parent == physicsBox)
            return;

        _launchImpulseXField = MakeFloatField("Launch impulse X:", f => options.launchImpulse.x = f);
        _launchImpulseYField = MakeFloatField("Launch impulse Y:", f => options.launchImpulse.y = f);
        _launchImpulseZField = MakeFloatField("Launch impulse Z:", f => options.launchImpulse.z = f);
        physicsBox.Add(_launchImpulseXField);
        physicsBox.Add(_launchImpulseYField);
        physicsBox.Add(_launchImpulseZField);

        _launchTorqueXField = MakeFloatField("Launch torque X:", f => options.launchTorque.x = f);
        _launchTorqueYField = MakeFloatField("Launch torque Y:", f => options.launchTorque.y = f);
        _launchTorqueZField = MakeFloatField("Launch torque Z:", f => options.launchTorque.z = f);
        physicsBox.Add(_launchTorqueXField);
        physicsBox.Add(_launchTorqueYField);
        physicsBox.Add(_launchTorqueZField);

        _overrideGravityToggle = new Toggle("Override gravity") { focusable = false };
        _overrideGravityToggle.RegisterValueChangedCallback(evt => options.overrideGravity = evt.newValue);
        physicsBox.Add(_overrideGravityToggle);

        _gravityScaleField = MakeFloatField("Gravity scale (1 = normal):", f => options.gravityScale = f);
        _linearDragField = MakeFloatField("Linear drag:", f => options.linearDrag = f);
        _angularDragField = MakeFloatField("Angular drag:", f => options.angularDrag = f);
        _massField = MakeFloatField("Mass (0 = immovable):", f => options.mass = f);
        physicsBox.Add(_gravityScaleField);
        physicsBox.Add(_linearDragField);
        physicsBox.Add(_angularDragField);
        physicsBox.Add(_massField);

        _killOnContactPhysToggle = new Toggle("Kill drone on contact") { focusable = false };
        _killOnContactPhysToggle.RegisterValueChangedCallback(evt => options.killOnContact = evt.newValue);
        physicsBox.Add(_killOnContactPhysToggle);
    }

    // Small helper for the many code-added, validated float fields the options panels need.
    private static TextField MakeFloatField(string label, Action<float> onChange)
    {
        var field = new TextField(label) { maxLength = 16 };
        GuiUtils.ConvertToFloatField(field, onChange);
        return field;
    }

    // Cap the editor panel to the screen and scroll the overflow. The animation box alone now carries
    // ~20 controls (spinner / orbit / phase / physics plus the step list), so on 1080p the panel ran
    // off the bottom of the screen with the lower controls unreachable (honk: "the window has too much
    // content — won't fit anymore"). We wrap the panel content in a ScrollView once. Controls appended
    // later to animation-box / physics-box land inside it automatically (they're descendants), and the
    // #root lookups elsewhere still resolve because Q() searches the whole subtree.
    //
    // Same idempotent, tree-rebuild-safe contract as the other Ensure* methods: the ScrollView is a
    // direct child of the #root container, so a surviving _panelScroll whose parent is still that
    // container means the wrap is intact; a rebuild (F1) recreates #root and we re-wrap the fresh tree.
    private void EnsurePanelScroll()
    {
        var container = _root.Q<VisualElement>("root");
        if (container == null || (_panelScroll != null && _panelScroll.parent == container))
            return;

        _panelScroll = new ScrollView(ScrollViewMode.Vertical);
        // Leave headroom for the panel's top offset (UpdateMenuPos) so the capped panel stays on-screen.
        _panelScroll.style.maxHeight = new StyleLength(new Length(85, LengthUnit.Percent));

        foreach (var child in container.Children().ToList())
            _panelScroll.Add(child);
        container.Add(_panelScroll);
    }

    // Home for the panel-content parent: the scroll view once wrapped (see EnsurePanelScroll), else
    // the raw #root container as a fallback. Code-added controls that belong at panel level go here.
    private VisualElement PanelContent() => (VisualElement)_panelScroll ?? _root.Q<VisualElement>("root");

    // Copy/Paste of the whole MO configuration between objects — author one moving gate, stamp it
    // onto twenty. Same idempotent code-added pattern; lives inside the scrollable panel content.
    private void EnsureClipboardControls()
    {
        var container = PanelContent();
        if (container == null)
            return;

        // Contains (not parent ==) because a ScrollView holds its children in an inner contentContainer,
        // so the button's direct parent isn't the ScrollView itself.
        if (_copyConfigButton != null && container.Contains(_copyConfigButton))
            return;

        _copyConfigButton = new Button(CopyConfig) { text = "Copy MO config", focusable = false };
        container.Add(_copyConfigButton);

        _pasteConfigButton = new Button(PasteConfig) { text = "Paste MO config", focusable = false };
        container.Add(_pasteConfigButton);
    }

    private void CopyConfig()
    {
        Shared.Clipboard.animationOptions = CloneUtils.DeepClone(options);
        Shared.Clipboard.animationSteps = CloneUtils.DeepClone(steps);
        Shared.Clipboard.triggerOptions = CloneUtils.DeepClone(trigger);
        RefreshGui();
    }

    private void PasteConfig()
    {
        if (!Shared.Clipboard.HasData || _blueprint == null)
            return;

        _blueprint.mo_animationOptions = CloneUtils.DeepClone(Shared.Clipboard.animationOptions);
        _blueprint.mo_animationSteps = CloneUtils.DeepClone(Shared.Clipboard.animationSteps);
        _blueprint.mo_triggerOptions = CloneUtils.DeepClone(Shared.Clipboard.triggerOptions);
        RefreshGui();
    }

    private void OnPlayAnimationClicked()
    {
        if (_tempAnimationObject == null)
            StartAnimation();
        else
            StopAnimation();
        RefreshGui();
    }

    private void OnPlayPhysicsClicked()
    {
        if (_tempPhysicsObject == null)
            StartSimulation();
        else
            StopSimulation();
        RefreshGui();
    }

    private IEnumerator UpdateMenuPos()
    {
        yield return new WaitForFixedUpdate();
        var rect = GameObject.Find("DetailWindows")?.GetComponent<RectTransform>();
        if (rect != null)
            _root.Q<VisualElement>("root").style.top =
                new StyleLength(new Length(rect.sizeDelta.y + 10, LengthUnit.Pixel));
    }


    private void RefreshGui()
    {
        if (_blueprint == null)
            return;

        StartCoroutine(UpdateMenuPos());

        EnsurePanelScroll();
        EnsureClipboardControls();
        _pasteConfigButton?.SetEnabled(Shared.Clipboard.HasData);

        var currentType = options == null ? Type.None : options.simulatePhysics ? Type.Physics : Type.Animation;
        _root.Q<DropdownField>("type").value = currentType.ToString();

        var hasTrigger = trigger != null;

        _root.Q<Toggle>("trigger-enabled").value = hasTrigger;
        GuiUtils.SetVisible(_root.Q<GroupBox>("trigger-box"), hasTrigger);
        if (hasTrigger)
        {
            var triggerName = _root.Q<TextField>("trigger-name");
            triggerName.value = trigger.triggerName;

            var triggerTarget = _root.Q<TextField>("trigger-target");
            triggerTarget.value = trigger.triggerTarget;

            var triggerTeleport = _root.Q<Toggle>("trigger-teleport");
            triggerTeleport.value = trigger.triggerTeleport;

            var triggerTargetGroup = _root.Q<VisualElement>("trigger-target-section");

            GuiUtils.SetVisible(triggerName, true);
            GuiUtils.SetVisible(triggerTargetGroup, _blueprint.itemID.StartsWith("Checkpoint"));

            _root.Q<TextField>("trigger-target-speed-min").value = GuiUtils.FloatToString(trigger.triggerMinSpeed);
            _root.Q<TextField>("trigger-target-speed-max").value = GuiUtils.FloatToString(trigger.triggerMaxSpeed);

            // Re-add our code-created controls if a tree rebuild dropped them (see EnsureTeleportControls).
            EnsureTeleportControls();
            if (_seamlessTeleportToggle != null)
                _seamlessTeleportToggle.SetValueWithoutNotify(trigger.seamlessTeleport);
            if (_exitSpeedField != null)
                _exitSpeedField.SetValueWithoutNotify(GuiUtils.FloatToString(trigger.exitSpeed));
            if (_triggerOnceToggle != null)
                _triggerOnceToggle.SetValueWithoutNotify(trigger.triggerOnce);
            if (_triggerCooldownField != null)
                _triggerCooldownField.SetValueWithoutNotify(GuiUtils.FloatToString(trigger.triggerCooldown));
            if (_sequentialTargetsToggle != null)
                _sequentialTargetsToggle.SetValueWithoutNotify(trigger.sequentialTargets);
            if (_boostToggle != null)
            {
                _boostToggle.SetValueWithoutNotify(trigger.boostEnabled);
                _speedMultiplierField.SetValueWithoutNotify(GuiUtils.FloatToString(trigger.speedMultiplier));
                _targetSpeedField.SetValueWithoutNotify(GuiUtils.FloatToString(trigger.targetSpeed));
            }
            if (_windToggle != null)
            {
                _windToggle.SetValueWithoutNotify(trigger.windEnabled);
                _forceXField.SetValueWithoutNotify(GuiUtils.FloatToString(trigger.forceVector.x));
                _forceYField.SetValueWithoutNotify(GuiUtils.FloatToString(trigger.forceVector.y));
                _forceZField.SetValueWithoutNotify(GuiUtils.FloatToString(trigger.forceVector.z));
                _forceModeField.SetValueWithoutNotify(trigger.forceMode == 1 ? "Acceleration" : "Force");
                _forceLocalToggle.SetValueWithoutNotify(trigger.forceLocalSpace);
            }
            if (_routeBySpeedToggle != null)
            {
                _routeBySpeedToggle.SetValueWithoutNotify(trigger.routeBySpeed);
                _routeSpeedThresholdField.SetValueWithoutNotify(GuiUtils.FloatToString(trigger.routeSpeedThreshold));
            }
            if (_playSoundToggle != null)
                _playSoundToggle.SetValueWithoutNotify(trigger.playSoundOnTrigger);
        }

        var animationBox = _root.Q<GroupBox>("animation-box");
        var physicsBox = _root.Q<GroupBox>("physics-box");

        switch (currentType)
        {
            case Type.Animation:
                GuiUtils.SetVisible(animationBox, true);
                GuiUtils.SetVisible(physicsBox, false);

                _root.Q<Toggle>("animation-teleport-to-start").value = options.teleportToStart;
                _root.Q<TextField>("animation-warmup").value = GuiUtils.FloatToString(options.animationWarmupDelay);
                _root.Q<TextField>("animation-repeats").value = options.animationRepeats.ToString();

                EnsureAnimationActionControl();
                if (_triggerActionField != null)
                    _triggerActionField.SetValueWithoutNotify(
                        ((MO_TriggerAction)options.triggerAction).ToString());

                EnsureAnimationControls();
                if (_easingField != null)
                    _easingField.SetValueWithoutNotify(((MO_Easing)options.easingMode).ToString());
                if (_pingPongToggle != null)
                    _pingPongToggle.SetValueWithoutNotify(options.pingPong);
                if (_spinnerToggle != null)
                {
                    _spinnerToggle.SetValueWithoutNotify(options.spinnerEnabled);
                    _spinAxisXField.SetValueWithoutNotify(GuiUtils.FloatToString(options.spinAxis.x));
                    _spinAxisYField.SetValueWithoutNotify(GuiUtils.FloatToString(options.spinAxis.y));
                    _spinAxisZField.SetValueWithoutNotify(GuiUtils.FloatToString(options.spinAxis.z));
                    _spinSpeedField.SetValueWithoutNotify(GuiUtils.FloatToString(options.spinSpeed));
                }
                if (_orbitToggle != null)
                {
                    _orbitToggle.SetValueWithoutNotify(options.orbitEnabled);
                    _orbitRadiusField.SetValueWithoutNotify(GuiUtils.FloatToString(options.orbitRadius));
                    _orbitSpeedField.SetValueWithoutNotify(GuiUtils.FloatToString(options.orbitSpeed));
                    _orbitAxisXField.SetValueWithoutNotify(GuiUtils.FloatToString(options.orbitAxis.x));
                    _orbitAxisYField.SetValueWithoutNotify(GuiUtils.FloatToString(options.orbitAxis.y));
                    _orbitAxisZField.SetValueWithoutNotify(GuiUtils.FloatToString(options.orbitAxis.z));
                    _orbitFacePathToggle.SetValueWithoutNotify(options.orbitFacePath);
                }
                if (_phaseOffsetField != null)
                {
                    _phaseOffsetField.SetValueWithoutNotify(GuiUtils.FloatToString(options.phaseOffset));
                    _randomizePhaseToggle.SetValueWithoutNotify(options.randomizePhase);
                    _killOnContactAnimToggle.SetValueWithoutNotify(options.killOnContact);
                }

                GuiUtils.SetVisible(_root.Q<Label>("animation-steps-empty"), steps.Count == 0);

                var animationPlay = _root.Q<Button>("animation-play");
                animationPlay.text = _tempAnimationObject == null ? PlayButtonText : StopButtonText;

                var stepsContainer = _root.Q<ScrollView>("animation-steps");
                stepsContainer.Clear();
                for (var i = 0; i < steps.Count; i++)
                    AddStepElement(stepsContainer, steps[i], i);

                UpdatePathPreview();
                break;
            case Type.Physics:
                GuiUtils.SetVisible(animationBox, false);
                GuiUtils.SetVisible(physicsBox, true);
                DestroyPathPreview();
                DestroyScrub();

                _root.Q<TextField>("physics-time").value = GuiUtils.FloatToString(options.simulatePhysicsTime);
                _root.Q<TextField>("physics-delay").value = GuiUtils.FloatToString(options.simulatePhysicsDelay);
                _root.Q<TextField>("physics-warmup").value = GuiUtils.FloatToString(options.simulatePhysicsWarmupDelay);

                EnsurePhysicsControls();
                if (_launchImpulseXField != null)
                {
                    _launchImpulseXField.SetValueWithoutNotify(GuiUtils.FloatToString(options.launchImpulse.x));
                    _launchImpulseYField.SetValueWithoutNotify(GuiUtils.FloatToString(options.launchImpulse.y));
                    _launchImpulseZField.SetValueWithoutNotify(GuiUtils.FloatToString(options.launchImpulse.z));
                    _launchTorqueXField.SetValueWithoutNotify(GuiUtils.FloatToString(options.launchTorque.x));
                    _launchTorqueYField.SetValueWithoutNotify(GuiUtils.FloatToString(options.launchTorque.y));
                    _launchTorqueZField.SetValueWithoutNotify(GuiUtils.FloatToString(options.launchTorque.z));
                    _overrideGravityToggle.SetValueWithoutNotify(options.overrideGravity);
                    _gravityScaleField.SetValueWithoutNotify(GuiUtils.FloatToString(options.gravityScale));
                    _linearDragField.SetValueWithoutNotify(GuiUtils.FloatToString(options.linearDrag));
                    _angularDragField.SetValueWithoutNotify(GuiUtils.FloatToString(options.angularDrag));
                    _massField.SetValueWithoutNotify(GuiUtils.FloatToString(options.mass));
                    _killOnContactPhysToggle.SetValueWithoutNotify(options.killOnContact);
                }

                var physicsPlay = _root.Q<Button>("physics-play");
                physicsPlay.text = _tempPhysicsObject == null ? PlayButtonText : StopButtonText;
                break;
            case Type.None:
                GuiUtils.SetVisible(animationBox, false);
                GuiUtils.SetVisible(physicsBox, false);
                DestroyPathPreview();
                DestroyScrub();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(currentType), currentType, null);
        }
    }

    private void AddStepElement(VisualElement stepsContainer, MO_Animation step, int i)
    {
        var item = assets.AnimationTemplateAsset.Instantiate();

        item.Q<Label>("id").text = i.ToString();
        item.Q<Label>("position").text = GuiUtils.VectorToString(step.position);
        item.Q<Label>("rotation").text = GuiUtils.VectorToString(step.rotation);
        item.Q<TextField>("time").value = GuiUtils.FloatToString(step.time);
        item.Q<TextField>("delay").value = GuiUtils.FloatToString(step.delay);

        GuiUtils.ConvertToFloatField(item.Q<TextField>("time"), f => step.time = f, step.time);
        GuiUtils.ConvertToFloatField(item.Q<TextField>("delay"), f => step.delay = f, step.delay);

        item.Q<Button>("delete").clicked += () =>
        {
            steps.Remove(step);
            RefreshGui();
        };

        // Code-added row editing (the template only had a delete button): re-capture the pose from
        // the live gizmo, reorder, and insert — so a typo no longer means deleting and re-capturing
        // everything after it.
        var index = i;

        var updateButton = new Button(() =>
        {
            step.position = new SerializableVector3(_item.transform.position);
            step.rotation = new SerializableVector3(_item.transform.rotation.eulerAngles);
            RefreshGui();
        }) { text = "Update", focusable = false };
        item.Add(updateButton);

        var upButton = new Button(() =>
        {
            if (index <= 0)
                return;
            (steps[index], steps[index - 1]) = (steps[index - 1], steps[index]);
            RefreshGui();
        }) { text = "↑", focusable = false };
        item.Add(upButton);

        var downButton = new Button(() =>
        {
            if (index >= steps.Count - 1)
                return;
            (steps[index], steps[index + 1]) = (steps[index + 1], steps[index]);
            RefreshGui();
        }) { text = "↓", focusable = false };
        item.Add(downButton);

        var insertButton = new Button(() =>
        {
            steps.Insert(index + 1, new MO_Animation
            {
                delay = 0f,
                time = 1f,
                position = new SerializableVector3(_item.transform.position),
                rotation = new SerializableVector3(_item.transform.rotation.eulerAngles)
            });
            RefreshGui();
        }) { text = "+", focusable = false };
        item.Add(insertButton);

        stepsContainer.Add(item);
    }

    private void SetType(Type type)
    {
        switch (type)
        {
            case Type.None:
                _blueprint.mo_animationOptions = null;
                _blueprint.mo_animationSteps = null;
                break;
            case Type.Animation:
                _blueprint.mo_animationOptions ??= new MO_AnimationOptions();
                _blueprint.mo_animationSteps ??= new List<MO_Animation>();
                _blueprint.mo_animationOptions.simulatePhysics = false;
                StopSimulation();
                break;
            case Type.Physics:
                _blueprint.mo_animationOptions ??= new MO_AnimationOptions();
                _blueprint.mo_animationSteps ??= new List<MO_Animation>();
                _blueprint.mo_animationSteps.Clear();
                _blueprint.mo_animationOptions.simulatePhysics = true;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, null);
        }
    }

    public void OnItemSelected(MonoBehaviour item)
    {
        StopAnimation();
        StopSimulation();

        _item = item;
        _blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(item);
        Invoke("RefreshGui", 0);
        Shared.Editor.ItemSelected(new Shared.Editor.ItemInfo
        {
            gameObject = item.gameObject,
            blueprint = _blueprint
        });

        Log.LogInfo(
            $"Item selected: {_blueprint.itemID}/{_blueprint.instanceID}, {_item.gameObject.transform.position}");
    }

    public void OnItemCleared()
    {
        StopAnimation();
        StopSimulation();
        DestroyPathPreview();
        DestroyScrub();
        _blueprint = null;
        Shared.Editor.ItemCleared();
        Log.LogInfo("Item unselected");
    }

    private void OnDestroy()
    {
        StopAnimation();
        StopSimulation();
        DestroyPathPreview();
        DestroyScrub();
    }

    private GameObject CreateTempObj()
    {
        if (string.IsNullOrEmpty(_blueprint.mo_groupId))
        {
            var obj = Instantiate(_item.gameObject);
            obj.transform.SetPositionAndRotation(_item.transform.position, _item.transform.rotation);
            
            obj.transform.localScale = _item.gameObject.transform.localScale;
            return obj;
        }

        var childs = new List<GameObject>();
        GameObject rootObj = null;
        var flags = EditorUtils.FindFlagsByGroupId(_blueprint.mo_groupId);
        foreach (var flag in flags)
        {
            var clone = Instantiate(flag.gameObject);
            clone.transform.SetPositionAndRotation(flag.gameObject.transform.position, flag.gameObject.transform.rotation);
            clone.transform.localScale = flag.gameObject.transform.localScale;
            if (flag.gameObject == _item.gameObject)
                rootObj = clone;
            else
                childs.Add(clone);
        }

        FakeGroup.GroupObjects(rootObj, childs, true);
        return rootObj;
    }

    // Physics preview needs a real compound body, not FakeGroup's visual-follow. FakeGroup only
    // makes the members *follow* the root each frame, so the root's single Rigidbody would collide
    // as just its own half of the assembly (e.g. one half-sphere bowl that drops but can't roll).
    // Transform-parenting the members under the root (as GroupFlags does in flight) turns their
    // colliders into compound colliders of that one body, so the group behaves as a solid whole.
    private GameObject CreateTempPhysicsObj()
    {
        if (string.IsNullOrEmpty(_blueprint.mo_groupId))
            return CreateTempObj();

        GameObject rootObj = null;
        var childs = new List<GameObject>();
        foreach (var flag in EditorUtils.FindFlagsByGroupId(_blueprint.mo_groupId))
        {
            var clone = Instantiate(flag.gameObject);
            clone.transform.SetPositionAndRotation(flag.gameObject.transform.position, flag.gameObject.transform.rotation);
            clone.transform.localScale = flag.gameObject.transform.localScale;
            if (flag.gameObject == _item.gameObject)
                rootObj = clone;
            else
                childs.Add(clone);
        }

        foreach (var child in childs)
            child.transform.SetParent(rootObj.transform, true);

        return rootObj;
    }

    private void StartAnimation()
    {
        Log.LogWarning($"Animation start: {_item.gameObject} at {_item.transform.position}");

        DestroyScrub();
        _tempAnimationObject = CreateTempObj();
        var player = _tempAnimationObject.AddComponent<AnimationPlayer>();
        player.steps = new List<MO_Animation>(_blueprint.mo_animationSteps);
        player.options = _blueprint.mo_animationOptions;
    }

    private void StopAnimation()
    {
        if (_tempAnimationObject != null)
        {
            Destroy(_tempAnimationObject);
            _tempAnimationObject = null;
        }
    }

    private void StartSimulation()
    {
        List<GameObject> groupObjects = null;
        if (!string.IsNullOrEmpty(_blueprint.mo_groupId))
            groupObjects = EditorUtils.FindFlagsByGroupId(_blueprint.mo_groupId).Select(c => c.gameObject).ToList();

        _tempPhysicsObject = CreateTempPhysicsObj();

        var tempColliders = _tempPhysicsObject.GetComponentsInChildren<Collider>(true).ToList();

        // Match the in-flight body: enable every member collider and convex-hull mesh colliders so
        // the dynamic Rigidbody keeps them (Unity drops non-convex meshes from a non-kinematic body).
        foreach (var tempCollider in tempColliders)
        {
            if (tempCollider is MeshCollider meshCollider)
                meshCollider.convex = true;
            tempCollider.enabled = true;
        }

        var targetColliders = new List<Collider>();
        targetColliders.AddRange(_item.gameObject.GetComponentsInChildren<Collider>());
        targetColliders.AddRange(GameObject.Find("TrackEditorGizmo").GetComponentsInChildren<Collider>());
        if (groupObjects != null)
            targetColliders.AddRange(groupObjects.SelectMany(c => c.GetComponentsInChildren<Collider>()));

        foreach (var tempCollider in tempColliders)
        foreach (var targetCollider in targetColliders)
            Physics.IgnoreCollision(tempCollider, targetCollider);

        var player = _tempPhysicsObject.AddComponent<PhysicsPlayer>();
        player.options = _blueprint.mo_animationOptions;
    }

    private void StopSimulation()
    {
        if (_tempPhysicsObject != null)
        {
            Destroy(_tempPhysicsObject);
            _tempPhysicsObject = null;
        }
    }

    public struct Assets
    {
        public VisualTreeAsset VisualTreeAsset { get; set; }
        public VisualTreeAsset AnimationTemplateAsset { get; set; }
        public PanelSettings PanelSettings { get; set; }
    }
}