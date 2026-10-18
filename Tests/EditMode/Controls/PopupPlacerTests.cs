using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// What <see cref="PopupPlacer"/> gained for the suggestion list (spec 2026-10-18 §4.6): a preferred
    /// side, a placement area smaller than the canvas (the safe area, minus a soft keyboard), and the room
    /// left on the chosen side so a host can clamp its panel's height. The TabMenu rules themselves are
    /// pinned by <see cref="TabMenuPlacementTests"/>, which must keep passing unchanged.
    /// </summary>
    public class PopupPlacerTests
    {
        // A 800x600 canvas centred on the origin, with a 120x44 handle.
        private static readonly Rect Canvas800 = new Rect(-400f, -300f, 800f, 600f);
        private static Rect Handle(float x, float y) => new Rect(x, y, 120f, 44f);

        [Test]
        public void Prefers_above_when_asked_and_it_fits()
        {
            // 256 above the handle, 150 + 4 needed.
            var p = PopupPlacer.Solve(Handle(-380f, 0f), new Vector2(200f, 150f), Canvas800, 4f, preferAbove: true);

            Assert.IsTrue(p.FlippedUp);
            Assert.AreEqual(new Vector2(0f, 1f), p.Anchor, "anchored to the handle's top-left");
            Assert.AreEqual(new Vector2(0f, 0f), p.Pivot, "…and stands on its own bottom edge");
            Assert.AreEqual(4f, p.AnchoredPosition.y, "gap above the handle");
        }

        [Test]
        public void Above_preference_flips_down_when_below_has_more_room()
        {
            // Handle near the top: 56 above, 500 below.
            var p = PopupPlacer.Solve(Handle(-380f, 200f), new Vector2(200f, 150f), Canvas800, 4f, preferAbove: true);

            Assert.IsFalse(p.FlippedUp);
            Assert.AreEqual(new Vector2(0f, 0f), p.Anchor);
            Assert.AreEqual(new Vector2(0f, 1f), p.Pivot);
            Assert.AreEqual(-4f, p.AnchoredPosition.y);
        }

        [Test]
        public void A_panel_taller_than_both_sides_flips_only_if_the_other_side_is_roomier()
        {
            var tall = new Vector2(200f, 900f);

            // Preferring above. Handle near the top: below (500) beats above (56), so it moves down.
            Assert.IsFalse(PopupPlacer.Solve(Handle(-380f, 200f), tall, Canvas800, 4f, preferAbove: true).FlippedUp);
            // Handle near the bottom: above (456) beats below (100), so it stays above.
            Assert.IsTrue(PopupPlacer.Solve(Handle(-380f, -200f), tall, Canvas800, 4f, preferAbove: true).FlippedUp);

            // Preferring below (the TabMenu default) is the mirror image.
            Assert.IsTrue(PopupPlacer.Solve(Handle(-380f, -200f), tall, Canvas800, 4f).FlippedUp);
            Assert.IsFalse(PopupPlacer.Solve(Handle(-380f, 200f), tall, Canvas800, 4f).FlippedUp);
        }

        [Test]
        public void Room_is_the_space_on_the_chosen_side_minus_the_gap()
        {
            // Handle at y 0..44: 300 below, 256 above.
            var below = PopupPlacer.Solve(Handle(-380f, 0f), new Vector2(200f, 100f), Canvas800, 4f);
            Assert.IsFalse(below.FlippedUp);
            Assert.AreEqual(296f, below.Room, 0.001f);

            var above = PopupPlacer.Solve(Handle(-380f, 0f), new Vector2(200f, 100f), Canvas800, 4f, preferAbove: true);
            Assert.IsTrue(above.FlippedUp);
            Assert.AreEqual(252f, above.Room, 0.001f);
        }

        [Test]
        public void Room_is_never_negative()
        {
            // The handle itself sits past the bottom of the area.
            var p = PopupPlacer.Solve(new Rect(-380f, -320f, 120f, 10f), new Vector2(200f, 100f),
                                      new Rect(-400f, -300f, 800f, 20f), 4f);
            Assert.GreaterOrEqual(p.Room, 0f);
        }

        [Test]
        public void Bounds_smaller_than_the_canvas_limit_room_and_the_right_clamp()
        {
            // A safe area inset 50 at the bottom and top and 100 on the right.
            var safe = Rect.MinMaxRect(-400f, -250f, 300f, 250f);

            var p = PopupPlacer.Solve(Handle(200f, 0f), new Vector2(200f, 100f), safe, 4f);

            Assert.IsFalse(p.FlippedUp);
            Assert.AreEqual(-100f, p.AnchoredPosition.x, 0.001f, "pulled back inside the safe right edge, not the canvas's");
            Assert.AreEqual(246f, p.Room, 0.001f, "measured to the safe bottom edge");
        }
    }
}
