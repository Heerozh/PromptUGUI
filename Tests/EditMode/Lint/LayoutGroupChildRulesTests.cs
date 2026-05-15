using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

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
    }
}
