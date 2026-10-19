using System;
using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <see cref="PopupPlacer.SolveSide"/>, the four-sided placement a <c>&lt;Popover&gt;</c> uses (spec
    /// 2026-10-19 §4.5 / §5.3): a preferred side that flips only when that helps, an alignment on the
    /// cross axis, and a panel pulled back inside the bounds — start edge first. Pure, like
    /// <see cref="PopupPlacerTests"/>: every input is a plain rect.
    /// </summary>
    public class PopupPlacerSideTests
    {
        // A 800x600 area centred on the origin, a 40x40 anchor in the middle.
        private static readonly Rect Bounds = new Rect(-400f, -300f, 800f, 600f);
        private static readonly Rect Mid = new Rect(-20f, -20f, 40f, 40f);
        private static readonly Vector2 Panel = new Vector2(200f, 100f);

        private static SidePlacement Solve(Rect anchor, Vector2 panel, PopupSide side,
                                           PopupAlign align = PopupAlign.Start, PopupSide? locked = null)
            => PopupPlacer.SolveSide(anchor, panel, Bounds, 4f, side, align, locked);

        [Test]
        public void Each_side_puts_the_panel_beside_the_anchor_with_the_gap()
        {
            var below = Solve(Mid, Panel, PopupSide.Bottom);
            Assert.AreEqual(PopupSide.Bottom, below.Side);
            Assert.AreEqual(-24f, below.Panel.yMax, 1e-4f, "4 under the anchor's bottom edge");

            var above = Solve(Mid, Panel, PopupSide.Top);
            Assert.AreEqual(PopupSide.Top, above.Side);
            Assert.AreEqual(24f, above.Panel.yMin, 1e-4f, "4 over the anchor's top edge");

            var left = Solve(Mid, Panel, PopupSide.Left);
            Assert.AreEqual(PopupSide.Left, left.Side);
            Assert.AreEqual(-24f, left.Panel.xMax, 1e-4f, "4 left of the anchor");

            var right = Solve(Mid, Panel, PopupSide.Right);
            Assert.AreEqual(PopupSide.Right, right.Side);
            Assert.AreEqual(24f, right.Panel.xMin, 1e-4f, "4 right of the anchor");

            Assert.AreEqual(Panel, below.Panel.size, "the panel keeps its size");
        }

        [Test]
        public void Start_center_end_align_the_cross_axis()
        {
            // Above / below: left edges, centres, right edges.
            Assert.AreEqual(-20f, Solve(Mid, Panel, PopupSide.Bottom, PopupAlign.Start).Panel.xMin, 1e-4f);
            Assert.AreEqual(0f, Solve(Mid, Panel, PopupSide.Bottom, PopupAlign.Center).Panel.center.x, 1e-4f);
            Assert.AreEqual(20f, Solve(Mid, Panel, PopupSide.Top, PopupAlign.End).Panel.xMax, 1e-4f);

            // Beside: top edges, centres, bottom edges.
            Assert.AreEqual(20f, Solve(Mid, Panel, PopupSide.Right, PopupAlign.Start).Panel.yMax, 1e-4f);
            Assert.AreEqual(0f, Solve(Mid, Panel, PopupSide.Right, PopupAlign.Center).Panel.center.y, 1e-4f);
            Assert.AreEqual(-20f, Solve(Mid, Panel, PopupSide.Left, PopupAlign.End).Panel.yMin, 1e-4f);
        }

        [Test]
        public void Flips_only_when_the_preferred_side_cannot_hold_it_and_the_other_is_roomier()
        {
            // Anchor near the bottom: 30 below, 530 above.
            var low = new Rect(-20f, -270f, 40f, 40f);
            Assert.AreEqual(PopupSide.Top, Solve(low, Panel, PopupSide.Bottom).Side);
            // Anchor near the top: prefer above, flips below.
            var high = new Rect(-20f, 230f, 40f, 40f);
            Assert.AreEqual(PopupSide.Bottom, Solve(high, Panel, PopupSide.Top).Side);
            // Anchor near the right edge: prefer right, flips left.
            var east = new Rect(340f, -20f, 40f, 40f);
            Assert.AreEqual(PopupSide.Left, Solve(east, Panel, PopupSide.Right).Side);
            // Near the left edge: prefer left, flips right.
            var west = new Rect(-380f, -20f, 40f, 40f);
            Assert.AreEqual(PopupSide.Right, Solve(west, Panel, PopupSide.Left).Side);

            // Does not fit either way, and the preferred side is the roomier one: it stays.
            var tall = new Vector2(200f, 900f);
            Assert.AreEqual(PopupSide.Bottom, Solve(new Rect(-20f, 0f, 40f, 40f), tall, PopupSide.Bottom).Side,
                "300 below vs 260 above: nothing to gain by flipping");
        }

        [Test]
        public void Cross_axis_is_pulled_back_inside_and_the_start_edge_wins()
        {
            // End-aligned to an anchor at the left edge would spill left: pulled right.
            var westAnchor = new Rect(-400f, -20f, 40f, 40f);
            Assert.AreEqual(-400f, Solve(westAnchor, Panel, PopupSide.Bottom, PopupAlign.End).Panel.xMin, 1e-4f);
            // Start-aligned to an anchor at the right edge would spill right: pulled left.
            var eastAnchor = new Rect(360f, -20f, 40f, 40f);
            Assert.AreEqual(400f, Solve(eastAnchor, Panel, PopupSide.Bottom).Panel.xMax, 1e-4f);
            // Wider than the bounds: the left edge stays inside.
            Assert.AreEqual(-400f, Solve(Mid, new Vector2(1000f, 100f), PopupSide.Bottom).Panel.xMin, 1e-4f);
            // Beside: taller than the bounds keeps the top edge inside.
            Assert.AreEqual(300f, Solve(Mid, new Vector2(100f, 900f), PopupSide.Right).Panel.yMax, 1e-4f);
        }

        [Test]
        public void Neither_side_fits_pushes_it_back_inside_along_the_main_axis()
        {
            // 300 below, 260 above, 400 tall: stays below (roomier) and is pushed up inside — over the anchor.
            var p = Solve(new Rect(-20f, 0f, 40f, 40f), new Vector2(200f, 400f), PopupSide.Bottom);
            Assert.AreEqual(PopupSide.Bottom, p.Side);
            Assert.AreEqual(-300f, p.Panel.yMin, 1e-4f, "bottom edge back on the bounds");
            Assert.Greater(p.Panel.yMax, 0f, "it now covers part of the anchor");
        }

        [Test]
        public void Larger_than_the_bounds_keeps_the_start_edge_inside()
        {
            var p = Solve(Mid, new Vector2(200f, 900f), PopupSide.Bottom);
            Assert.AreEqual(300f, p.Panel.yMax, 1e-4f, "the top edge, where reading starts, stays visible");
            var q = Solve(Mid, new Vector2(1000f, 100f), PopupSide.Right);
            Assert.AreEqual(-400f, q.Panel.xMin, 1e-4f);
        }

        [Test]
        public void A_locked_side_never_flips()
        {
            var low = new Rect(-20f, -270f, 40f, 40f);
            var p = Solve(low, Panel, PopupSide.Bottom, locked: PopupSide.Bottom);
            Assert.AreEqual(PopupSide.Bottom, p.Side);
            Assert.AreEqual(-300f, p.Panel.yMin, 1e-4f, "re-clamped on its side instead of flipping");
        }

        [Test]
        public void A_zero_size_anchor_works_like_a_point()
        {
            var p = Solve(new Rect(10f, 10f, 0f, 0f), Panel, PopupSide.Bottom);
            Assert.AreEqual(10f, p.Panel.xMin, 1e-4f, "top-left corner just off the pointer");
            Assert.AreEqual(6f, p.Panel.yMax, 1e-4f);
        }

        [Test]
        public void Room_is_the_space_on_the_chosen_side_minus_the_gap()
        {
            Assert.AreEqual(276f, Solve(Mid, Panel, PopupSide.Bottom).Room, 1e-4f);
            Assert.AreEqual(376f, Solve(Mid, Panel, PopupSide.Right).Room, 1e-4f);
        }

        [Test]
        public void ParsePlacement_reads_side_and_alignment()
        {
            Assert.AreEqual((PopupSide.Bottom, PopupAlign.Start), PopupPlacer.ParsePlacement("bottom"));
            Assert.AreEqual((PopupSide.Top, PopupAlign.End), PopupPlacer.ParsePlacement("top-end"));
            Assert.AreEqual((PopupSide.Left, PopupAlign.Center), PopupPlacer.ParsePlacement("left-center"));
            Assert.AreEqual((PopupSide.Right, PopupAlign.Start), PopupPlacer.ParsePlacement(" right-start "));
            Assert.Throws<ArgumentException>(() => PopupPlacer.ParsePlacement("up"));
            Assert.Throws<ArgumentException>(() => PopupPlacer.ParsePlacement("top-middle"));
            Assert.Throws<ArgumentException>(() => PopupPlacer.ParsePlacement(""));
        }

        [Test]
        public void Pivot_follows_the_side_and_the_alignment()
        {
            Assert.AreEqual(new Vector2(0f, 1f), PopupPlacer.PivotFor(PopupSide.Bottom, PopupAlign.Start));
            Assert.AreEqual(new Vector2(1f, 0f), PopupPlacer.PivotFor(PopupSide.Top, PopupAlign.End));
            Assert.AreEqual(new Vector2(1f, 0.5f), PopupPlacer.PivotFor(PopupSide.Left, PopupAlign.Center));
            Assert.AreEqual(new Vector2(0f, 1f), PopupPlacer.PivotFor(PopupSide.Right, PopupAlign.Start));
        }

        // ── over: on top of the anchor (spec §17) ──────────────────────────────────────────────

        [Test]
        public void Over_centres_vertically_and_aligns_horizontally()
        {
            var start = Solve(Mid, Panel, PopupSide.Over, PopupAlign.Start);
            Assert.AreEqual(PopupSide.Over, start.Side);
            Assert.AreEqual(0f, start.Panel.center.y, 1e-4f, "centred on the anchor's middle");
            Assert.AreEqual(-20f, start.Panel.xMin, 1e-4f, "left edges");

            Assert.AreEqual(0f, Solve(Mid, Panel, PopupSide.Over, PopupAlign.Center).Panel.center.x, 1e-4f);

            var end = Solve(Mid, Panel, PopupSide.Over, PopupAlign.End);
            Assert.AreEqual(20f, end.Panel.xMax, 1e-4f, "right edges: the wider panel reaches out to the left");
            Assert.AreEqual(0f, end.Panel.center.y, 1e-4f);
            Assert.AreEqual(Panel, end.Panel.size, "the panel keeps its size");
        }

        [Test]
        public void Over_ignores_the_gap()
        {
            var p = PopupPlacer.SolveSide(Mid, Panel, Bounds, 30f, PopupSide.Over, PopupAlign.End);
            Assert.AreEqual(20f, p.Panel.xMax, 1e-4f);
            Assert.AreEqual(0f, p.Panel.center.y, 1e-4f);
        }

        [Test]
        public void Over_never_flips_and_is_pushed_back_inside()
        {
            // On the bottom edge: centred, half the panel would hang out below — pushed up, not flipped.
            var low = new Rect(-20f, -300f, 40f, 40f);
            var p = Solve(low, Panel, PopupSide.Over);
            Assert.AreEqual(PopupSide.Over, p.Side);
            Assert.AreEqual(-300f, p.Panel.yMin, 1e-4f);
            // On the top edge: pushed down.
            var high = new Rect(-20f, 260f, 40f, 40f);
            Assert.AreEqual(300f, Solve(high, Panel, PopupSide.Over).Panel.yMax, 1e-4f);
            // End-aligned on the left edge would spill left: pulled right.
            var west = new Rect(-400f, -20f, 40f, 40f);
            Assert.AreEqual(-400f, Solve(west, Panel, PopupSide.Over, PopupAlign.End).Panel.xMin, 1e-4f);
            // Start-aligned on the right edge: pulled left.
            var east = new Rect(360f, -20f, 40f, 40f);
            Assert.AreEqual(400f, Solve(east, Panel, PopupSide.Over).Panel.xMax, 1e-4f);
        }

        [Test]
        public void Over_larger_than_the_bounds_keeps_the_start_edges_inside()
        {
            var p = Solve(Mid, new Vector2(1000f, 900f), PopupSide.Over, PopupAlign.End);
            Assert.AreEqual(-400f, p.Panel.xMin, 1e-4f, "left edge");
            Assert.AreEqual(300f, p.Panel.yMax, 1e-4f, "top edge");
        }

        [Test]
        public void A_locked_over_stays_over()
        {
            // The preference turned into a side while open (a variant): the lock still holds.
            var p = Solve(Mid, Panel, PopupSide.Bottom, PopupAlign.Start, locked: PopupSide.Over);
            Assert.AreEqual(PopupSide.Over, p.Side);
            Assert.AreEqual(0f, p.Panel.center.y, 1e-4f);
            // And the other way round: a side locked at open stays a side.
            Assert.AreEqual(PopupSide.Bottom, Solve(Mid, Panel, PopupSide.Over, locked: PopupSide.Bottom).Side);
        }

        [Test]
        public void Over_a_point_centres_on_it_and_has_no_room()
        {
            var p = Solve(new Rect(10f, 10f, 0f, 0f), Panel, PopupSide.Over);
            Assert.AreEqual(10f, p.Panel.xMin, 1e-4f, "start: the left edge on the point");
            Assert.AreEqual(10f, p.Panel.center.y, 1e-4f);
            Assert.AreEqual(0f, p.Room, 1e-4f, "no side, no room");
        }

        [Test]
        public void ParsePlacement_reads_over()
        {
            Assert.AreEqual((PopupSide.Over, PopupAlign.Start), PopupPlacer.ParsePlacement("over"));
            Assert.AreEqual((PopupSide.Over, PopupAlign.End), PopupPlacer.ParsePlacement("over-end"));
            Assert.AreEqual((PopupSide.Over, PopupAlign.Center), PopupPlacer.ParsePlacement(" over-center "));
            Assert.Throws<ArgumentException>(() => PopupPlacer.ParsePlacement("over-top"));
            Assert.Throws<ArgumentException>(() => PopupPlacer.ParsePlacement("over-"));
        }

        [Test]
        public void Over_pivots_on_its_aligned_edge_and_vertical_middle()
        {
            Assert.AreEqual(new Vector2(0f, 0.5f), PopupPlacer.PivotFor(PopupSide.Over, PopupAlign.Start));
            Assert.AreEqual(new Vector2(0.5f, 0.5f), PopupPlacer.PivotFor(PopupSide.Over, PopupAlign.Center));
            Assert.AreEqual(new Vector2(1f, 0.5f), PopupPlacer.PivotFor(PopupSide.Over, PopupAlign.End));
        }
    }
}
