using System.Collections.Generic;
using UnityEngine;

namespace Liftoff.MovingObjects;

// Editor overlay: draws the animation trajectory (a polyline through the step positions) plus a
// short forward tick at each step, so mappers see the path and facing without pressing Play.
// Debug.DrawLine doesn't render in the game view, so this uses real LineRenderers.
internal class PathPreview : MonoBehaviour
{
    private const float TickLength = 0.5f;
    private const float LineWidth = 0.05f;

    private readonly List<LineRenderer> _lines = new();

    public void SetPath(List<Vector3> positions, List<Quaternion> rotations)
    {
        Clear();
        if (positions == null || positions.Count < 1)
            return;

        var path = CreateLine(Color.cyan, positions.Count);
        for (var i = 0; i < positions.Count; i++)
            path.SetPosition(i, positions[i]);

        for (var i = 0; i < positions.Count; i++)
        {
            var tick = CreateLine(Color.yellow, 2);
            tick.SetPosition(0, positions[i]);
            tick.SetPosition(1, positions[i] + rotations[i] * Vector3.forward * TickLength);
        }
    }

    private LineRenderer CreateLine(Color color, int count)
    {
        var obj = new GameObject("PathLine");
        obj.transform.SetParent(transform, false);

        var line = obj.AddComponent<LineRenderer>();
        var shader = Shader.Find("Sprites/Default");
        if (shader != null)
            line.material = new Material(shader);
        line.startColor = line.endColor = color;
        line.widthMultiplier = LineWidth;
        line.positionCount = count;
        line.useWorldSpace = true;
        line.numCapVertices = 2;

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
