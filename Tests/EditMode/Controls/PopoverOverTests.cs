using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using PuguiScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Popover placement="over-*"&gt;</c> (spec 2026-10-19 §17): the panel covers its anchor — centred
    /// on it vertically, lined up by the alignment horizontally, pushed back inside the bounds and never
    /// flipped — and follows it like a side placement. Driven with <c>TickForTests</c>, the body of the
    /// panel's <c>LateUpdate</c>.
    /// </summary>
    public class PopoverOverTests
    {
        private PuguiScreen _s;
        private Popover _m;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-500f, -300f, 1000f, 600f);
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        private void Open(string placement = "over-end", string popAttrs = "")
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='a' anchor='center' size='40x40'>A</Btn>
  <Popover id='m' placement='" + placement + "' " + popAttrs + @"><Frame width='240' height='50'/></Popover>
</Screen></PromptUGUI>");
            _s = UI.Open("S");
            _m = _s.Get<Popover>("m");
        }

        private Canvas Root => _s.RootGameObject.GetComponent<Canvas>();
        private Btn A => _s.Get<Btn>("a");
        private Rect Anchor => PopupCore.RectInCanvas(A.RectTransform, Root);
        private Rect Panel => PopupCore.RectInCanvas(_m.RectTransform, Root);

        [Test]
        public void Over_end_covers_the_anchor_from_its_right_edge()
        {
            Open();
            _m.Open(A);

            Assert.AreEqual(240f, Panel.width, 0.5f, "precondition: the panel hugs its 240-wide content");
            Assert.AreEqual(Anchor.xMax, Panel.xMax, 0.5f, "right edges");
            Assert.AreEqual(Anchor.center.y, Panel.center.y, 0.5f, "centred on the anchor");
            // Edge by edge: Rect.Contains leaves out the max edges, and the right edges coincide.
            Assert.IsTrue(Panel.xMin <= Anchor.xMin && Panel.yMin <= Anchor.yMin && Panel.yMax >= Anchor.yMax,
                          "the anchor is under the panel");
        }

        [Test]
        public void Over_start_and_center_line_up_and_the_gap_is_ignored()
        {
            Open("over-start", "gap='12'");
            _m.Open(A);
            Assert.AreEqual(Anchor.xMin, Panel.xMin, 0.5f, "left edges");
            Assert.AreEqual(Anchor.center.y, Panel.center.y, 0.5f, "no gap to keep: still centred");

            _m.Close();
            _m.Placement = "over-center";
            _m.Open(A);
            Assert.AreEqual(Anchor.center.x, Panel.center.x, 0.5f);
            Assert.AreEqual(Anchor.center.y, Panel.center.y, 0.5f);
        }

        [Test]
        public void It_follows_a_moving_anchor_and_stays_over_it()
        {
            Open();
            _m.Open(A);
            A.RectTransform.anchoredPosition += new Vector2(-100f, 120f);
            _m.TickForTests();

            Assert.AreEqual(Anchor.xMax, Panel.xMax, 0.5f);
            Assert.AreEqual(Anchor.center.y, Panel.center.y, 0.5f);
        }

        [Test]
        public void A_keyboard_pushes_it_up_inside_without_flipping()
        {
            var safe = new Rect(-500f, -300f, 1000f, 600f);
            PopupBounds.SafeOverride = () => safe;
            Open();
            A.RectTransform.anchoredPosition = new Vector2(0f, -270f);
            _m.Open(A);
            Assert.AreEqual(Anchor.center.y, Panel.center.y, 0.5f, "precondition: centred, it fits");

            // A keyboard slides in and takes the bottom 20: the safe area stays, the bounds shrink.
            PopupBounds.BoundsOverride = () => new Rect(-500f, -280f, 1000f, 580f);
            _m.TickForTests();

            Assert.AreEqual(-280f, Panel.yMin, 0.5f, "pushed up onto the keyboard's edge — still over the anchor");
            Assert.Less(Panel.yMin, Anchor.yMax, "not flipped above it");
        }

        [Test]
        public void A_side_and_over_swap_on_the_next_open()
        {
            Open("bottom-start");
            _m.Open(A);
            _m.Placement = "over-end";
            A.RectTransform.anchoredPosition += new Vector2(10f, 0f);
            _m.TickForTests();
            Assert.AreEqual(Anchor.yMin - 4f, Panel.yMax, 0.5f, "the side chosen at open holds while open");

            _m.Close();
            _m.Open(A);
            Assert.AreEqual(Anchor.xMax, Panel.xMax, 0.5f, "reopened: over");
            Assert.AreEqual(Anchor.center.y, Panel.center.y, 0.5f);
        }

        [Test]
        public void OpenAt_centres_on_the_point()
        {
            Open("over-start");
            var rootRt = (RectTransform)Root.transform;
            var cam = Root.renderMode == RenderMode.ScreenSpaceOverlay ? null : Root.worldCamera;
            var screen = RectTransformUtility.WorldToScreenPoint(cam, rootRt.TransformPoint(new Vector3(30f, 40f, 0f)));

            _m.OpenAt(screen);

            Assert.AreEqual(30f, Panel.xMin, 0.5f, "start: the left edge on the point");
            Assert.AreEqual(40f, Panel.center.y, 0.5f, "centred on it");
        }
    }
}
