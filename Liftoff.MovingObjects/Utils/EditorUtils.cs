using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Liftoff.MovingObjects.Utils;

internal class EditorUtils
{

    private static readonly Type[] TrackItemTypes = new[]
    {
        typeof(TrackItemFlag),
        typeof(TrackItemKillDroneTrigger),
        typeof(TrackItemShowTextTrigger),
        typeof(TrackItemPlaySoundTrigger),
        typeof(TrackItemRepairPropellersTrigger),
        typeof(TrackItemChargeBatteryTrigger),
        typeof(TrackItemFlexibleCheckpointTrigger),
    };

    public static List<Component> FindAllFlags()
    {
        var flags = new List<Component>();
        foreach (var type in TrackItemTypes)
            flags.AddRange(Object.FindObjectsOfType(type).OfType<Component>());
        return flags;
    }

    public static Component FindFlagInParent(GameObject parent)
    {
        return TrackItemTypes.Select(parent.GetComponentInParent).FirstOrDefault(component => component != null);
    }

    public static List<Component> FindFlagsByGroupId(string groupId)
    {
        var flags = new List<Component>();
        foreach (var flag in FindAllFlags())
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);
            if (string.Equals(blueprint?.mo_groupId, groupId))
                flags.Add(flag);
        }
        return flags;
    }

    // Lint the map's triggers/portals for the mistakes that silently break a map at flight time:
    // a Target with no matching Name marker, and teleport-with-no-target. Duplicate names are
    // reported as info since they're legitimately used for multi-exit portals / multi-triggers.
    public static List<string> ValidateTriggers()
    {
        var warnings = new List<string>();
        var blueprints = FindAllFlags()
            .Select(ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>)
            .Where(b => b != null)
            .ToList();

        var nameCounts = new Dictionary<string, int>();
        foreach (var blueprint in blueprints)
        {
            var name = blueprint.mo_triggerOptions?.triggerName;
            if (!string.IsNullOrEmpty(name))
                nameCounts[name] = nameCounts.TryGetValue(name, out var count) ? count + 1 : 1;
        }

        foreach (var blueprint in blueprints)
        {
            var trigger = blueprint.mo_triggerOptions;
            if (trigger == null)
                continue;

            var target = trigger.triggerTarget;
            if (!string.IsNullOrEmpty(target) && !nameCounts.ContainsKey(target))
                warnings.Add($"Dangling target '{target}' on {blueprint.itemID}: no object has that trigger Name.");
            if (trigger.triggerTeleport && string.IsNullOrEmpty(target))
                warnings.Add($"Teleport enabled with no target on {blueprint.itemID}.");
        }

        foreach (var pair in nameCounts)
            if (pair.Value > 1)
                warnings.Add($"Info: name '{pair.Key}' used by {pair.Value} objects (ok for multi-exit / multi-trigger).");

        if (warnings.Count == 0)
            warnings.Add("No trigger issues found.");

        return warnings;
    }
}