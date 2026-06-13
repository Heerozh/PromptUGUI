using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Lint
{
    public class LayoutGroupChildRulesTests
    {
        [Test]
        public void NoLayoutAttrs_ProducesNoIssue()
        {
            var child = new ElementNode("Text");
            child.Attributes["fontSize"] = "18";
            child.Attributes["size"] = "100x40";
            Assert.IsEmpty(LayoutGroupChildRules.CheckChild(child));
        }

        [Test]
        public void Anchor_ProducesAnchorIssue()
        {
            var child = new ElementNode("Text") { Id = "title" };
            child.Attributes["anchor"] = "stretch";
            var issues = LayoutGroupChildRules.CheckChild(child).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(LayoutGroupChildRules.AnchorCode, issues[0].Code);
            Assert.AreEqual("Text", issues[0].Tag);
            Assert.AreEqual("title", issues[0].Id);
            StringAssert.Contains("'anchor'", issues[0].Message);
        }

        [Test]
        public void Margin_ProducesMarginIssue()
        {
            var child = new ElementNode("Text");
            child.Attributes["margin"] = "8";
            var issues = LayoutGroupChildRules.CheckChild(child).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(LayoutGroupChildRules.MarginCode, issues[0].Code);
            StringAssert.Contains("'margin'", issues[0].Message);
        }

        [Test]
        public void AnchorInVariantOverride_ProducesAnchorIssue()
        {
            var child = new ElementNode("Text");
            child.VariantOverrides["anchor"] =
                new List<(string Variant, string Value)> { ("portrait", "stretch") };
            var issues = LayoutGroupChildRules.CheckChild(child).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(LayoutGroupChildRules.AnchorCode, issues[0].Code);
        }

        [Test]
        public void MarginInVariantOverride_ProducesMarginIssue()
        {
            var child = new ElementNode("Text");
            child.VariantOverrides["margin"] =
                new List<(string Variant, string Value)> { ("portrait", "8") };
            var issues = LayoutGroupChildRules.CheckChild(child).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(LayoutGroupChildRules.MarginCode, issues[0].Code);
        }

        [Test]
        public void AnchorAndMargin_ProducesTwoIssues()
        {
            var child = new ElementNode("Text") { Id = "row" };
            child.Attributes["anchor"] = "stretch";
            child.Attributes["margin"] = "8";
            var issues = LayoutGroupChildRules.CheckChild(child).ToList();
            Assert.AreEqual(2, issues.Count);
            Assert.IsTrue(issues.Any(i => i.Code == LayoutGroupChildRules.AnchorCode));
            Assert.IsTrue(issues.Any(i => i.Code == LayoutGroupChildRules.MarginCode));
            Assert.IsTrue(issues.All(i => i.Tag == "Text" && i.Id == "row"));
        }

        // ===== PUI-GRID-CHILD-SIZE: a direct <Grid> child's own size is overridden by cellSize =====

        [Test]
        public void GridChild_Size_ProducesGridSizeIssue()
        {
            var child = new ElementNode("Btn") { Id = "cell" };
            child.Attributes["size"] = "80x80";
            var issues = LayoutGroupChildRules.CheckGridChild(child).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(LayoutGroupChildRules.GridChildSizeCode, issues[0].Code);
            Assert.AreEqual("Btn", issues[0].Tag);
            Assert.AreEqual("cell", issues[0].Id);
            StringAssert.Contains("cellSize", issues[0].Message);
        }

        [Test]
        public void GridChild_WidthOrHeight_ProducesGridSizeIssue()
        {
            foreach (var attr in new[] { "width", "height" })
            {
                var child = new ElementNode("Image");
                child.Attributes[attr] = "64";
                var issues = LayoutGroupChildRules.CheckGridChild(child).ToList();
                Assert.AreEqual(1, issues.Count, $"'{attr}' on a Grid child should be flagged");
                Assert.AreEqual(LayoutGroupChildRules.GridChildSizeCode, issues[0].Code);
                StringAssert.Contains(attr, issues[0].Message);
            }
        }

        [Test]
        public void GridChild_SizeInVariantOverride_ProducesGridSizeIssue()
        {
            var child = new ElementNode("Image");
            child.VariantOverrides["width"] =
                new List<(string Variant, string Value)> { ("portrait", "64") };
            var issues = LayoutGroupChildRules.CheckGridChild(child).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(LayoutGroupChildRules.GridChildSizeCode, issues[0].Code);
        }

        [Test]
        public void GridChild_AllThreeSizeAttrs_OneCombinedIssue()
        {
            var child = new ElementNode("Btn");
            child.Attributes["size"] = "80x80";
            child.Attributes["width"] = "80";
            child.Attributes["height"] = "80";
            var issues = LayoutGroupChildRules.CheckGridChild(child).ToList();
            Assert.AreEqual(1, issues.Count, "one combined issue per child");
            StringAssert.Contains("size", issues[0].Message);
            StringAssert.Contains("width", issues[0].Message);
            StringAssert.Contains("height", issues[0].Message);
        }

        [Test]
        public void GridChild_NoSize_NoIssue()
        {
            var child = new ElementNode("Btn");
            child.Attributes["text"] = "OK";
            Assert.IsEmpty(LayoutGroupChildRules.CheckGridChild(child));
        }

        [Test]
        public void GridChild_FlowFalse_NoIssue()
        {
            // flow="false" → out of layout flow → GridLayoutGroup skips it → its own size is meaningful again.
            var child = new ElementNode("Image") { Id = "bg" };
            child.Attributes["flow"] = "false";
            child.Attributes["size"] = "80x80";
            Assert.IsEmpty(LayoutGroupChildRules.CheckGridChild(child));
        }

        // ===== IRWalker integration: dispatched ONLY under a <Grid> parent =====

        private static List<LintIssue> Lint(string body)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        [Test]
        public void Walker_GridChildSize_Surfaced()
        {
            var issues = Lint(@"<Grid columns='3' cellSize='64x64'><Btn id='c' size='80x80'>x</Btn></Grid>");
            Assert.IsTrue(issues.Any(i => i.Code == LayoutGroupChildRules.GridChildSizeCode));
        }

        [Test]
        public void Walker_VStackChildSize_NotFlagged()
        {
            // In a VStack/HStack, a child's size IS meaningful (main-axis size) — only Grid overrides it.
            var issues = Lint(@"<VStack><Btn id='c' size='80x80'>x</Btn></VStack>");
            Assert.IsFalse(issues.Any(i => i.Code == LayoutGroupChildRules.GridChildSizeCode));
        }

        [Test]
        public void Walker_GridChildOutOfFlow_NotFlagged()
        {
            var issues = Lint(@"<Grid columns='3' cellSize='64x64'><Image id='bg' flow='false' size='80x80'/></Grid>");
            Assert.IsFalse(issues.Any(i => i.Code == LayoutGroupChildRules.GridChildSizeCode));
        }
    }
}
