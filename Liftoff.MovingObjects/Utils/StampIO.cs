using System;
using System.Collections.Generic;
using System.IO;
using System.Xml.Serialization;
using BepInEx;
using BepInEx.Logging;
using Logger = BepInEx.Logging.Logger;

namespace Liftoff.MovingObjects.Utils;

// A saved "stamp": a named set of TrackBlueprints written to disk so a selection can be reused
// across tracks (a mini prefab library). TrackBlueprint is [Serializable] and all its fields are
// simple/serializable (it's how the mo_* config already round-trips in track XML), so an
// XmlSerializer over a wrapper is enough — no bespoke format needed.
[XmlRoot("MO_Stamp")]
public class StampSet
{
    [XmlElement("item")]
    public List<TrackBlueprint> items = new();
}

internal static class StampIO
{
    private static readonly ManualLogSource Log =
        Logger.CreateLogSource($"{PluginInfo.PLUGIN_NAME}.{nameof(StampIO)}");

    // Stamps live under the game root so they're easy to find and share.
    public static string StampDir => Path.Combine(Paths.GameRootPath, "mo_stamps");

    private static string PathFor(string name)
    {
        var safe = string.IsNullOrWhiteSpace(name) ? "stamp" : name;
        return Path.Combine(StampDir, safe + ".xml");
    }

    public static void Save(string name, List<TrackBlueprint> items)
    {
        try
        {
            Directory.CreateDirectory(StampDir);
            var set = new StampSet { items = items };
            var serializer = new XmlSerializer(typeof(StampSet));
            using var writer = new StreamWriter(PathFor(name));
            serializer.Serialize(writer, set);
            Log.LogInfo($"Saved stamp '{name}' ({items.Count} items) to {PathFor(name)}");
        }
        catch (Exception e)
        {
            Log.LogError($"Failed to save stamp '{name}': {e}");
        }
    }

    public static List<TrackBlueprint> Load(string name)
    {
        try
        {
            var path = PathFor(name);
            if (!File.Exists(path))
            {
                Log.LogWarning($"Stamp not found: {path}");
                return null;
            }

            var serializer = new XmlSerializer(typeof(StampSet));
            using var reader = new StreamReader(path);
            var set = (StampSet)serializer.Deserialize(reader);
            return set?.items;
        }
        catch (Exception e)
        {
            Log.LogError($"Failed to load stamp '{name}': {e}");
            return null;
        }
    }
}
