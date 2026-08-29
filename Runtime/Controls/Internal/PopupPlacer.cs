using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>Where a dropped panel ends up, relative to the handle it hangs from.</summary>
    internal readonly struct PopupPlacement
    {
        public readonly Vector2 Anchor;            // both anchorMin and anchorMax — a corner of the handle
        public readonly Vector2 Pivot;
        public readonly Vector2 AnchoredPosition;
        public readonly bool FlippedUp;

        public PopupPlacement(Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, bool flippedUp)
        {
            Anchor = anchor;
            Pivot = pivot;
            AnchoredPosition = anchoredPosition;
            FlippedUp = flippedUp;
        }
    }

    /// <summary>
    /// Solves where a <see cref="TabMenu"/>'s popup panel sits: under the handle and left-aligned
    /// with it by default, flipped above when the room below cannot hold it, and pulled left when it
    /// would spill past the right edge (spec §7.2).
    ///
    /// <para>Deliberately pure — every input is a plain rect, so the rules are unit-testable without
    /// a live Canvas, which EditMode cannot size deterministically.</para>
    /// </summary>
    internal static class PopupPlacer
    {
        /// <param name="handle">The collapsed handle's rect, in the root canvas's local space.</param>
        /// <param name="panel">The panel's width and height.</param>
        /// <param name="canvas">The root canvas's rect, same space as <paramref name="handle"/>.</param>
        /// <param name="gap">Vertical space left between handle and panel.</param>
        public static PopupPlacement Solve(Rect handle, Vector2 panel, Rect canvas, float gap)
        {
            var roomBelow = handle.yMin - canvas.yMin;
            var roomAbove = canvas.yMax - handle.yMax;
            var needed = panel.y + gap;

            // Flip only when it actually helps: a panel taller than both sides stays put (down is
            // the convention) rather than jumping upward for no gain.
            var flipUp = needed > roomBelow && roomAbove > roomBelow;

            // Left-aligned with the handle, then pulled back inside the right edge if it spills —
            // and never pushed past the left edge in the process.
            var dx = 0f;
            var overflowRight = handle.xMin + panel.x - canvas.xMax;
            if (overflowRight > 0f) dx = -overflowRight;
            var leftLimit = canvas.xMin - handle.xMin;
            if (dx < leftLimit) dx = leftLimit;

            return flipUp
                ? new PopupPlacement(new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(dx, gap), true)
                : new PopupPlacement(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(dx, -gap), false);
        }
    }
}
