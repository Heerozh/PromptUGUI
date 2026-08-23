using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// `Frame` draws fill / radius / border / glow procedurally (ProceduralPanel), so those
    /// attributes are legitimate on it — but it still carries no `Image`, so `sprite=` is dropped.
    /// `VStack` / `HStack` / `Grid` / `SafeArea` remain layout-only and drop every visual attribute.
    /// The CLI surfaces both so the author hears about it at write time instead of staring at an
    /// invisible background. (`mask=` is fine — it adds a `RectMask2D`, not a `Graphic`.)
    /// </summary>
    public class PureContainerVisualAttrRulesTests
    {
        [Test]
        public void Frame_Sprite_VisualAttrIssue()
        {
            var n = new ElementNode("Frame") { Id = "bg" };
            n.Attributes["sprite"] = "ui:card";
            var issues = PureContainerVisualAttrRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PureContainerVisualAttrRules.VisualAttrCode, issues[0].Code);
            StringAssert.Contains("Frame", issues[0].Message);
            StringAssert.Contains("sprite", issues[0].Message);
            StringAssert.Contains("Image", issues[0].Message);
        }

        [TestCase("color")]
        [TestCase("radius")]
        [TestCase("borderWidth")]
        [TestCase("borderColor")]
        [TestCase("glow")]
        [TestCase("glowColor")]
        public void Frame_ProceduralVisualAttrs_NoIssue(string attr)
        {
            // Frame 现在自己画这些 —— 曾经的 "silently ignored" 警告已经过时。
            var n = new ElementNode("Frame");
            n.Attributes[attr] = "1";
            Assert.IsEmpty(PureContainerVisualAttrRules.Check(n));
        }

        [Test]
        public void Frame_SpriteAndColor_OnlySpriteIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["sprite"] = "ui:card";
            n.Attributes["color"] = "#222";
            var issues = PureContainerVisualAttrRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("'sprite'", issues[0].Message);
        }

        [Test]
        public void Frame_OnlyMask_NoIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["mask"] = "rect";
            Assert.IsEmpty(PureContainerVisualAttrRules.Check(n));
        }

        [Test]
        public void Frame_NoVisualAttrs_NoIssue()
        {
            var n = new ElementNode("Frame");
            Assert.IsEmpty(PureContainerVisualAttrRules.Check(n));
        }

        [TestCase("VStack")]
        [TestCase("HStack")]
        [TestCase("Grid")]
        [TestCase("SafeArea")]
        public void LayoutOnlyContainers_Sprite_Issue(string tag)
        {
            var n = new ElementNode(tag);
            n.Attributes["sprite"] = "ui:card";
            var issues = PureContainerVisualAttrRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PureContainerVisualAttrRules.VisualAttrCode, issues[0].Code);
            StringAssert.Contains(tag, issues[0].Message);
        }

        [TestCase("VStack", "color")]
        [TestCase("HStack", "radius")]
        [TestCase("Grid", "borderWidth")]
        [TestCase("SafeArea", "glow")]
        public void LayoutOnlyContainers_ProceduralAttrs_Issue(string tag, string attr)
        {
            // 这些容器既没 Graphic 也没 ProceduralPanel —— 指路"套一层 Frame"。
            var n = new ElementNode(tag);
            n.Attributes[attr] = "8";
            var issues = PureContainerVisualAttrRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("'" + attr + "'", issues[0].Message);
            StringAssert.Contains("Frame", issues[0].Message);
        }

        [Test]
        public void LayoutOnlyContainer_SpriteAndColor_TwoIssues()
        {
            var n = new ElementNode("VStack");
            n.Attributes["sprite"] = "ui:card";
            n.Attributes["color"] = "#222";
            var issues = PureContainerVisualAttrRules.Check(n).ToList();
            Assert.AreEqual(2, issues.Count);
            Assert.IsTrue(issues.All(i => i.Code == PureContainerVisualAttrRules.VisualAttrCode));
            CollectionAssert.AreEquivalent(new[] { "sprite", "color" }, ExtractAttrNames(issues));
        }

        [TestCase("Image")]
        [TestCase("Btn")]
        [TestCase("Toggle")]
        [TestCase("Slider")]
        [TestCase("Dropdown")]
        [TestCase("ScrollList")]
        [TestCase("InputField")]
        [TestCase("Text")]
        public void GraphicCarryingControls_Sprite_NoIssue(string tag)
        {
            // 这些控件原生支持 sprite= / color=; 规则不该误伤。
            // (Text 没 sprite=, 但 color= 合法; 用 sprite 触发一次也得通过 — 规则只看 tag 白名单。)
            var n = new ElementNode(tag);
            n.Attributes["sprite"] = "ui:card";
            n.Attributes["color"] = "#fff";
            Assert.IsEmpty(PureContainerVisualAttrRules.Check(n));
        }

        [Test]
        public void Frame_SpriteInVariantOverride_VisualAttrIssue()
        {
            // 作者用 sprite.mobile="x" 想在 mobile variant 给 Frame 加背景 — 同样无效。
            var n = new ElementNode("Frame") { Id = "bg" };
            n.VariantOverrides["sprite"] =
                new List<(string Variant, string Value)> { ("mobile", "ui:card-mobile") };
            var issues = PureContainerVisualAttrRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PureContainerVisualAttrRules.VisualAttrCode, issues[0].Code);
            StringAssert.Contains("sprite", issues[0].Message);
        }

        private static IEnumerable<string> ExtractAttrNames(IEnumerable<LintIssue> issues)
        {
            // Tests rely on the message containing the attribute name in single quotes.
            foreach (var i in issues)
            {
                foreach (var name in new[] { "sprite", "color" })
                    if (i.Message.Contains("'" + name + "'"))
                        yield return name;
            }
        }
    }
}
