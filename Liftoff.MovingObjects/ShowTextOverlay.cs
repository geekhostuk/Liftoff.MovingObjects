using UnityEngine;

namespace Liftoff.MovingObjects;

// Mod-owned on-screen message for Show-Text triggers. Liftoff's native show-text display auto-hides
// after ~1s with no exposed duration knob, so when an author sets a custom display time we render the
// message ourselves for exactly that long instead (see the OnDroneEnter patch in Plugin). Uses IMGUI
// (OnGUI) deliberately: it needs no asset bundle / PanelSettings and gives full control over timing,
// size and placement. A single instance persists across scenes and is reused for every message.
internal sealed class ShowTextOverlay : MonoBehaviour
{
    private static ShowTextOverlay _instance;

    private string _text;
    private float _hideAtUnscaledTime;
    private GUIStyle _style;

    public static ShowTextOverlay Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("MO_ShowTextOverlay");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<ShowTextOverlay>();
            }

            return _instance;
        }
    }

    // Show text for the given number of seconds (unscaled, so it isn't affected by any time-scale
    // change). Calling again replaces the current message and restarts the timer.
    public void Show(string text, float seconds)
    {
        _text = text;
        _hideAtUnscaledTime = Time.unscaledTime + Mathf.Max(0.1f, seconds);
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(_text) || Time.unscaledTime >= _hideAtUnscaledTime)
            return;

        _style ??= new GUIStyle(GUI.skin.label)
        {
            alignment = TextAnchor.MiddleCenter,
            wordWrap = true,
            fontStyle = FontStyle.Bold
        };
        // Scale with resolution so the message reads the same on any screen.
        _style.fontSize = Mathf.RoundToInt(Screen.height * 0.045f);

        var width = Screen.width * 0.8f;
        var height = Screen.height * 0.2f;
        var rect = new Rect((Screen.width - width) / 2f, Screen.height * 0.12f, width, height);

        // Drop shadow first so the text stays legible over bright scenery.
        var previous = GUI.color;
        var shadow = new Rect(rect.x + 2f, rect.y + 2f, rect.width, rect.height);
        GUI.color = new Color(0f, 0f, 0f, 0.75f);
        GUI.Label(shadow, _text, _style);
        GUI.color = Color.white;
        GUI.Label(rect, _text, _style);
        GUI.color = previous;
    }
}
