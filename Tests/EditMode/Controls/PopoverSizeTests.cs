using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using PuguiScreen = PromptUGUI.Application.Screen;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Popover&gt;</c>'s panel and size (spec 2026-10-19 §4.1 / §4.2 / §4.7 / §6): the node itself
    /// is the panel, stacked like a <c>&lt;VStack&gt;</c>, sized by its own fitter (hug by default, a
    /// number, or <c>clamp(min, hug, max)</c>), never taking a place in a parent's flow; it opens beside
    /// its anchor and is re-clamped when its content changes while open.
    /// </summary>
    public class PopoverSizeTests
    {
        private PuguiScreen _screen;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            // Root-canvas space is centred on the origin; a generous area keeps the clamp out of the way.
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private Popover Open(string body)
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                                 + "<Btn id='a' anchor='center' size='40x40'>A</Btn>" + body
                                 + "</Screen></PromptUGUI>");
            _screen = UI.Open("S");
            return _screen.Get<Popover>("p");
        }

        private Btn Anchor => _screen.Get<Btn>("a");

        private Canvas Root => _screen.RootGameObject.GetComponent<Canvas>();

        private Rect InCanvas(RectTransform rt) => PopupCore.RectInCanvas(rt, Root);

        // ── Structure ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Registered_and_reachable_by_id()
        {
            var p = Open("<Popover id='p'><Frame size='10x10'/></Popover>");
            Assert.IsNotNull(p);
        }

        [Test]
        public void The_panel_is_the_node_with_the_default_popup_skin()
        {
            var p = Open("<Popover id='p'><Frame size='10x10'/></Popover>");
            var go = p.GameObject;

            var img = go.GetComponent<UnityImage>();
            Assert.IsNotNull(img);
            Assert.AreEqual(ProceduralBuilders.DefaultPopupBgColor, img.color);
            Assert.IsTrue(img.raycastTarget, "the panel catches the pointer");
            Assert.IsNotNull(go.GetComponent<VerticalLayoutGroup>(), "stacked like a <VStack>");
            Assert.IsNotNull(go.GetComponent<PopoverFitter>());
            Assert.IsNotNull(go.GetComponent<PanelSink>());
            Assert.AreSame(p, go.GetComponent<ExpandableMarker>().Owner);
            Assert.IsNotNull(go.GetComponent<PopupPanelGuard>());
            Assert.IsNotNull(go.GetComponent<Canvas>(), "lifted by the popup core");
        }

        [Test]
        public void Closed_after_open_and_out_of_the_flow()
        {
            var p = Open("<VStack id='v' anchor='center' width='100'>"
                         + "<Frame id='f1' height='20'/>"
                         + "<Popover id='p' width='80' height='60'><Frame height='10'/></Popover>"
                         + "<Frame id='f2' height='20'/></VStack>");
            Canvas.ForceUpdateCanvases();

            Assert.IsFalse(p.GameObject.activeSelf, "closed popovers are switched off after the open pass");
            Assert.IsFalse(p.IsOpen);
            var v = _screen.Get<VStack>("v").RectTransform;
            Assert.AreEqual(40f, LayoutUtility.GetPreferredHeight(v), 0.01f, "it takes no room in the stack");
            var f1 = InCanvas(_screen.Get<Frame>("f1").RectTransform);
            var f2 = InCanvas(_screen.Get<Frame>("f2").RectTransform);
            Assert.AreEqual(f1.yMin, f2.yMax, 0.5f, "f2 sits right under f1");
        }

        // ── Size ──────────────────────────────────────────────────────────────────────────

        [Test]
        public void Hug_number_and_clamp_size_the_panel()
        {
            const string kids = "<Frame width='120' height='30'/><Frame width='80' height='20'/>";

            var hug = Open("<Popover id='p' padding='4'>" + kids + "</Popover>");
            hug.Open(Anchor);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(new Vector2(128f, 58f), hug.RectTransform.sizeDelta, "hug both ways by default");

            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            var fixedW = Open("<Popover id='p' padding='4' width='200' height='hug'>" + kids + "</Popover>");
            fixedW.Open(Anchor);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(new Vector2(200f, 58f), fixedW.RectTransform.sizeDelta);

            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            var clamped = Open("<Popover id='p' padding='4' height='clamp(_, hug, 40)'>" + kids + "</Popover>");
            clamped.Open(Anchor);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(40f, clamped.RectTransform.sizeDelta.y, 0.01f);
        }

        [Test]
        public void The_procedural_surface_does_not_join_the_stack()
        {
            var p = Open("<Popover id='p' padding='6' radius='8'><Frame id='first' width='50' height='20'/></Popover>");
            p.Open(Anchor);
            Canvas.ForceUpdateCanvases();

            var panel = InCanvas(p.RectTransform);
            var first = InCanvas(_screen.Get<Frame>("first").RectTransform);
            Assert.AreEqual(panel.yMax - 6f, first.yMax, 0.5f, "the first author child is the top of the stack");
            Assert.AreEqual(new Vector2(62f, 32f), p.RectTransform.sizeDelta);
        }

        [Test]
        public void A_clamped_hug_ScrollList_inside_sizes_the_panel()
        {
            var p = Open("<Popover id='p'><ScrollList id='l' width='100' height='clamp(_, hug, 50)'>"
                         + "<Frame height='20'/><Frame height='20'/></ScrollList></Popover>");
            p.Open(Anchor);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(40f, p.RectTransform.sizeDelta.y, 0.5f, "as tall as the list's rows");
        }

        [Test]
        public void A_content_change_while_open_resizes_and_repositions()
        {
            // 80 units below the anchor's bottom edge (-20); the panel starts 30 tall and fits.
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -100f, 2000f, 1100f);
            var p = Open("<Popover id='p'><Frame id='c' width='50' height='30'/></Popover>");
            p.Open(Anchor);
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(-24f, InCanvas(p.RectTransform).yMax, 0.5f, "precondition: hangs 4 under the anchor");

            _screen.Get<Frame>("c").GameObject.GetComponent<LayoutElement>().preferredHeight = 120f;
            Canvas.ForceUpdateCanvases();

            Assert.AreEqual(120f, p.RectTransform.sizeDelta.y, 0.5f, "the fitter followed the content");
            Assert.AreEqual(-100f, InCanvas(p.RectTransform).yMin, 0.5f,
                "re-clamped on its locked side: pushed back up inside the bounds");
        }

        [Test]
        public void A_resolve_with_nothing_changed_leaves_it_in_place()
        {
            var p = Open("<Popover id='p' padding='4'><Frame width='50' height='30'/></Popover>");
            p.Open(Anchor);
            Canvas.ForceUpdateCanvases();
            var before = InCanvas(p.RectTransform);

            _screen.ReSolve();
            Canvas.ForceUpdateCanvases();

            var after = InCanvas(p.RectTransform);
            Assert.AreEqual(before.position, after.position);
            Assert.AreEqual(before.size, after.size);
            Assert.IsTrue(p.IsOpen);
        }

        // ── Placement ─────────────────────────────────────────────────────────────────────

        [Test]
        public void Open_with_an_anchor_places_the_panel_beside_it()
        {
            var p = Open("<Popover id='p'><Frame width='100' height='50'/></Popover>");
            p.Open(Anchor);
            Canvas.ForceUpdateCanvases();

            var r = InCanvas(p.RectTransform);
            Assert.AreEqual(-24f, r.yMax, 0.5f, "4 under the anchor's bottom edge");
            Assert.AreEqual(-20f, r.xMin, 0.5f, "left edges aligned (bottom-start)");
            Assert.IsTrue(p.GameObject.activeSelf);
        }

        [Test]
        public void placement_top_end_puts_it_above_right_aligned()
        {
            var p = Open("<Popover id='p' placement='top-end' gap='6'><Frame width='100' height='50'/></Popover>");
            p.Open(Anchor);
            Canvas.ForceUpdateCanvases();

            var r = InCanvas(p.RectTransform);
            Assert.AreEqual(26f, r.yMin, 0.5f);
            Assert.AreEqual(20f, r.xMax, 0.5f);
        }

        // ── Attributes ────────────────────────────────────────────────────────────────────

        [Test]
        public void placement_and_dismiss_reject_unknown_values()
        {
            Assert.Throws<ParseException>(() => Open("<Popover id='p' placement='up'/>"));
            UI.ResetForTests();
            Assert.Throws<ParseException>(() => Open("<Popover id='p' dismiss='never'/>"));
        }

        [Test]
        public void gap_and_transition_fall_back_with_a_warning()
        {
            LogAssert.Expect(LogType.Warning, new Regex(@"PUI-POPOVER-VALUE.*gap='-1'"));
            var p = Open("<Popover id='p' gap='-1'/>");
            Assert.AreEqual(Popover.DefaultGap, p.GapValue);

            UI.ResetForTests();
            LogAssert.Expect(LogType.Warning, new Regex(@"PUI-POPOVER-VALUE.*gap='wide'"));
            p = Open("<Popover id='p' gap='wide'/>");
            Assert.AreEqual(Popover.DefaultGap, p.GapValue);

            UI.ResetForTests();
            LogAssert.Expect(LogType.Warning, new Regex(@"PUI-POPOVER-VALUE.*transition='fast'"));
            p = Open("<Popover id='p' transition='fast'/>");
            Assert.AreEqual(Popover.DefaultTransition, p.TransitionSeconds);
        }

        [Test]
        public void stretch_percent_and_native_sizes_are_rejected()
        {
            Assert.Throws<ParseException>(() => Open("<Popover id='p' width='stretch'/>"));
            UI.ResetForTests();
            Assert.Throws<ParseException>(() => Open("<Popover id='p' height='40%'/>"));
            UI.ResetForTests();
            Assert.Throws<ParseException>(() => Open("<Popover id='p' size='native'/>"));
        }
    }
}
