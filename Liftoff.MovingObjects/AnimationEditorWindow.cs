using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using BepInEx.Logging;
using Liftoff.MovingObjects.Player;
using Liftoff.MovingObjects.Utils;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.UIElements;
using static Liftoff.FlightControllers.FlightMode;
using Button = UnityEngine.UIElements.Button;
using Logger = BepInEx.Logging.Logger;
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
    private const string NotSupportedButtonText = "CURRENTLY IN DEVELOPEMENT";

    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{PluginInfo.PLUGIN_NAME}.{nameof(AnimationEditorWindow)}");

    private TrackBlueprint _blueprint;
    private MonoBehaviour _item;
    private VisualElement _root;
    private GameObject _tempAnimationObject;

    private GameObject _tempPhysicsObject;

    private UIDocument _uiDocument;

    private Toggle _seamlessTeleportToggle;
    private TextField _exitSpeedField;
    private Toggle _triggerOnceToggle;
    private TextField _triggerCooldownField;
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
    }

    // Small helper for the many code-added, validated float fields the options panels need.
    private static TextField MakeFloatField(string label, Action<float> onChange)
    {
        var field = new TextField(label) { maxLength = 16 };
        GuiUtils.ConvertToFloatField(field, onChange);
        return field;
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
        if (!string.IsNullOrEmpty(_blueprint.mo_groupId))
            return; // TODO: Fix group physics

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
                }

                GuiUtils.SetVisible(_root.Q<Label>("animation-steps-empty"), steps.Count == 0);

                var animationPlay = _root.Q<Button>("animation-play");
                animationPlay.text = _tempAnimationObject == null ? PlayButtonText : StopButtonText;

                var stepsContainer = _root.Q<ScrollView>("animation-steps");
                stepsContainer.Clear();
                for (var i = 0; i < steps.Count; i++)
                    AddStepElement(stepsContainer, steps[i], i);
                break;
            case Type.Physics:
                GuiUtils.SetVisible(animationBox, false);
                GuiUtils.SetVisible(physicsBox, true);

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
                }

                var physicsPlay = _root.Q<Button>("physics-play");

                if (!string.IsNullOrEmpty(_blueprint.mo_groupId)) // TODO: Fix group physics
                    physicsPlay.text = NotSupportedButtonText;
                else
                    physicsPlay.text = _tempPhysicsObject == null ? PlayButtonText : StopButtonText;
                break;
            case Type.None:
                GuiUtils.SetVisible(animationBox, false);
                GuiUtils.SetVisible(physicsBox, false);
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
        _blueprint = null;
        Shared.Editor.ItemCleared();
        Log.LogInfo("Item unselected");
    }

    private void OnDestroy()
    {
        StopAnimation();
        StopSimulation();
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

    private void StartAnimation()
    {
        Log.LogWarning($"Animation start: {_item.gameObject} at {_item.transform.position}");

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

        _tempPhysicsObject = CreateTempObj();

        var tempColliders = _tempPhysicsObject.GetComponentsInChildren<Collider>().ToList();

        var tempGroupObjects = FakeGroup.GetChilds(_tempPhysicsObject);
        if (tempGroupObjects != null)
            tempColliders.AddRange(tempGroupObjects.SelectMany(o => o.GetComponentsInChildren<Collider>()));
        
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