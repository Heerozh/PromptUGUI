using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Lazily installs the press/select content-offset on a state-source control (Btn / Tab / Toggle):
    /// creates a full-stretch content-holder, sweeps the control's direct children into it, and wires a
    /// <see cref="PressOffsetController"/>. Idempotent: re-run on each Variant ReSolve, reusing the holder.
    /// Returns the holder (or null when no offset was ever authored).
    /// </summary>
    internal static class StateOffsetInstaller
    {
        internal static RectTransform Install(GameObject go, RectTransform existing, StateOffsetSet offsets)
        {
            if (!offsets.HasAny && existing == null) return null;   // never authored → no holder

            var holder = existing != null ? existing : CreateHolder(go);
            SweepDirectChildrenInto(go, holder);
            var ctrl = holder.GetComponent<PressOffsetController>()
                       ?? holder.gameObject.AddComponent<PressOffsetController>();
            ctrl.Configure(offsets);
            return holder;
        }

        // Full-stretch RectTransform (transparent to layout — same rect as the control), à la
        // Animation's _offsetProxy. The content lives under it so one anchoredPosition shift moves all.
        private static RectTransform CreateHolder(GameObject go)
        {
            var holderGo = new GameObject("_offsetHolder", typeof(RectTransform));
            var rt = (RectTransform)holderGo.transform;
            rt.SetParent(go.transform, worldPositionStays: false);
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
            return rt;
        }

        // Move every direct child of `go` (except the holder) into the holder, preserving sibling order.
        // Snapshot first: SetParent mutates the child list mid-iteration. On ReSolve the content is
        // already inside the holder → no-op; a child that first appears later (a Variant ReSolve) is
        // swept in then. The bg Image / PuiButton / CanvasGroup are components on `go`, not children.
        private static void SweepDirectChildrenInto(GameObject go, RectTransform holder)
        {
            var parent = go.transform;
            var count = parent.childCount;
            var moved = new List<Transform>(count);
            for (int i = 0; i < count; i++)
            {
                var child = parent.GetChild(i);
                if (child != holder) moved.Add(child);
            }
            foreach (var child in moved)
                child.SetParent(holder, worldPositionStays: false);
        }
    }
}
