using System;
using System.Collections.Generic;
using UnityEngine;

namespace Liftoff.MovingObjects;

internal static class Shared
{
    internal static class PlacementUtils
    {
        public static float GridRound { get; set; } = 0.5f;
        public static float DragGridRound { get; set; } = 0.0f;
        public static bool EnchantedEditor { get; set; }
    }

    // Holds a deep copy of an object's MO configuration (options + steps + trigger) so it can be
    // stamped onto other objects — the fix for symmetric/repetitive tracks (author one moving gate,
    // paste onto twenty). Populated by the animation editor's Copy button.
    internal static class Clipboard
    {
        public static MO_AnimationOptions animationOptions;
        public static List<MO_Animation> animationSteps;
        public static MO_TriggerOptions triggerOptions;

        public static bool HasData => animationOptions != null || triggerOptions != null;
    }

    // Whole-item clipboard for multi-object copy/paste: deep-cloned blueprints plus the centroid
    // they were copied around, so paste can land them relative to the cursor.
    internal static class ItemClipboard
    {
        public static List<TrackBlueprint> blueprints;
        public static UnityEngine.Vector3 centroid;

        public static bool HasData => blueprints != null && blueprints.Count > 0;
    }

    internal static class Editor
    {
        public static event Action<ItemInfo> OnItemSelected;
        public static event Action OnItemCleared;
        public static event Action OnRefreshGuiRequest;

        public static void ItemSelected(ItemInfo info)
        {
            OnItemSelected?.Invoke(info);
        }

        public static void ItemCleared()
        {
            OnItemCleared?.Invoke();
        }
        public static void RequestRefreshGui()
        {
            OnRefreshGuiRequest?.Invoke();
        }

        public class ItemInfo
        {
            public TrackBlueprint blueprint;
            public GameObject gameObject;
        }
    }
}