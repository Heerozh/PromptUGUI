using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Whether a gesture a control handles itself (a drag source's lift, a Carousel's swipe) may act: the answer a
    /// <c>Selectable</c> gets from the <c>CanvasGroup</c>s around it — what <c>interactable="false"</c> on the control or on a
    /// panel above it turns off (spec 2026-11-23-input-correctness §4).
    /// </summary>
    internal static class Interaction
    {
        /// <summary>The CanvasGroups from <paramref name="t"/> (included) up to the first that ignores its parents.</summary>
        internal static bool GroupsAllow(Transform t)
        {
            for (; t != null; t = t.parent)
            {
                var g = t.GetComponent<CanvasGroup>();
                if (g == null) continue;
                if (!g.interactable) return false;
                if (g.ignoreParentGroups) break;
            }
            return true;
        }
    }
}
