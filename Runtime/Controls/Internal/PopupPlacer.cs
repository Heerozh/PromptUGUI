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
        public readonly float Room;                // space left on the chosen side after the gap; never negative

        public PopupPlacement(Vector2 anchor, Vector2 pivot, Vector2 anchoredPosition, bool flippedUp,
                              float room = 0f)
        {
            Anchor = anchor;
            Pivot = pivot;
            AnchoredPosition = anchoredPosition;
            FlippedUp = flippedUp;
            Room = room;
        }
    }

    /// <summary>
    /// Solves where a popup panel sits against the handle it hangs from: on the preferred side
    /// (below by default) and left-aligned with it, on the other side when the preferred one cannot
    /// hold it, and pulled left when it would spill past the right edge (TabMenu spec §7.2; the
    /// preferred side and <see cref="PopupPlacement.Room"/> are spec 2026-10-18 §4.6).
    ///
    /// <para>Deliberately pure — every input is a plain rect, so the rules are unit-testable without
    /// a live Canvas, which EditMode cannot size deterministically.</para>
    /// </summary>
    internal static class PopupPlacer
    {
        /// <param name="handle">The handle's rect, in the root canvas's local space.</param>
        /// <param name="panel">The panel's width and height.</param>
        /// <param name="bounds">
        /// Where the panel may go, same space as <paramref name="handle"/>: the root canvas's rect, or
        /// a smaller area inside it (the safe area, minus a soft keyboard).
        /// </param>
        /// <param name="gap">Vertical space left between handle and panel.</param>
        /// <param name="preferAbove">Open upward unless that side cannot hold the panel.</param>
        public static PopupPlacement Solve(Rect handle, Vector2 panel, Rect bounds, float gap, bool preferAbove = false)
        {
            var roomBelow = handle.yMin - bounds.yMin;
            var roomAbove = bounds.yMax - handle.yMax;
            var needed = panel.y + gap;

            // Flip only when it actually helps: the preferred side wins unless the panel does not fit
            // there AND the other side is roomier. A panel taller than both sides therefore still moves
            // to whichever side holds more of it, and stays put when that is the preferred one.
            var roomPreferred = preferAbove ? roomAbove : roomBelow;
            var roomOther = preferAbove ? roomBelow : roomAbove;
            var flip = needed > roomPreferred && roomOther > roomPreferred;
            var above = preferAbove != flip;

            // Left-aligned with the handle, then pulled back inside the right edge if it spills —
            // and never pushed past the left edge in the process.
            var dx = 0f;
            var overflowRight = handle.xMin + panel.x - bounds.xMax;
            if (overflowRight > 0f) dx = -overflowRight;
            var leftLimit = bounds.xMin - handle.xMin;
            if (dx < leftLimit) dx = leftLimit;

            // What a host may clamp its panel's height to so it ends inside the bounds.
            var room = Mathf.Max(0f, (above ? roomAbove : roomBelow) - gap);

            return above
                ? new PopupPlacement(new Vector2(0f, 1f), new Vector2(0f, 0f), new Vector2(dx, gap), true, room)
                : new PopupPlacement(new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(dx, -gap), false, room);
        }
    }
}
