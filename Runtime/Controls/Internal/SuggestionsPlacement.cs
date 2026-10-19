using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The suggestion list's height and side (spec 2026-10-18 §6.8), as a pure function: the height is
    /// <c>min(content, maxHeight, room)</c> (<c>maxHeight = 0</c>: content and room only); the side is
    /// the preferred one unless the content does not fit there and the other side is roomier — the same
    /// flip rule as <see cref="PopupPlacer"/>. While the list is open the side is locked: a push only
    /// re-clamps the height, so the panel never jumps across the field while the user types.
    /// </summary>
    internal static class SuggestionsPlacement
    {
        /// <param name="field">The field, in the bounds' space.</param>
        /// <param name="bounds">Where the panel may go.</param>
        /// <param name="contentHeight">The rows' preferred height, padding included.</param>
        /// <param name="maxHeight">The cap; 0 = none.</param>
        /// <param name="gap">Space between the field and the panel.</param>
        /// <param name="above">The preferred side, or with <paramref name="locked"/> the side to keep.</param>
        /// <param name="locked">Keep <paramref name="above"/> whatever the room.</param>
        internal static (float Height, bool Above) Solve(Rect field, Rect bounds, float contentHeight,
                                                         float maxHeight, float gap, bool above, bool locked)
        {
            var roomBelow = Mathf.Max(0f, field.yMin - bounds.yMin - gap);
            var roomAbove = Mathf.Max(0f, bounds.yMax - field.yMax - gap);
            var capped = maxHeight > 0f ? Mathf.Min(contentHeight, maxHeight) : contentHeight;

            if (!locked)
            {
                var preferred = above ? roomAbove : roomBelow;
                var other = above ? roomBelow : roomAbove;
                if (capped > preferred && other > preferred) above = !above;
            }
            return (Mathf.Max(0f, Mathf.Min(capped, above ? roomAbove : roomBelow)), above);
        }
    }
}
