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

    // Whole-item clipboard for multi-object copy/paste: each entry is a deep-cloned blueprint paired
    // with the live world transform it was copied at, so paste reproduces the exact relative layout
    // and orientation around the cursor (the blueprint's own position/rotation fields are stale
    // mid-edit — see PlacedItem).
    internal static class ItemClipboard
    {
        public static List<Utils.PlacedItem> items;

        public static bool HasData => items != null && items.Count > 0;
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