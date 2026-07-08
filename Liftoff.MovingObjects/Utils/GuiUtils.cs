using System;
using System.Globalization;
using UnityEngine.UIElements;

namespace Liftoff.MovingObjects.Utils;

internal static class GuiUtils
{
    public static string FloatToString(float value)
    {
        return value.ToString("0.000", CultureInfo.InvariantCulture);
    }

    public static void SetVisible(VisualElement element, bool visible)
    {
        element.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
    }

    public static void ToggleVisible(VisualElement element)
    {
        element.style.display = element.style.display == DisplayStyle.None ? DisplayStyle.Flex : DisplayStyle.None;
    }

    // Keep the arrow keys inside a focused text field instead of letting UI Toolkit's directional
    // focus navigation carry them out of it. When the caret is at the end of a field, an arrow the
    // TextField can't use for the caret is turned into a NavigationMoveEvent that moves focus to
    // another element. In Liftoff that focus-exit also drops the game's "a field is focused, don't
    // move the avatar" suppression, so the same key then reaches the fly-camera — hence honk's "Right
    // arrow moves the avatar and exits the field" (Left stays in the field, so it never leaked). We
    // swallow the navigation while a text field is focused (trickle-down, so it runs before the
    // panel's focus controller acts), which keeps focus — and the arrows — in the field, matching how
    // the game's own input fields behave. Call once per panel with its root; the callback lives on the
    // long-lived root element and covers every descendant TextField.
    public static void KeepArrowsInTextFields(VisualElement root)
    {
        if (root == null)
            return;

        root.RegisterCallback<NavigationMoveEvent>(evt =>
        {
            var element = root.panel?.focusController?.focusedElement as VisualElement;
            while (element != null)
            {
                if (element is TextField)
                {
                    evt.StopPropagation();
                    return;
                }

                element = element.parent;
            }
        }, TrickleDown.TrickleDown);
    }

    public static void ConvertToIntField(TextField field, Action<int> valueCallback, int defaultValue = 0)
    {
        field.SetValueWithoutNotify(defaultValue.ToString());
        field.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (!char.IsDigit(evt.character))
                evt.PreventDefault();
        });

        field.RegisterValueChangedCallback(evt =>
        {
            if (!int.TryParse(evt.newValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                field.SetValueWithoutNotify(evt.previousValue);
                evt.PreventDefault();
                return;
            }

            var strInt = value.ToString();
            if (strInt != evt.newValue)
                field.SetValueWithoutNotify(strInt);
            valueCallback(value);
        });
    }


    public static void ConvertToFloatField(TextField field, Action<float> valueCallback, float defaultValue = 0f)
    {
        field.SetValueWithoutNotify(FloatToString(defaultValue));
        field.RegisterCallback<KeyDownEvent>(evt =>
        {
            if (!char.IsDigit(evt.character))
                evt.PreventDefault();
        });

        field.RegisterValueChangedCallback(evt =>
        {
            if (!float.TryParse(evt.newValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                field.SetValueWithoutNotify(evt.previousValue);
                evt.PreventDefault();
                return;
            }

            var strFloat = FloatToString(value);
            if (strFloat != evt.newValue)
                field.SetValueWithoutNotify(strFloat);
            valueCallback(value);
        });
    }

    public static string VectorToString(SerializableVector3 vec)
    {
        return $"{FloatToString(vec.x)}, {FloatToString(vec.y)}, {FloatToString(vec.z)}";
    }
}