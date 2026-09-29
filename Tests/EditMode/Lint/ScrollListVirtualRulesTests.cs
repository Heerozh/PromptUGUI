using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <c>PUI-SCROLL-VIRTUAL-*</c> (spec 2026-09-29-scrolllist-virtualization §4.3): what cannot go with
    /// <c>virtualize="true"</c>. CLI only — the list itself warns once at runtime and falls back (VIR-P1).
    /// </summary>
    public class ScrollListVirtualRulesTests
    {
        private const string Row = "<Template name='Row'><Frame height='30'/></Template>";

        private static PromptUGUI.IR.UIDocument Doc(string list, string styles = "")
            => UIDocumentParser.Parse($@"<?xml version='1.0'?>
<PromptUGUI version='1'>{styles}{Row}<Screen name='S'>{list}</Screen></PromptUGUI>");

        private static string[] Codes(string list, string styles = "") =>
            IRWalker.Walk(Doc(list, styles)).Select(i => i.Code).Where(c => c.StartsWith("PUI-SCROLL-VIRTUAL")).ToArray();

        private static void AssertReports(string code, string list, string styles = "") =>
            CollectionAssert.Contains(Codes(list, styles), code, list);

        private static void AssertClean(string list, string styles = "") =>
            CollectionAssert.IsEmpty(Codes(list, styles), list);

        [Test]
        public void A_plain_virtual_list_is_clean()
        {
            AssertClean("<ScrollList id='sl' height='300' itemTemplate='Row' virtualize='true' stickToEnd='true'/>");
        }

        [Test]
        public void Nothing_is_reported_without_virtualize()
        {
            AssertClean("<ScrollList id='sl' height='hug' columns='2' cellSize='10x10' reorder='true' reuseItems='false'/>");
            AssertClean("<ScrollList id='sl' height='hug' direction='horizontal' virtualize='false'/>");
        }

        // ───── PUI-SCROLL-VIRTUAL-LAYOUT ─────

        [TestCase("columns='2' cellSize='10x10'")]
        [TestCase("direction='horizontal'")]
        [TestCase("columns='0' columns.portrait='3' cellSize='10x10'")]
        [TestCase("direction='vertical' direction.landscape='horizontal'")]
        public void Layout_that_is_not_one_vertical_column_is_reported(string attrs)
        {
            AssertReports(ScrollListRules.VirtualLayoutCode,
                $"<ScrollList id='sl' height='300' itemTemplate='Row' virtualize='true' {attrs}/>");
        }

        [Test]
        public void Columns_zero_is_one_column()
        {
            AssertClean("<ScrollList id='sl' height='300' itemTemplate='Row' virtualize='true' columns='0' direction='vertical'/>");
        }

        [Test]
        public void Virtualize_through_a_class_counts()
        {
            AssertReports(ScrollListRules.VirtualLayoutCode,
                "<ScrollList id='sl' height='300' itemTemplate='Row' class='chat' columns='2' cellSize='10x10'/>",
                "<Style name='chat' virtualize='true'/>");
        }

        // ───── PUI-SCROLL-VIRTUAL-REORDER ─────

        [TestCase("reorder='true'")]
        [TestCase("reorder='false' reorder.edit='true'")]
        public void Reorder_is_reported(string attrs)
        {
            AssertReports(ScrollListRules.VirtualReorderCode,
                $"<ScrollList id='sl' height='300' itemTemplate='Row' virtualize='true' {attrs}/>");
        }

        [Test]
        public void Reorder_false_is_clean()
        {
            AssertClean("<ScrollList id='sl' height='300' itemTemplate='Row' virtualize='true' reorder='false'/>");
        }

        // ───── PUI-SCROLL-VIRTUAL-REUSE ─────

        [Test]
        public void ReuseItems_false_is_reported()
        {
            AssertReports(ScrollListRules.VirtualReuseCode,
                "<ScrollList id='sl' height='300' itemTemplate='Row' virtualize='true' reuseItems='false'/>");
        }

        [Test]
        public void ReuseItems_false_through_a_class_is_reported()
        {
            AssertReports(ScrollListRules.VirtualReuseCode,
                "<ScrollList id='sl' height='300' itemTemplate='Row' virtualize='true' class='fresh'/>",
                "<Style name='fresh' reuseItems='false'/>");
        }

        [Test]
        public void ReuseItems_true_is_clean()
        {
            AssertClean("<ScrollList id='sl' height='300' itemTemplate='Row' virtualize='true' reuseItems='true'/>");
        }

        // ───── PUI-SCROLL-VIRTUAL-HUG ─────

        [TestCase("height='hug'")]
        [TestCase("height='clamp(40, hug, _)'")]
        [TestCase("height='300' height.portrait='hug'")]
        public void An_unbounded_hug_is_reported(string attrs)
        {
            AssertReports(ScrollListRules.VirtualHugCode,
                $"<ScrollList id='sl' itemTemplate='Row' virtualize='true' {attrs}/>");
        }

        [TestCase("height='clamp(_, hug, 200)'")]
        [TestCase("height='clamp(40, hug, 200)'")]
        [TestCase("height='200'")]
        [TestCase("anchor='stretch'")]
        public void A_capped_or_fixed_height_is_clean(string attrs)
        {
            AssertClean($"<ScrollList id='sl' itemTemplate='Row' virtualize='true' {attrs}/>");
        }

        [TestCase("hug", true)]
        [TestCase(" hug ", true)]
        [TestCase("clamp(40, hug, _)", true)]
        [TestCase("clamp(40,hug,)", true)]
        [TestCase("clamp(_, hug, 200)", false)]
        [TestCase("clamp(40, stretch, _)", false)]
        [TestCase("200", false)]
        [TestCase(null, false)]
        public void IsOpenEndedHug(string value, bool expected)
        {
            Assert.AreEqual(expected, HugRules.IsOpenEndedHug(value));
        }

        // ───── PUI-SCROLL-VIRTUAL-VARIANT ─────

        [TestCase("virtualize='true' virtualize.portrait='false'")]
        [TestCase("virtualize.portrait='true'")]
        public void A_virtualize_variant_is_reported(string attrs)
        {
            AssertReports(ScrollListRules.VirtualVariantCode,
                $"<ScrollList id='sl' height='300' itemTemplate='Row' {attrs}/>");
        }

        // ───── PUI-SCROLL-VIRTUAL-TEMPLATE ─────

        [Test]
        public void No_itemTemplate_is_reported()
        {
            AssertReports(ScrollListRules.VirtualTemplateCode, "<ScrollList id='sl' height='300' virtualize='true'/>");
        }

        [Test]
        public void ItemTemplate_through_a_class_is_clean()
        {
            AssertClean("<ScrollList id='sl' height='300' virtualize='true' class='rows'/>",
                "<Style name='rows' itemTemplate='Row'/>");
        }

        [Test]
        public void Each_finding_names_the_list_and_the_fix()
        {
            var issue = IRWalker.Walk(Doc("<ScrollList id='chat' height='300' virtualize='true' reorder='true' itemTemplate='Row'/>"))
                .Single(i => i.Code == ScrollListRules.VirtualReorderCode);
            StringAssert.Contains("chat", issue.Message);
            StringAssert.Contains("Fix:", issue.Message);
        }
    }
}
