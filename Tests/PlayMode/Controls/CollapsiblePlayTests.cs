using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// The parts of <c>&lt;Collapsible&gt;</c> that need a running player loop: the LitMotion fold
    /// (EditMode never ticks it), its interruption behaviour, and the deactivation that only a
    /// finished collapse is allowed to perform. Spec 2026-08-31-collapsible-design §5.2.
    /// </summary>
    public class CollapsiblePlayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        private const string ThreeRows =
            "<Btn id='r1' height='32'/><Btn id='r2' height='32'/><Btn id='r3' height='32'/>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string body)
        {
            UI.LoadDocument("t", Header + body + Footer);
            return UI.Open("S");
        }

        private static PromptUGUI.Application.Screen OpenPanel(string attrs = "transition='0.2s'")
            => Open($@"<VStack id='v' anchor='top-left' width='150' spacing='0'>
                         <Collapsible id='c' text='任务' headerHeight='24' {attrs}>{ThreeRows}</Collapsible>
                         <Btn id='below' height='20'/>
                       </VStack>");

        private static RectTransform Body(Collapsible c) => (RectTransform)c.RectTransform.Find("Body");
        private static RectTransform Content(Collapsible c) => (RectTransform)Body(c).Find("Content");
        private static float BodyHeight(Collapsible c) => LayoutUtility.GetPreferredHeight(Body(c));
        private static float ArrowTurn(Collapsible c)
            => c.RectTransform.Find("Header/Arrow").GetComponent<RotateFlipEffect>().Rotation;

        [UnityTest]
        public IEnumerator Collapsing_shrinks_the_body_over_time()
        {
            var c = OpenPanel().Get<Collapsible>("c");
            yield return null;
            Assert.AreEqual(96f, BodyHeight(c), 1f, "three 32-tall rows to start with");

            c.Collapse();
            yield return null;
            var midway = BodyHeight(c);
            Assert.Less(midway, 96f, "it starts shrinking immediately");
            Assert.Greater(midway, 0f, "…and does not snap");

            yield return new WaitForSeconds(0.35f);
            Assert.AreEqual(0f, BodyHeight(c), 0.5f);
            Assert.AreEqual(180f, ArrowTurn(c), 1f, "the caret ends up pointing the other way");
            Assert.AreEqual(0f, Body(c).GetComponent<CanvasGroup>().alpha, 0.01f);
            Assert.IsFalse(Content(c).gameObject.activeSelf, "a finished collapse switches the rows off");
        }

        [UnityTest]
        public IEnumerator What_follows_the_panel_moves_up_as_it_folds()
        {
            var s = OpenPanel();
            var c = s.Get<Collapsible>("c");
            var below = s.Get<Btn>("below").RectTransform;
            yield return null;

            var start = below.anchoredPosition.y;
            c.Collapse();
            yield return new WaitForSeconds(0.1f);
            var midway = below.anchoredPosition.y;
            yield return new WaitForSeconds(0.3f);
            var end = below.anchoredPosition.y;

            Assert.Greater(midway, start, "an inline fold re-flows the page as it goes");
            Assert.Greater(end, midway);
            Assert.AreEqual(96f, end - start, 1f, "…by exactly the body's height");
        }

        [UnityTest]
        public IEnumerator Re_opening_mid_collapse_reverses_without_a_jump()
        {
            var c = OpenPanel("transition='0.4s'").Get<Collapsible>("c");
            yield return null;

            c.Collapse();
            yield return new WaitForSeconds(0.15f);
            var atInterrupt = BodyHeight(c);
            Assert.Less(atInterrupt, 96f);
            Assert.Greater(atInterrupt, 0f);

            c.Expand();
            yield return null;
            Assert.AreEqual(atInterrupt, BodyHeight(c), 25f,
                            "the fold reverses from where it was, it does not snap to either end");

            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(96f, BodyHeight(c), 1f);
            Assert.IsTrue(Content(c).gameObject.activeSelf,
                          "the cancelled collapse must not switch off a body that re-opened");
        }

        [UnityTest]
        public IEnumerator An_open_panel_follows_its_content()
        {
            var s = OpenPanel();
            var c = s.Get<Collapsible>("c");
            yield return null;

            s.Get<Btn>("r3").Hidden = true;
            yield return null;

            Assert.AreEqual(64f, BodyHeight(c), 1f,
                            "an idle open body publishes the content's height every pass");
        }

        [UnityTest]
        public IEnumerator A_resolve_mid_fold_does_not_reset_it()
        {
            var c = OpenPanel("transition='0.4s'").Get<Collapsible>("c");
            yield return null;

            c.Collapse();
            yield return new WaitForSeconds(0.15f);
            var before = BodyHeight(c);

            UI.Variants.Set("mobile", true);   // any ReSolve
            yield return null;

            Assert.LessOrEqual(BodyHeight(c), before + 1f,
                               "a variant pass must not hand the fold back to its start");
            Assert.Greater(BodyHeight(c), 0f);

            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(0f, BodyHeight(c), 0.5f, "…and it still lands closed");
        }

        [UnityTest]
        public IEnumerator The_body_clips_while_folding_and_stops_when_it_fits()
        {
            var c = OpenPanel().Get<Collapsible>("c");
            yield return null;
            var mask = Body(c).GetComponent<RectMask2D>();
            Assert.IsFalse(mask.enabled, "nothing to clip when the box equals the content");

            c.Collapse();
            yield return null;
            Assert.IsTrue(mask.enabled, "mid-fold the rows stick out of the box");

            c.Expand();
            yield return new WaitForSeconds(0.35f);
            Assert.IsFalse(mask.enabled, "…and the mask stands down again once everything fits");
        }

        [UnityTest]
        public IEnumerator A_capped_body_keeps_clipping()
        {
            var c = Open($@"<Collapsible id='c' anchor='top-left' width='150' text='任务'
                                         headerHeight='24' maxHeight='60' transition='0.2s'>{ThreeRows}</Collapsible>")
                .Get<Collapsible>("c");
            yield return null;

            Assert.AreEqual(60f, BodyHeight(c), 1f);
            Assert.IsTrue(Body(c).GetComponent<RectMask2D>().enabled,
                          "the rows are taller than the cap, so the clip stays on");
        }

        [UnityTest]
        public IEnumerator A_hidden_panel_folds_without_animating()
        {
            var s = Open($@"<Collapsible id='c' anchor='top-left' width='150' text='任务'
                                         headerHeight='24' hidden='true' transition='0.4s'>{ThreeRows}</Collapsible>");
            var c = s.Get<Collapsible>("c");
            yield return null;

            c.Collapse();
            yield return null;

            Assert.IsFalse(c.IsExpanded);
            Assert.IsFalse(Content(c).gameObject.activeSelf,
                           "nothing to animate for a panel nobody can see — write the end state");
        }
    }
}
