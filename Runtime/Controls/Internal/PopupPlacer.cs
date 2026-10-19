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

        // ── Four sides (spec 2026-10-19 §4.5 / §5.3) ──────────────────────────────────────

        /// <summary>
        /// Where a panel goes beside <paramref name="anchor"/>, as a rect in the same space: on the
        /// preferred side unless that side cannot hold it and the opposite one is roomier (the rule of
        /// <see cref="Solve"/>, on both axes), aligned on the cross axis, then pulled back inside the
        /// bounds. When it fits on neither side it is pushed back along the main axis too and may cover
        /// the anchor; larger than the bounds, its start edge (left / top, where reading starts) stays in.
        /// </summary>
        /// <param name="locked">The side chosen when the panel opened: kept, only re-clamped (spec SG-D12).</param>
        public static SidePlacement SolveSide(Rect anchor, Vector2 panel, Rect bounds, float gap,
                                              PopupSide prefer, PopupAlign align, PopupSide? locked = null)
        {
            PopupSide side;
            if (locked.HasValue)
            {
                side = locked.Value;
            }
            else
            {
                var vertical = prefer == PopupSide.Top || prefer == PopupSide.Bottom;
                var needed = (vertical ? panel.y : panel.x) + gap;
                var other = Opposite(prefer);
                var roomPreferred = RoomOn(prefer, anchor, bounds);
                var roomOther = RoomOn(other, anchor, bounds);
                side = needed > roomPreferred && roomOther > roomPreferred ? other : prefer;
            }

            float xMin, yMin;
            switch (side)
            {
                case PopupSide.Top:
                    yMin = anchor.yMax + gap;
                    xMin = AlignStart(anchor.xMin, anchor.xMax, panel.x, align, startIsMax: false);
                    break;
                case PopupSide.Left:
                    xMin = anchor.xMin - gap - panel.x;
                    yMin = AlignStart(anchor.yMin, anchor.yMax, panel.y, align, startIsMax: true);
                    break;
                case PopupSide.Right:
                    xMin = anchor.xMax + gap;
                    yMin = AlignStart(anchor.yMin, anchor.yMax, panel.y, align, startIsMax: true);
                    break;
                default:
                    yMin = anchor.yMin - gap - panel.y;
                    xMin = AlignStart(anchor.xMin, anchor.xMax, panel.x, align, startIsMax: false);
                    break;
            }

            // Both axes back inside — the cross axis always, the main axis only bites when neither side
            // held the panel (or the anchor itself sits outside the bounds).
            xMin = ClampInside(xMin, panel.x, bounds.xMin, bounds.xMax, startIsMax: false);
            yMin = ClampInside(yMin, panel.y, bounds.yMin, bounds.yMax, startIsMax: true);

            var room = Mathf.Max(0f, RoomOn(side, anchor, bounds) - gap);
            return new SidePlacement(new Rect(xMin, yMin, panel.x, panel.y), side, room);
        }

        /// <summary>
        /// The panel's pivot for a side and an alignment: the edge facing the anchor stays put, so a panel
        /// that grows while open grows away from it.
        /// </summary>
        public static Vector2 PivotFor(PopupSide side, PopupAlign align)
        {
            var cross = align == PopupAlign.Start ? 0f : align == PopupAlign.Center ? 0.5f : 1f;
            switch (side)
            {
                case PopupSide.Top: return new Vector2(cross, 0f);
                case PopupSide.Left: return new Vector2(1f, 1f - cross);
                case PopupSide.Right: return new Vector2(0f, 1f - cross);
                default: return new Vector2(cross, 1f);
            }
        }

        /// <summary><c>top|bottom|left|right</c>, optionally <c>-start|-center|-end</c> (start when omitted).</summary>
        public static (PopupSide side, PopupAlign align) ParsePlacement(string value)
        {
            var v = (value ?? string.Empty).Trim();
            var dash = v.IndexOf('-');
            var sidePart = dash < 0 ? v : v.Substring(0, dash);
            var alignPart = dash < 0 ? "start" : v.Substring(dash + 1);

            PopupSide side;
            switch (sidePart)
            {
                case "top": side = PopupSide.Top; break;
                case "bottom": side = PopupSide.Bottom; break;
                case "left": side = PopupSide.Left; break;
                case "right": side = PopupSide.Right; break;
                default: throw BadPlacement(value);
            }
            PopupAlign align;
            switch (alignPart)
            {
                case "start": align = PopupAlign.Start; break;
                case "center": align = PopupAlign.Center; break;
                case "end": align = PopupAlign.End; break;
                default: throw BadPlacement(value);
            }
            return (side, align);
        }

        private static System.ArgumentException BadPlacement(string value)
            => new System.ArgumentException(
                $"placement='{value}' invalid; expected top|bottom|left|right, optionally followed by " +
                "-start|-center|-end (e.g. 'top-end')");

        private static PopupSide Opposite(PopupSide side)
        {
            switch (side)
            {
                case PopupSide.Top: return PopupSide.Bottom;
                case PopupSide.Left: return PopupSide.Right;
                case PopupSide.Right: return PopupSide.Left;
                default: return PopupSide.Top;
            }
        }

        private static float RoomOn(PopupSide side, Rect anchor, Rect bounds)
        {
            switch (side)
            {
                case PopupSide.Top: return bounds.yMax - anchor.yMax;
                case PopupSide.Left: return anchor.xMin - bounds.xMin;
                case PopupSide.Right: return bounds.xMax - anchor.xMax;
                default: return anchor.yMin - bounds.yMin;
            }
        }

        // The panel's min coordinate on the cross axis. "Start" is the left edge horizontally and the TOP
        // edge vertically (startIsMax), the way a reader scans.
        private static float AlignStart(float aMin, float aMax, float size, PopupAlign align, bool startIsMax)
        {
            switch (align)
            {
                case PopupAlign.Center: return (aMin + aMax) * 0.5f - size * 0.5f;
                case PopupAlign.End: return startIsMax ? aMin : aMax - size;
                default: return startIsMax ? aMax - size : aMin;
            }
        }

        private static float ClampInside(float min, float size, float bMin, float bMax, bool startIsMax)
        {
            if (size > bMax - bMin) return startIsMax ? bMax - size : bMin;
            if (min < bMin) return bMin;
            if (min + size > bMax) return bMax - size;
            return min;
        }
    }

    /// <summary>Which side of its anchor a panel sits on.</summary>
    internal enum PopupSide { Top, Bottom, Left, Right }

    /// <summary>How a panel lines up with its anchor across the side: left / top edges, centres, right / bottom edges.</summary>
    internal enum PopupAlign { Start, Center, End }

    /// <summary>A panel rect beside its anchor, in the anchor's space, and the side it ended up on.</summary>
    internal readonly struct SidePlacement
    {
        public readonly Rect Panel;
        public readonly PopupSide Side;
        public readonly float Room;   // space left on the chosen side after the gap; never negative

        public SidePlacement(Rect panel, PopupSide side, float room)
        {
            Panel = panel;
            Side = side;
            Room = room;
        }
    }
}
