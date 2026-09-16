using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// Lint for <c>&lt;ScrollList reorder&gt;</c> (spec 2026-09-16-scrolllist-drag-reorder §4.5):
    /// <c>PUI-REORDER-HANDLE-ID</c> (the handle id must exist in the row) and
    /// <c>PUI-REORDER-VALUE</c> (<c>reorderHold</c> / <c>reorderDuration</c> must be durations).
    /// </summary>
    public class ScrollListReorderRulesTests
    {
        private static PromptUGUI.IR.UIDocument Doc(string inner, string templates = "")
            => UIDocumentParser.Parse($@"<?xml version='1.0'?>
<PromptUGUI version='1'>{templates}<Screen name='S'>{inner}</Screen></PromptUGUI>");

        private const string RowWithGrip =
            "<Template name='Row'><HStack height='30'><Frame id='grip' width='20'/><Text id='label'>x</Text></HStack></Template>";

        // ───── PUI-REORDER-HANDLE-ID ─────

        [Test]
        public void Handle_present_in_the_item_template_is_fine()
        {
            var issues = IRWalker.Walk(Doc(
                "<ScrollList itemTemplate='Row' reorder='true' reorderHandle='grip'/>", RowWithGrip)).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == ScrollListRules.ReorderHandleCode));
        }

        [Test]
        public void Handle_missing_from_the_item_template_is_reported()
        {
            var issues = IRWalker.Walk(Doc(
                "<ScrollList id='sl' itemTemplate='Row' reorder='true' reorderHandle='nope'/>", RowWithGrip)).ToList();
            var issue = issues.SingleOrDefault(i => i.Code == ScrollListRules.ReorderHandleCode);
            Assert.IsNotNull(issue);
            StringAssert.Contains("nope", issue.Message);
            StringAssert.Contains("Row", issue.Message);
        }

        [Test]
        public void Handle_checked_against_each_static_child_when_there_is_no_item_template()
        {
            var issues = IRWalker.Walk(Doc(
                "<ScrollList id='sl' reorder='true' reorderHandle='grip'>"
                + "<HStack><Frame id='grip'/><Text>a</Text></HStack>"
                + "<HStack><Text>b</Text></HStack>"          // no grip here
                + "<Scrollbar/>"                              // chrome, not a row
                + "</ScrollList>")).ToList();
            Assert.AreEqual(1, issues.Count(i => i.Code == ScrollListRules.ReorderHandleCode),
                "one finding per static row that lacks the handle; the bar is not a row");
        }

        [Test]
        public void Handle_from_a_template_this_document_does_not_declare_is_not_judged()
        {
            // An imported library's template body is not in this document: unresolvable, not wrong.
            var issues = IRWalker.Walk(Doc(
                "<ScrollList itemTemplate='Elsewhere' reorder='true' reorderHandle='grip'/>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == ScrollListRules.ReorderHandleCode));
        }

        [Test]
        public void Handle_without_reorder_is_still_checked()
        {
            // reorder.portrait="true" may switch it on later; the handle must exist either way.
            var issues = IRWalker.Walk(Doc(
                "<ScrollList itemTemplate='Row' reorderHandle='nope'/>", RowWithGrip)).ToList();
            Assert.That(issues.Any(i => i.Code == ScrollListRules.ReorderHandleCode));
        }

        // ───── PUI-REORDER-VALUE ─────

        [TestCase("auto")]
        [TestCase("0")]
        [TestCase("0.4")]
        [TestCase("0.4s")]
        [TestCase("400ms")]
        public void Valid_hold_values_pass(string hold)
        {
            var issues = IRWalker.Walk(Doc($"<ScrollList reorder='true' reorderHold='{hold}'/>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == ScrollListRules.ReorderValueCode), hold);
        }

        [TestCase("soon")]
        [TestCase("-1")]
        [TestCase("0.4sec")]
        public void Bad_hold_values_are_reported(string hold)
        {
            var issues = IRWalker.Walk(Doc($"<ScrollList id='sl' reorder='true' reorderHold='{hold}'/>")).ToList();
            var issue = issues.SingleOrDefault(i => i.Code == ScrollListRules.ReorderValueCode);
            Assert.IsNotNull(issue, hold);
            StringAssert.Contains("reorderHold", issue.Message);
        }

        [Test]
        public void Auto_is_not_a_duration_value()
        {
            var issues = IRWalker.Walk(Doc("<ScrollList id='sl' reorder='true' reorderDuration='auto'/>")).ToList();
            var issue = issues.SingleOrDefault(i => i.Code == ScrollListRules.ReorderValueCode);
            Assert.IsNotNull(issue);
            StringAssert.Contains("reorderDuration", issue.Message);
        }

        [Test]
        public void Variant_values_are_checked_too()
        {
            var issues = IRWalker.Walk(Doc(
                "<ScrollList id='sl' reorder='true' reorderHold='auto' reorderHold.portrait='later'/>")).ToList();
            Assert.That(issues.Any(i => i.Code == ScrollListRules.ReorderValueCode));
        }

        [Test]
        public void Bad_value_arriving_through_a_style_pack_is_reported()
        {
            var issues = IRWalker.Walk(UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Style name='sortable' reorder='true' reorderDuration='fast'/>
  <Screen name='S'><ScrollList id='sl' class='sortable'/></Screen>
</PromptUGUI>")).ToList();
            Assert.That(issues.Any(i => i.Code == ScrollListRules.ReorderValueCode));
        }
    }
}
