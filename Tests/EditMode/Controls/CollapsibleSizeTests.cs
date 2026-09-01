using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Collapsible&gt;</c>'s height is never authored — it is header + body, always
    /// (spec 2026-08-31-collapsible-design §5.1). These pin that down through a real layout pass,
    /// free-positioned and inside a stack.
    /// </summary>
    public class CollapsibleSizeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void Drain() => Canvas.ForceUpdateCanvases();

        private const string ThreeRows =
            "<Btn id='r1' height='32'/><Btn id='r2' height='32'/><Btn id='r3' height='32'/>";

        [Test]
        public void Free_positioned_height_is_header_plus_body()
        {
            var c = CollapsibleTests.Open(
                $"<Collapsible id='c' anchor='top-right' width='150' headerHeight='24' transition='0'>{ThreeRows}</Collapsible>")
                .Get<Collapsible>("c");
            Drain();

            Assert.AreEqual(24f + 96f, c.RectTransform.rect.height, 0.5f,
                            "24 header + three 32-tall rows");
        }

        [Test]
        public void Collapsing_shrinks_it_to_the_header()
        {
            var c = CollapsibleTests.Open(
                $"<Collapsible id='c' anchor='top-right' width='150' headerHeight='24' transition='0'>{ThreeRows}</Collapsible>")
                .Get<Collapsible>("c");
            Drain();

            c.Collapse();
            Drain();

            Assert.AreEqual(24f, c.RectTransform.rect.height, 0.5f);
        }

        [Test]
        public void A_margin_positions_the_panel_without_eating_its_height()
        {
            // The panel's height is injected hug, so the author cannot write it — which made a
            // `margin` on the vertical axis silently subtract from it: 24 - 46 < 0 when collapsed,
            // and a negative box turns the rect upside down over the bar above it, swallowing the
            // clicks there. The margin positions the panel; it never sizes it.
            var c = CollapsibleTests.Open(
                $"<Collapsible id='c' anchor='top-right' width='150' margin='46,6,_,_' " +
                $"headerHeight='24' transition='0'>{ThreeRows}</Collapsible>")
                .Get<Collapsible>("c");
            Drain();

            Assert.AreEqual(24f + 96f, c.RectTransform.rect.height, 0.5f, "header + three rows");
            Assert.AreEqual(-46f, c.RectTransform.offsetMax.y, 0.5f, "46 below the parent's top edge");

            c.Collapse();
            Drain();

            Assert.AreEqual(24f, c.RectTransform.rect.height, 0.5f, "collapsed = the header, never negative");
            Assert.AreEqual(-46f, c.RectTransform.offsetMax.y, 0.5f, "…still hanging off the same edge");
        }

        [Test]
        public void Hiding_a_row_reflows_the_panel_with_nothing_to_notify()
        {
            var s = CollapsibleTests.Open(
                $"<Collapsible id='c' anchor='top-right' width='150' headerHeight='24' transition='0'>{ThreeRows}</Collapsible>");
            var c = s.Get<Collapsible>("c");
            Drain();

            s.Get<Btn>("r3").Hidden = true;
            Drain();

            Assert.AreEqual(24f + 64f, c.RectTransform.rect.height, 0.5f,
                            "an open body publishes its content's height every pass, so it follows");
        }

        [Test]
        public void MaxHeight_caps_the_body_but_the_content_keeps_its_size()
        {
            var c = CollapsibleTests.Open(
                $"<Collapsible id='c' anchor='top-right' width='150' headerHeight='24' maxHeight='60' transition='0'>{ThreeRows}</Collapsible>")
                .Get<Collapsible>("c");
            Drain();

            Assert.AreEqual(24f + 60f, c.RectTransform.rect.height, 0.5f, "the fold stops at the cap");
            Assert.AreEqual(96f, CollapsibleTests.Content(c).rect.height, 0.5f,
                            "…while the rows keep their full height, so there is something to scroll");
        }

        [Test]
        public void Width_is_the_authors_business()
        {
            var c = CollapsibleTests.Open(
                $"<Collapsible id='c' anchor='top-right' width='150' headerHeight='24' transition='0'>{ThreeRows}</Collapsible>")
                .Get<Collapsible>("c");
            Drain();
            Assert.AreEqual(150f, c.RectTransform.rect.width, 0.5f);
        }

        [Test]
        public void Inside_a_stack_it_publishes_header_plus_body()
        {
            var s = CollapsibleTests.Open(
                $@"<VStack id='v' anchor='top-right' width='150' spacing='0'>
                     <Collapsible id='c' headerHeight='24' transition='0'>{ThreeRows}</Collapsible>
                     <Btn id='below' height='20'/>
                   </VStack>");
            var c = s.Get<Collapsible>("c");
            Drain();

            Assert.AreEqual(24f + 96f, c.RectTransform.rect.height, 0.5f);
            var belowTop = s.Get<Btn>("below").RectTransform.anchoredPosition.y;

            c.Collapse();
            Drain();

            Assert.AreEqual(24f, c.RectTransform.rect.height, 0.5f);
            Assert.Greater(s.Get<Btn>("below").RectTransform.anchoredPosition.y, belowTop + 90f,
                           "folding pulls what follows up by the body's height");
        }

        [Test]
        public void Stretch_width_inside_a_stack_works()
        {
            var s = CollapsibleTests.Open(
                $@"<VStack id='v' anchor='top-stretch' height='200' margin='0,20,0,20'>
                     <Collapsible id='c' width='stretch' headerHeight='24' transition='0'>{ThreeRows}</Collapsible>
                   </VStack>");
            Drain();
            Assert.Greater(s.Get<Collapsible>("c").RectTransform.rect.width, 100f);
        }

        [Test]
        public void The_body_is_rigid_so_the_column_hands_over_exactly_the_box()
        {
            var c = CollapsibleTests.Open(
                $"<Collapsible id='c' anchor='top-right' width='150' headerHeight='24' transition='0'>{ThreeRows}</Collapsible>")
                .Get<Collapsible>("c");
            Drain();

            var body = CollapsibleTests.Body(c);
            Assert.AreEqual(LayoutUtility.GetPreferredHeight(body), LayoutUtility.GetMinHeight(body), 0.01f);
            Assert.AreEqual(0f, LayoutUtility.GetFlexibleHeight(body), 0.01f);
        }
    }
}
