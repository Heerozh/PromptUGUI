using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>scrollbarWidth</c> / <c>scrollbarOverlay</c> (requirements §R3). The 20-unit default is
    /// most of a 66-wide grid column on a 640x360 reference canvas, so a grid list needs both a
    /// thinner bar and the option to draw it over the content instead of out of the viewport.
    /// Defaults must not move — existing call sites depend on them.
    /// </summary>
    public class ScrollListScrollbarTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static ScrollList OpenList(string attrs)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'><Screen name='S'>"
                    + $"<ScrollList id='sl' width='150' height='200' {attrs}/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var list = UI.Open("S").Get<ScrollList>("sl");
            Canvas.ForceUpdateCanvases();
            return list;
        }

        private static Scrollbar BarOf(ScrollList sl, string name) =>
            sl.GameObject.transform.Find(name).GetComponent<Scrollbar>();

        private static RectTransform SlidingAreaOf(Scrollbar bar) =>
            (RectTransform)bar.handleRect.parent;

        // ───── the default shape is frozen ─────

        [Test]
        public void The_default_vertical_scrollbar_keeps_its_historical_metrics()
        {
            var list = OpenList("");
            var bar = BarOf(list, "Scrollbar Vertical");
            var scroll = list.GameObject.GetComponent<ScrollRect>();

            Assert.AreEqual(20f, ((RectTransform)bar.transform).sizeDelta.x, 0.001f);
            Assert.AreEqual(new Vector2(-20f, -20f), SlidingAreaOf(bar).sizeDelta);
            Assert.AreEqual(new Vector2(20f, 20f), bar.handleRect.sizeDelta);
            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport,
                scroll.verticalScrollbarVisibility);
            Assert.AreEqual(-3f, scroll.verticalScrollbarSpacing, 0.001f);
        }

        [Test]
        public void The_default_horizontal_scrollbar_keeps_its_historical_metrics()
        {
            var list = OpenList("direction='horizontal'");
            var bar = BarOf(list, "Scrollbar Horizontal");
            var scroll = list.GameObject.GetComponent<ScrollRect>();

            Assert.AreEqual(20f, ((RectTransform)bar.transform).sizeDelta.y, 0.001f);
            Assert.AreEqual(new Vector2(-20f, -20f), SlidingAreaOf(bar).sizeDelta);
            Assert.AreEqual(new Vector2(20f, 20f), bar.handleRect.sizeDelta);
            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport,
                scroll.horizontalScrollbarVisibility);
            Assert.AreEqual(-3f, scroll.horizontalScrollbarSpacing, 0.001f);
        }

        // ───── scrollbarWidth ─────

        [Test]
        public void ScrollbarWidth_resizes_the_bar_the_sliding_area_and_the_handle()
        {
            var bar = BarOf(OpenList("scrollbarWidth='6'"), "Scrollbar Vertical");

            Assert.AreEqual(6f, ((RectTransform)bar.transform).sizeDelta.x, 0.001f);
            Assert.AreEqual(new Vector2(-6f, -6f), SlidingAreaOf(bar).sizeDelta,
                "the sliding area insets by the bar thickness on both axes");
            Assert.AreEqual(new Vector2(6f, 6f), bar.handleRect.sizeDelta,
                "the handle spans the full bar width again");
        }

        [Test]
        public void ScrollbarWidth_is_the_thickness_of_a_horizontal_bar_too()
        {
            var bar = BarOf(OpenList("direction='horizontal' scrollbarWidth='6'"), "Scrollbar Horizontal");

            Assert.AreEqual(6f, ((RectTransform)bar.transform).sizeDelta.y, 0.001f);
            Assert.AreEqual(new Vector2(-6f, -6f), SlidingAreaOf(bar).sizeDelta);
        }

        [Test]
        public void A_bar_thinner_than_the_overlap_never_grows_the_viewport()
        {
            // The stock -3 spacing overlaps the bar into the viewport. Below 3 units that would make
            // the viewport WIDER than the list, so the overlap is capped at the bar's own thickness.
            var scroll = OpenList("scrollbarWidth='2'").GameObject.GetComponent<ScrollRect>();

            Assert.AreEqual(-2f, scroll.verticalScrollbarSpacing, 0.001f);
        }

        [Test]
        public void A_bar_wider_than_the_overlap_keeps_the_stock_spacing()
        {
            var scroll = OpenList("scrollbarWidth='6'").GameObject.GetComponent<ScrollRect>();

            Assert.AreEqual(-3f, scroll.verticalScrollbarSpacing, 0.001f);
        }

        // ───── scrollbarOverlay ─────

        [Test]
        public void ScrollbarOverlay_draws_the_bar_over_the_content()
        {
            var scroll = OpenList("scrollbarOverlay='true'").GameObject.GetComponent<ScrollRect>();

            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHide, scroll.verticalScrollbarVisibility,
                "AutoHide leaves the viewport alone — the 4th grid column stays visible");
        }

        [Test]
        public void ScrollbarOverlay_false_is_the_viewport_expanding_default()
        {
            var scroll = OpenList("scrollbarOverlay='false'").GameObject.GetComponent<ScrollRect>();

            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport,
                scroll.verticalScrollbarVisibility);
        }

        [Test]
        public void ScrollbarOverlay_applies_to_a_horizontal_bar_too()
        {
            var scroll = OpenList("direction='horizontal' scrollbarOverlay='true'")
                .GameObject.GetComponent<ScrollRect>();

            Assert.AreEqual(ScrollRect.ScrollbarVisibility.AutoHide, scroll.horizontalScrollbarVisibility);
        }

        // ───── the bars are built lazily, so arrival order must not matter ─────

        [Test]
        public void Metrics_written_before_the_direction_still_reach_the_lazily_built_bar()
        {
            var a = BarOf(OpenList("scrollbarWidth='6' direction='vertical'"), "Scrollbar Vertical");
            var widthFirst = ((RectTransform)a.transform).sizeDelta.x;
            UI.ResetForTests();
            var b = BarOf(OpenList("direction='vertical' scrollbarWidth='6'"), "Scrollbar Vertical");

            Assert.AreEqual(6f, widthFirst, 0.001f);
            Assert.AreEqual(widthFirst, ((RectTransform)b.transform).sizeDelta.x, 0.001f);
        }

        [Test]
        public void ScrollbarWidth_variant_reaches_the_live_bar()
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'><Screen name='S'>"
                    + "<ScrollList id='sl' width='150' height='200'"
                    + " scrollbarWidth='20' scrollbarWidth.portrait='4'/></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var bar = BarOf(UI.Open("S").Get<ScrollList>("sl"), "Scrollbar Vertical");
            var rt = (RectTransform)bar.transform;

            Assert.AreEqual(20f, rt.sizeDelta.x, 0.001f);

            UI.Variants.Set("portrait", true);
            Assert.AreEqual(4f, rt.sizeDelta.x, 0.001f);

            UI.Variants.Set("portrait", false);
            Assert.AreEqual(20f, rt.sizeDelta.x, 0.001f, "the base value reverts when the variant clears");
        }

        [Test]
        public void Sizing_and_skinning_the_scrollbar_do_not_clobber_each_other()
        {
            var bar = BarOf(
                OpenList("scrollbarWidth='6' scrollbar='' scrollbarColor='#ff0000' scrollbarHandleColor='#00ff00'"),
                "Scrollbar Vertical");

            Assert.AreEqual(6f, ((RectTransform)bar.transform).sizeDelta.x, 0.001f);
            Assert.IsNull(bar.GetComponent<UnityImage>().sprite, "scrollbar='' clears the track sprite");
            Assert.AreEqual(Color.red, bar.GetComponent<UnityImage>().color);
            Assert.AreEqual(Color.green, bar.handleRect.GetComponent<UnityImage>().color);
        }
    }
}
