using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Where a <c>&lt;TabMenu&gt;</c>'s popup lands (spec §7.2). The rules themselves are a pure
    /// function — <see cref="PopupPlacer"/> — because EditMode cannot give a ScreenSpaceOverlay
    /// canvas a deterministic size; the integration tests below only check the wiring.
    /// </summary>
    public class TabMenuPlacementTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // A 800x600 canvas centred on the origin, with a 120x44 handle near its top-left.
        private static readonly Rect Canvas800 = new Rect(-400f, -300f, 800f, 600f);
        private static Rect Handle(float x, float y) => new Rect(x, y, 120f, 44f);

        // ── The placement rules ────────────────────────────────────────────────────────────

        [Test]
        public void Defaults_to_below_the_handle_left_aligned()
        {
            var p = PopupPlacer.Solve(Handle(-380f, 200f), new Vector2(200f, 150f), Canvas800, 4f);

            Assert.IsFalse(p.FlippedUp);
            Assert.AreEqual(new Vector2(0f, 0f), p.Anchor, "anchored to the handle's bottom-left");
            Assert.AreEqual(new Vector2(0f, 1f), p.Pivot, "…and hangs down from its own top edge");
            Assert.AreEqual(0f, p.AnchoredPosition.x, "left edges line up");
            Assert.AreEqual(-4f, p.AnchoredPosition.y, "gap below the handle");
        }

        [Test]
        public void Flips_up_when_the_panel_cannot_fit_below()
        {
            // Handle sits at the very bottom: 20px of room below, 200px panel.
            var p = PopupPlacer.Solve(Handle(-380f, -280f), new Vector2(200f, 200f), Canvas800, 4f);

            Assert.IsTrue(p.FlippedUp);
            Assert.AreEqual(new Vector2(0f, 1f), p.Anchor, "anchored to the handle's top-left");
            Assert.AreEqual(new Vector2(0f, 0f), p.Pivot, "…and grows upward from its own bottom edge");
            Assert.AreEqual(4f, p.AnchoredPosition.y, "gap above the handle");
        }

        [Test]
        public void Flips_only_when_the_other_side_is_actually_roomier()
        {
            // Taller than either side can hold. Handle low on the canvas: 100 below vs 456 above —
            // flipping still buys room, so it flips.
            var p = PopupPlacer.Solve(Handle(-380f, -200f), new Vector2(200f, 900f), Canvas800, 4f);
            Assert.IsTrue(p.FlippedUp, "above genuinely has more room here, so flipping does help");

            // Same panel, handle near the top: below is the roomier side, so it must not jump —
            // "below" is the convention and flipping would only make the overflow worse.
            var q = PopupPlacer.Solve(Handle(-380f, 200f), new Vector2(200f, 900f), Canvas800, 4f);
            Assert.IsFalse(q.FlippedUp);
        }

        [Test]
        public void Clamps_left_when_the_panel_would_spill_past_the_right_edge()
        {
            // Handle's left edge at x=300, 200-wide panel → right edge would be 500, canvas ends at 400.
            var p = PopupPlacer.Solve(Handle(300f, 200f), new Vector2(200f, 150f), Canvas800, 4f);
            Assert.AreEqual(-100f, p.AnchoredPosition.x, "pulled back exactly to the edge");
        }

        [Test]
        public void Never_pushes_the_panel_past_the_left_edge()
        {
            // A panel wider than the canvas: clamping right would drag it off the left. Left wins.
            var p = PopupPlacer.Solve(Handle(-380f, 200f), new Vector2(900f, 150f), Canvas800, 4f);
            Assert.AreEqual(-20f, p.AnchoredPosition.x, "stops at the canvas's left edge");
        }

        [Test]
        public void Does_not_move_a_panel_that_already_fits()
        {
            var p = PopupPlacer.Solve(Handle(0f, 0f), new Vector2(200f, 100f), Canvas800, 4f);
            Assert.AreEqual(0f, p.AnchoredPosition.x);
            Assert.IsFalse(p.FlippedUp);
        }

        // ── Wiring ────────────────────────────────────────────────────────────────────────

        private static PromptUGUI.Application.Screen OpenScreen(
            string attrs, string tabs = "<Tab id='a' text='World'/>")
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>" +
                $"<TabMenu id='m' {attrs}>{tabs}</TabMenu>" +
                "</Screen></PromptUGUI>");
            var s = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return s;
        }

        private static TabMenu OpenMenu(string attrs, string tabs = "<Tab id='a' text='World'/>")
            => OpenScreen(attrs, tabs).Get<TabMenu>("m");

        private static RectTransform Popup(TabMenu m) => (RectTransform)m.RectTransform.Find("Popup");

        [Test]
        public void Explicit_popupWidth_wins()
        {
            var m = OpenMenu("popupWidth='240' transition='0'");
            m.Expand();
            Assert.AreEqual(240f, Popup(m).rect.width, 0.01f);
        }

        [Test]
        public void Popup_is_at_least_as_wide_as_the_handle()
        {
            var m = OpenMenu("width='300' transition='0'");
            m.Expand();
            Assert.GreaterOrEqual(Popup(m).rect.width, 300f - 0.01f,
                "with no popupWidth the panel is never narrower than the handle it hangs from");
        }

        [Test]
        public void Popup_height_follows_its_content()
        {
            var m = OpenMenu("popupWidth='240' padding='8' transition='0'",
                             "<Tab id='a' text='A' height='40'/><Tab id='b' text='B' height='40'/>");
            m.Expand();
            Assert.AreEqual(96f, Popup(m).rect.height, 1f, "two 40px rows plus 8px padding top and bottom");
        }

        [Test]
        public void Reposition_survives_a_resolve_while_expanded()
        {
            var screen = OpenScreen("popupWidth='240' transition='0'");
            var m = screen.Get<TabMenu>("m");
            m.Expand();
            var before = Popup(m).anchoredPosition;

            screen.ReSolve();
            Assert.IsTrue(m.IsExpanded, "a resize must not close an open menu (TM-D11)");
            Assert.AreEqual(before, Popup(m).anchoredPosition);
        }
    }
}
