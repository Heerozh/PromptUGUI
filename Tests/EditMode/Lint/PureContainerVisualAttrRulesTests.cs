using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// Pure containers (`Frame` / `VStack` / `HStack` / `Grid` / `SafeArea`) carry no `Graphic`
    /// component on their root — `sprite=` / `color=` would be silently dropped by
    /// <see cref="PromptUGUI.Application.ControlAttributeApplier"/>. The CLI surfaces this so
    /// the author hears about it at write time instead of staring at an invisible background.
    /// (`mask=` is fine — it adds a `RectMask2D`, not a `Graphic`.)
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

        [Test]
        public void Frame_Color_VisualAttrIssue()
        {
            var n = new ElementNode("Frame");
            n.Attributes["color"] = "#222";
            var issues = PureContainerVisualAttrRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PureContainerVisualAttrRules.VisualAttrCode, issues[0].Code);
            StringAssert.Contains("color", issues[0].Message);
        }

        [Test]
        public void Frame_SpriteAndColor_TwoIssues()
        {
            var n = new ElementNode("Frame");
            n.Attributes["sprite"] = "ui:card";
            n.Attributes["color"] = "#222";
            var issues = PureContainerVisualAttrRules.Check(n).ToList();
            Assert.AreEqual(2, issues.Count);
            Assert.IsTrue(issues.All(i => i.Code == PureContainerVisualAttrRules.VisualAttrCode));
            CollectionAssert.AreEquivalent(new[] { "sprite", "color" }, ExtractAttrNames(issues));
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
        public void OtherPureContainers_Sprite_Issue(string tag)
        {
            var n = new ElementNode(tag);
            n.Attributes["sprite"] = "ui:card";
            var issues = PureContainerVisualAttrRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(PureContainerVisualAttrRules.VisualAttrCode, issues[0].Code);
            StringAssert.Contains(tag, issues[0].Message);
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
