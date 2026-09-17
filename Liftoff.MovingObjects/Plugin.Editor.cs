using System.IO;
using BepInEx;
using HarmonyLib;
using Liftoff.MovingObjects.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Liftoff.MovingObjects;

// The track-builder half of the plugin: the editor windows, their asset bundle, and the editor's
// Harmony patches. The race-only build (Configuration=Race, MO_RACE) leaves this file out, so
// everything here must be something flying a track can do without. Plugin.cs is the flying half.
public sealed partial class Plugin
{
    private static AssetBundle _assetBundle;
    private static AnimationEditorWindow.Assets _editorAssets;
    private static PlacementUtilsWindow.Assets _placementAssets;

    // Loads the editor windows' UI from the embedded bundle. Called from Awake.
    private static void LoadEditorAssets()
    {
        try
        {
            _assetBundle = AssetBundle.LoadFromMemory(UI.LiftoffUI);
            if (_assetBundle == null)
            {
                Log.LogError("Failed to load embedded UI asset bundle");
                return;
            }

            _editorAssets = new AnimationEditorWindow.Assets
            {
                VisualTreeAsset = _assetBundle.LoadAsset<VisualTreeAsset>(
                    "Assets/Liftoff.MovingObject/AnimationEditorWindow.uxml"),
                AnimationTemplateAsset = _assetBundle.LoadAsset<VisualTreeAsset>(
                    "Assets/Liftoff.MovingObject/AnimationStepTemplate.uxml"),
                PanelSettings = _assetBundle.LoadAsset<PanelSettings>(
                    "Assets/Liftoff.MovingObject/AnimationEditorWindowPanelSettings.asset")
            };

            _placementAssets = new PlacementUtilsWindow.Assets
            {
                VisualTreeAsset = _assetBundle.LoadAsset<VisualTreeAsset>(
                    "Assets/Liftoff.MovingObject/UtilsWindow.uxml"),
                PanelSettings = _assetBundle.LoadAsset<PanelSettings>(
                    "Assets/Liftoff.MovingObject/UtilsWindowPanelSettings.asset")
            };
        }
        catch (System.Exception ex)
        {
            Log.LogError($"Asset bundle setup failed: {ex}");
        }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TrackEditorGUI), "Start")]
    private static void OnTrackEditorGuiStart(TrackEditorGUI __instance)
    {
        // Fresh editor session: clear undo history and arm the post-load suppression window so the
        // items the loaded track spawns don't seed the history.
        UndoHistory.ResetForNewSession();

        var trackMenu = ReflectionUtils.GetPrivateFieldValue<TrackEditorMenuManager>(__instance, "trackMenu");
        var trackBuilderPanel =
            ReflectionUtils.GetPrivateFieldValue<TrackEditorEditWindow>(trackMenu, "trackBuilderPanel");

        var animation = trackBuilderPanel.detailPane.gameObject.AddComponent<AnimationEditorWindow>();
        animation.assets = _editorAssets;

        trackBuilderPanel.onItemSelected += animation.OnItemSelected;
        trackBuilderPanel.onItemSelectionCleared += animation.OnItemCleared;

        var placementUtilsObj = new GameObject("MO_PlacementUtils");
        placementUtilsObj.transform.SetParent(trackBuilderPanel.gameObject.transform);

        var placementUtilsWindow = placementUtilsObj.AddComponent<PlacementUtilsWindow>();
        placementUtilsWindow.assets = _placementAssets;
    }

    // Stamp the required-mod-version onto every item that carries MovingObjects config, just before
    // the game serializes the track to disk. This is the single chokepoint the Save button funnels
    // through, so it covers all authoring paths (editor, copy/paste, array/mirror, stamp-insert). We
    // stamp the live item blueprints (the same instances the game serializes) via the existing
    // FindAllFlags/reflection path. A missing stamp is treated as "compatible" at load, so any path
    // we somehow miss under-protects rather than falsely blocking.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TrackEditorMenuManager), "SaveTrack")]
    private static void StampTrackVersionOnSave()
    {
        var version = MinCompatibleVersion.ToString();
        foreach (var flag in EditorUtils.FindAllFlags())
        {
            var blueprint = ReflectionUtils.GetPrivateFieldValueByType<TrackBlueprint>(flag);
            if (HasMoContent(blueprint))
                blueprint.mo_minModVersion = version;
        }
    }

    // Override the Workshop preview image with a local preview.png when sharing a track.
    // The current game's ShareItem gained a third parameter of an obfuscated (unnameable) type,
    // which is why the old two-type-argument patch could no longer bind. There is only one
    // ShareItem overload, so we patch it by name alone (no parameter types) — HarmonyX resolves
    // the single method without us naming the obfuscated type — and reach the Sprite preview
    // positionally via __1 (the second parameter).
    [HarmonyPrefix]
    [HarmonyPatch(typeof(PopupShareContent), "ShareItem")]
    private static void ShareItem(ref Sprite __1)
    {
        var overwritePreview = Path.Combine(Paths.GameRootPath, "preview.png");
        if (!File.Exists(overwritePreview))
        {
            Log.LogInfo($"Preview overwrite not found {overwritePreview}, skip");
            return;
        }

        var preview = new Texture2D(2, 2);
        preview.LoadImage(File.ReadAllBytes(overwritePreview));
        __1 = Sprite.Create(preview, new Rect(0, 0, preview.width, preview.height), new Vector2(0.5f, 0.5f));
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TrackEditorEditWindow), "AtLeastOneItemAvailable", typeof(TrackItemCategory))]
    private static void AtLeastOneItemAvailable(ref bool __result)
    {
        if (!__result)
            __result = true;
    }

    // Add chokepoint: every new track item — native palette placement AND the mod's own spawns
    // (ItemSpawner.SpawnFromBlueprint calls this to register the item into Track.blueprints) — passes
    // through here. Recording adds here captures both uniformly. __0 is the obfuscated item type,
    // bound positionally as the Component it derives from. Guarded: never break placement.
    [HarmonyPostfix]
    [HarmonyPatch(typeof(TrackEditor), "AssignIDToTrackItem")]
    private static void OnTrackItemAssignedId(Component __0)
    {
        try { UndoHistory.NotifyAdded(__0); }
        catch (System.Exception ex) { Log.LogWarning($"Undo add-capture failed: {ex.Message}"); }
    }

    // Remove chokepoint: the game's native erase and the mod's F9 delete both funnel through
    // TrackEditor.RemoveTrackItem (see ItemSpawner.RemoveItem). This is a PREFIX so we snapshot the
    // item before its GameObject is destroyed. Guarded: never break deletion.
    [HarmonyPrefix]
    [HarmonyPatch(typeof(TrackEditor), "RemoveTrackItem")]
    private static void OnTrackItemRemoving(Component __0)
    {
        try { UndoHistory.NotifyRemoving(__0); }
        catch (System.Exception ex) { Log.LogWarning($"Undo remove-capture failed: {ex.Message}"); }
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TrackDragCenterOnCamera), "OnDragHold")]
    [HarmonyPatch(typeof(TrackDragBehaviorSnap), "OnDragHold")]
    [HarmonyPatch(typeof(TrackDragBehaviorRibbon), "OnDragHold")]
    private static void OnDragHold(MonoBehaviour __instance)
    {
        if (Shared.PlacementUtils.DragGridRound <= 0)
            return;
        var parent = __instance.gameObject.transform.parent;
        if (parent == null)
            return;
        parent.position = GridUtils.RoundVectorToStep(parent.position, Shared.PlacementUtils.DragGridRound);
    }

    [HarmonyPostfix]
    [HarmonyPatch(typeof(TrackDragBehaviorSnap), "OnDragRelease")]
    [HarmonyPatch(typeof(TrackDragCenterOnCamera), "OnDragRelease")]
    [HarmonyPatch(typeof(TrackDragBehaviorRibbon), "OnDragRelease")]
    private static void OnDragRelease(MonoBehaviour __instance)
    {
        if (!Shared.PlacementUtils.EnchantedEditor)
            return;

        var rot = __instance.gameObject.transform.rotation.eulerAngles;
        __instance.gameObject.transform.rotation =
            Quaternion.Euler(GridUtils.SmartRound(rot));
    }
}
