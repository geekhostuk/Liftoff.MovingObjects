using System.Collections.Generic;
using UnityEngine;

namespace Liftoff.MovingObjects;

// Editor overlay: draws a line from each trigger entrance to every exit marker sharing its target
// name, plus a short arrow along the exit's facing — so portal links are legible in-editor instead
// of only discoverable by flying the map. Uses real LineRenderers (Debug.DrawLine won't show in the
// game view), mirroring PathPreview.
internal class TriggerLinkPreview : MonoBehaviour
{
    private const float ArrowLength = 1f;
    private const float LineWidth = 0.05f;

    private readonly List<LineRenderer> _lines = new();

    public struct Link
    {
        public Vector3 From;
        public Vector3 To;
        public Vector3 Forward;
    }

    public void SetLinks(List<Link> links)
    {
        Clear();
        foreach (var link in links)
        {
            var line = CreateLine(Color.green);
            line.SetPosition(0, link.From);
            line.SetPosition(1, link.To);

            var arrow = CreateLine(Color.red);
            arrow.SetPosition(0, link.To);
            arrow.SetPosition(1, link.To + link.Forward.normalized * ArrowLength);
        }
    }

    private LineRenderer CreateLine(Color color)
    {
        var obj = new GameObject("TriggerLink");
        obj.transform.SetParent(transform, false);

        var line = obj.AddComponent<LineRenderer>();
        var shader = Shader.Find("Sprites/Default");
        if (shader != null)
            line.material = new Material(shader);
        line.startColor = line.endColor = color;
        line.widthMultiplier = LineWidth;
        line.positionCount = 2;
        line.useWorldSpace = true;

        _lines.Add(line);
        return line;
    }

    private void Clear()
    {
        foreach (var line in _lines)
            if (line != null)
                Destroy(line.gameObject);
        _lines.Clear();
    }

    private void OnDestroy()
    {
        Clear();
    }
}
