using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// Tests for <c>PUI-NAV-ON-NON-SELECTABLE</c> and <c>PUI-NAV-UNKNOWN-TARGET</c>.
    /// </summary>
    public class NavTargetRulesTests
    {
        private static ElementNode Node(string tag, params (string, string)[] attrs)
        {
            var n = new ElementNode(tag);
            foreach (var (k, v) in attrs) n.Attributes[k] = v;
            return n;
        }

        // ===== PUI-NAV-ON-NON-SELECTABLE =====

        [Test]
        public void NavOnFrame_Errors()
        {
            var issues = NavTargetRules.CheckNav(Node("Frame", ("navUp", "x"))).ToList();
            Assert.IsTrue(issues.Any(i => i.Code == NavTargetRules.NonSelectableCode));
        }

        [Test]
        public void NavOnBtn_NoSelectableError()
        {
            var issues = NavTargetRules.CheckNav(Node("Btn", ("navUp", "x"))).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == NavTargetRules.NonSelectableCode));
        }

        [Test]
        public void FocusOnText_Errors()
        {
            var issues = NavTargetRules.CheckNav(Node("Text", ("focus", "true"))).ToList();
            Assert.IsTrue(issues.Any(i => i.Code == NavTargetRules.NonSelectableCode));
        }

        [Test]
        public void NavModeNoneOnFrame_Errors()
        {
            var issues = NavTargetRules.CheckNav(Node("Frame", ("nav", "none"))).ToList();
            Assert.IsTrue(issues.Any(i => i.Code == NavTargetRules.NonSelectableCode));
        }

        [Test]
        public void NavOnScrollList_NoError()
        {
            var issues = NavTargetRules.CheckNav(Node("ScrollList", ("nav", "none"))).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == NavTargetRules.NonSelectableCode));
        }

        [Test]
        public void NavOnImage_NoNavAttrs_NoError()
        {
            var issues = NavTargetRules.CheckNav(Node("Image")).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void AllSelectableTags_NoError()
        {
            string[] selectables = { "Btn", "Tab", "Toggle", "Slider", "Dropdown", "InputField", "ScrollList" };
            foreach (var tag in selectables)
            {
                var issues = NavTargetRules.CheckNav(Node(tag, ("navUp", "other"))).ToList();
                Assert.IsFalse(issues.Any(i => i.Code == NavTargetRules.NonSelectableCode),
                    $"<{tag}> is selectable — must not produce PUI-NAV-ON-NON-SELECTABLE.");
            }
        }

        [Test]
        public void NavOnFrame_OneIssueTotal()
        {
            // Multiple nav attrs on one non-selectable → still only one issue per node.
            var issues = NavTargetRules.CheckNav(
                Node("Frame", ("navUp", "a"), ("navDown", "b"))).ToList();
            Assert.AreEqual(1,
                issues.Count(i => i.Code == NavTargetRules.NonSelectableCode),
                "Should yield exactly one PUI-NAV-ON-NON-SELECTABLE per node.");
        }

        // ===== PUI-NAV-UNKNOWN-TARGET =====

        [Test]
        public void NavTarget_KnownId_NoError()
        {
            var ids = new HashSet<string> { "btn1", "btn2" };
            var issues = NavTargetRules.CheckNavTarget(Node("Btn", ("navUp", "btn1")), ids).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == NavTargetRules.UnknownTargetCode));
        }

        [Test]
        public void NavTarget_UnknownId_Errors()
        {
            var ids = new HashSet<string> { "btn1" };
            var issues = NavTargetRules.CheckNavTarget(Node("Btn", ("navUp", "btn99")), ids).ToList();
            Assert.IsTrue(issues.Any(i => i.Code == NavTargetRules.UnknownTargetCode));
        }

        [Test]
        public void NavTarget_EmptyScreenIds_NoError()
        {
            // No ids collected → best-effort skip.
            var ids = new HashSet<string>();
            var issues = NavTargetRules.CheckNavTarget(Node("Btn", ("navUp", "x")), ids).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void NavTarget_NullScreenIds_NoError()
        {
            var issues = NavTargetRules.CheckNavTarget(Node("Btn", ("navUp", "x")), null).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void NavTarget_MultipleDirections_UnknownReported()
        {
            var ids = new HashSet<string> { "b1" };
            // navUp known, navDown unknown
            var issues = NavTargetRules.CheckNavTarget(
                Node("Btn", ("navUp", "b1"), ("navDown", "ghost")), ids).ToList();
            var unknowns = issues.Where(i => i.Code == NavTargetRules.UnknownTargetCode).ToList();
            Assert.AreEqual(1, unknowns.Count,
                "Should report exactly one issue for navDown pointing at unknown id.");
            StringAssert.Contains("ghost", unknowns[0].Message);
        }

        [Test]
        public void NavTarget_NoNavAttrs_NoError()
        {
            var ids = new HashSet<string> { "b1" };
            var issues = NavTargetRules.CheckNavTarget(Node("Btn"), ids).ToList();
            Assert.IsEmpty(issues);
        }

        // ===== IRWalker integration =====

        private static UIDocument Parse(string innerXml)
        {
            var xml = $"<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            return UIDocumentParser.Parse(xml);
        }

        [Test]
        public void IRWalker_NavOnNonSelectable_Reported()
        {
            var doc = Parse("<Frame id='f' navUp='x'/>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.Contains(codes, NavTargetRules.NonSelectableCode);
        }

        [Test]
        public void IRWalker_NavOnBtn_NotReported()
        {
            var doc = Parse("<Btn id='b1' navUp='b2'/><Btn id='b2'/>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.DoesNotContain(codes, NavTargetRules.NonSelectableCode);
        }

        [Test]
        public void IRWalker_NavTargetUnknown_Reported()
        {
            var doc = Parse("<Btn id='b1' navUp='nonexistent'/>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.Contains(codes, NavTargetRules.UnknownTargetCode);
        }

        [Test]
        public void IRWalker_NavTargetKnown_NotReported()
        {
            var doc = Parse("<Btn id='b1' navUp='b2'/><Btn id='b2'/>");
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.DoesNotContain(codes, NavTargetRules.UnknownTargetCode);
        }

        [Test]
        public void IRWalker_NavTargetInTemplate_NotReported()
        {
            // Template bodies don't know their screen id context → skip nav-target check.
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='NavBtn'><Btn id='b' navUp='ghost'/></Template>
  <Screen name='S'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var codes = IRWalker.Walk(doc).Select(i => i.Code).ToList();
            CollectionAssert.DoesNotContain(codes, NavTargetRules.UnknownTargetCode,
                "nav-target check is skipped in Template bodies (id context unknown).");
        }
    }
}
