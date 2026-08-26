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

        // ===== 第三档：挂了 Image 的控件（spec §12.1） =====
        //
        // 中间那一档一直没人补：`color` / `sprite` 在这些标签上有效（它们有 Image），但程序化那一组
        // 需要 ProceduralPanel，而全仓只有 <Frame> 会挂 —— 于是 <Btn radius="8"> 三道关卡全部放行，
        // 彻底静默。这是农场/玻璃示例实战撞出来的。

        [TestCase("Btn", "radius")]
        [TestCase("Btn", "borderWidth")]
        [TestCase("Btn", "glass")]
        [TestCase("Toggle", "radius")]
        [TestCase("Slider", "glow")]
        [TestCase("Dropdown", "borderColor")]
        [TestCase("InputField", "radius")]
        [TestCase("ScrollList", "frost")]
        [TestCase("Progress", "radius")]
        [TestCase("Image", "radius")]
        [TestCase("RawImage", "glow")]
        [TestCase("Text", "radius")]
        [TestCase("Btn", "weld")]
        public void ImageBackedControl_ProceduralAttr_VisualAttrIssue(string tag, string attr)
        {
            var n = new ElementNode(tag) { Id = "x" };
            n.Attributes[attr] = "8";
            var issues = PureContainerVisualAttrRules.Check(n).ToList();

            Assert.AreEqual(1, issues.Count, $"<{tag} {attr}=> should be reported exactly once");
            Assert.AreEqual(PureContainerVisualAttrRules.VisualAttrCode, issues[0].Code);
            StringAssert.Contains(attr, issues[0].Message);
            StringAssert.Contains("Frame", issues[0].Message);
        }

        [TestCase("Btn")]
        [TestCase("Toggle")]
        [TestCase("Image")]
        [TestCase("Progress")]
        public void ImageBackedControl_Color_NoIssue(string tag)
        {
            // color 是这一档与纯排版容器的分界线：它们有 Image，所以 color 真的生效。
            var n = new ElementNode(tag);
            n.Attributes["color"] = "#fff";
            Assert.IsEmpty(PureContainerVisualAttrRules.Check(n));
        }

        [TestCase("Btn")]
        [TestCase("Image")]
        [TestCase("ScrollList")]
        public void ImageBackedControl_Sprite_NoIssue(string tag)
        {
            var n = new ElementNode(tag);
            n.Attributes["sprite"] = "ui:card";
            Assert.IsEmpty(PureContainerVisualAttrRules.Check(n));
        }

        [Test]
        public void UnknownTag_SaysNothing()
        {
            // 模板调用：CLI 在展开前看不见它的 body，断言什么都是猜。
            var n = new ElementNode("MyCard") { Id = "c" };
            n.Attributes["radius"] = "8";
            Assert.IsFalse(PureContainerVisualAttrRules.AppliesTo("MyCard"));
            Assert.IsEmpty(PureContainerVisualAttrRules.Check(n));
        }

        [Test]
        public void ImageBackedControl_ReportsEveryOffendingAttr_NotJustTheFirst()
        {
            var n = new ElementNode("Btn") { Id = "b" };
            n.Attributes["radius"] = "8";
            n.Attributes["glow"] = "4";
            Assert.AreEqual(2, PureContainerVisualAttrRules.Check(n).Count());
        }

        [Test]
        public void ImageBackedControl_VariantOverride_IsAlsoReported()
        {
            var n = new ElementNode("Btn") { Id = "b" };
            n.VariantOverrides["radius"] = new List<(string, string)> { ("mobile", "8") };
            Assert.AreEqual(1, PureContainerVisualAttrRules.Check(n).Count());
        }
    }
}
