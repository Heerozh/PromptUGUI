using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    // PUI-PADDING-INT: a layout group's padding is a RectOffset — whole pixels. A fraction made
    // <VStack> / <HStack> / <Grid> / <ScrollList> / <InputField> throw at Open (or on a hot reload) and
    // was silently read as 0 by <TabBar> / <TabMenu> / <Collapsible>; the lint said nothing either way.
    public class PaddingRulesTests
    {
        private static ElementNode Node(string tag, string padding)
        {
            var n = new ElementNode(tag) { Id = "p" };
            if (padding != null) n.Attributes["padding"] = padding;
            return n;
        }

        private static List<LintIssue> Walk(string body)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                      + body + "</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).Where(i => i.Code == PaddingRules.Code).ToList();
        }

        [TestCase("8")]
        [TestCase("4,8")]
        [TestCase("4,8,4,8")]
        [TestCase("_,8,_,8")]
        [TestCase(" 4 , 8 ")]
        [TestCase("-2")]
        [TestCase("")]
        public void Whole_pixels_pass(string padding)
        {
            Assert.IsEmpty(PaddingRules.Check(Node("VStack", padding)).ToList());
        }

        [TestCase("VStack")]
        [TestCase("HStack")]
        [TestCase("Grid")]
        [TestCase("ScrollList")]
        [TestCase("InputField")]
        [TestCase("TabBar")]
        [TestCase("TabMenu")]
        [TestCase("Collapsible")]
        public void A_fraction_is_flagged_on_every_layout_group(string tag)
        {
            var issues = PaddingRules.Check(Node(tag, "4.5,8")).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PaddingRules.Code, issues[0].Code);
            StringAssert.Contains("4.5", issues[0].Message);
            StringAssert.Contains("whole pixels", issues[0].Message);
        }

        [TestCase("4,8,4")]
        [TestCase("1,2,3,4,5")]
        [TestCase("4px")]
        public void A_value_that_is_not_one_two_or_four_integers_is_flagged(string padding)
        {
            Assert.AreEqual(1, PaddingRules.Check(Node("HStack", padding)).Count());
        }

        [Test]
        public void A_scrollbar_padding_is_not_a_rect_offset()
        {
            // <Scrollbar padding="along,across"> takes floats; ScrollbarRules checks it.
            Assert.IsEmpty(PaddingRules.Check(Node("Scrollbar", "1.5,2")).ToList());
        }

        [Test]
        public void A_variant_override_is_checked_too()
        {
            var issues = Walk("<VStack id='v' padding='8' padding.portrait='2.5'/>");

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("2.5", issues[0].Message);
        }

        [Test]
        public void A_template_placeholder_is_left_to_the_expanded_pass()
        {
            Assert.IsEmpty(PaddingRules.Check(Node("VStack", "{{pad}}")).ToList());
        }

        [Test]
        public void The_walker_reports_it()
        {
            Assert.AreEqual(1, Walk("<ScrollList id='list' padding='0.5'/>").Count);
        }
    }
}
