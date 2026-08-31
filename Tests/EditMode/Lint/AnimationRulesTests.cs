using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    // PUI-REVEAL-* / PUI-REVERSE-* — spec 2026-08-31-hug-reveal-flip-checked-design §2.3.
    public class AnimationRulesTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static List<LintIssue> Walk(string body)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                      + body + "</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        private static int Count(List<LintIssue> issues, string code) => issues.Count(i => i.Code == code);

        private const string Child = "<VStack width='200'><Btn height='20'/></VStack>";

        // ── reveal ───────────────────────────────────────────────────────────────────────

        [Test]
        public void A_well_formed_reveal_is_quiet()
        {
            var issues = Walk($"<Animation id='a' on='manual' reveal='y'>{Child}</Animation>");

            Assert.IsEmpty(issues.Where(i => i.Code.StartsWith("PUI-REVEAL")).ToList());
        }

        [Test]
        public void Two_children_under_a_reveal_are_flagged()
        {
            var issues = Walk($"<Animation id='a' on='manual' reveal='y'>{Child}{Child}</Animation>");

            Assert.AreEqual(1, Count(issues, AnimationRules.SingleChildCode));
        }

        [Test]
        public void No_children_under_a_reveal_are_flagged()
        {
            var issues = Walk("<Animation id='a' on='manual' reveal='y'/>");

            Assert.AreEqual(1, Count(issues, AnimationRules.SingleChildCode));
        }

        [Test]
        public void Height_on_a_y_reveal_is_a_contradiction()
        {
            var issues = Walk($"<Animation id='a' on='manual' reveal='y' height='140'>{Child}</Animation>");

            Assert.AreEqual(1, Count(issues, AnimationRules.SizeConflictCode));
        }

        [Test]
        public void The_cross_axis_size_is_fine()
        {
            var issues = Walk($"<Animation id='a' on='manual' reveal='y' width='200'>{Child}</Animation>");

            Assert.AreEqual(0, Count(issues, AnimationRules.SizeConflictCode));
        }

        [Test]
        public void A_variant_size_on_the_revealed_axis_is_flagged_too()
        {
            var issues = Walk($"<Animation id='a' on='manual' reveal='x' width='10' width.portrait='200'>{Child}</Animation>");

            Assert.AreEqual(1, Count(issues, AnimationRules.SizeConflictCode));
        }

        [Test]
        public void Size_shorthand_is_flagged()
        {
            var issues = Walk($"<Animation id='a' on='manual' reveal='y' size='200x140'>{Child}</Animation>");

            Assert.AreEqual(1, Count(issues, AnimationRules.SizeConflictCode));
        }

        [Test]
        public void Reveal_plus_scale_is_flagged()
        {
            var issues = Walk($"<Animation id='a' on='manual' reveal='y' scale='0.5'>{Child}</Animation>");

            Assert.AreEqual(1, Count(issues, AnimationRules.ScaleCode));
        }

        [TestCase("stretch")]
        [TestCase("fill")]
        [TestCase("stretch-left")]
        public void A_child_stretching_on_the_revealed_axis_is_flagged(string anchor)
        {
            var issues = Walk(
                $"<Animation id='a' on='manual' reveal='y'><Frame id='c' anchor='{anchor}'/></Animation>");

            Assert.AreEqual(1, Count(issues, AnimationRules.ChildStretchCode));
        }

        [Test]
        public void A_child_stretching_on_the_other_axis_is_fine()
        {
            var issues = Walk(
                "<Animation id='a' on='manual' reveal='y'><Frame id='c' anchor='top-stretch' height='40'/></Animation>");

            Assert.AreEqual(0, Count(issues, AnimationRules.ChildStretchCode));
        }

        [Test]
        public void An_x_reveal_looks_at_the_horizontal_half()
        {
            var issues = Walk(
                "<Animation id='a' on='manual' reveal='x'><Frame id='c' anchor='top-stretch' height='40'/></Animation>");

            Assert.AreEqual(1, Count(issues, AnimationRules.ChildStretchCode));
        }

        // ── reverse-on ───────────────────────────────────────────────────────────────────

        [Test]
        public void Reverse_on_with_loop_is_flagged()
        {
            var issues = Walk(
                $"<Animation id='a' on='manual' reverse-on='manual' fade='0:1' loop='yoyo'>{Child}</Animation>");

            Assert.AreEqual(1, Count(issues, AnimationRules.ReverseLoopCode));
        }

        [Test]
        public void Reverse_on_with_the_text_family_is_flagged()
        {
            var issues = Walk(
                "<Animation id='a' on='manual' reverse-on='manual' count='0:10'><Text id='t'>0</Text></Animation>");

            Assert.AreEqual(1, Count(issues, AnimationRules.ReverseTextCode));
        }

        [Test]
        public void Reverse_on_a_trigger_is_flagged()
        {
            var issues = Walk("<Trigger id='t' on='manual' reverse-on='click'><Frame/></Trigger>");

            Assert.AreEqual(1, Count(issues, AnimationRules.ReverseOnTagCode));
        }

        [Test]
        public void Reverse_on_a_show_is_flagged()
        {
            var issues = Walk(
                "<Btn id='b'><Show on='state-hover' reverse-on='state-normal'><Frame/></Show></Btn>");

            Assert.AreEqual(1, Count(issues, AnimationRules.ReverseOnTagCode));
        }

        [Test]
        public void Reverse_on_an_animation_is_fine()
        {
            var issues = Walk(
                $"<Animation id='a' on='manual' reverse-on='manual' fade='0:1'>{Child}</Animation>");

            Assert.AreEqual(0, Count(issues, AnimationRules.ReverseOnTagCode));
        }

        // ── runtime hard error ───────────────────────────────────────────────────────────

        [Test]
        public void Reveal_plus_scale_throws_at_open()
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>" +
                "<VStack id='outer' anchor='top-left' width='200' height='400'>" +
                $"<Animation id='a' on='manual' reveal='y' scale='0.5'>{Child}</Animation>" +
                "</VStack></Screen></PromptUGUI>");

            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("PUI-REVEAL-SCALE", ex.Message);
        }
    }
}
