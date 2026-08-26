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

        // ===== 第三档：有 Graphic 但还没有程序化表面的标签（spec §12.1） =====
        //
        // 中间那一档一直没人补：`color` / `sprite` 在这些标签上有效，但程序化那一组需要
        // ProceduralPanel —— 于是 <Btn radius="8"> 曾经三道关卡全部放行，彻底静默。这是农场/玻璃
        // 示例实战撞出来的。M1/M2 之后 <Btn> 等控件已经真的能画了，这一档只剩下还没接线的标签，
        // 名单随之收缩；ProceduralAttrNamesTests 负责在有标签挪动时把这里叫醒。

        // Tags with an Image (or at least a Graphic) but no procedural surface — the list shrinks
        // as controls are wired up, and ProceduralAttrNamesTests is what catches one that moved.
        // <Image> / <RawImage> stay here on purpose: a sprite IS their point, and a procedural
        // rectangle is what <Frame> is for.
        [TestCase("Image", "borderWidth")]
        [TestCase("RawImage", "glass")]
        [TestCase("Text", "radius")]
        [TestCase("Carousel", "radius")]
        [TestCase("Markdown", "glow")]
        [TestCase("TabBar", "borderColor")]
        [TestCase("Image", "radius")]
        [TestCase("Icon", "frost")]
        [TestCase("RawImage", "radius")]
        [TestCase("Image", "weld")]
        public void ControlWithoutASurface_ProceduralAttr_VisualAttrIssue(string tag, string attr)
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
        public void ControlWithAnImage_Color_NoIssue(string tag)
        {
            // color 是这一档与纯排版容器的分界线：它们有 Image，所以 color 真的生效。
            var n = new ElementNode(tag);
            n.Attributes["color"] = "#fff";
            Assert.IsEmpty(PureContainerVisualAttrRules.Check(n));
        }

        [TestCase("Btn")]
        [TestCase("Image")]
        [TestCase("ScrollList")]
        public void ControlWithAnImage_Sprite_NoIssue(string tag)
        {
            var n = new ElementNode(tag);
            n.Attributes["sprite"] = "ui:card";
            Assert.IsEmpty(PureContainerVisualAttrRules.Check(n));
        }

        [Test]
        public void ControlWithASurface_StopsBeingReported_ExceptForWeld()
        {
            // <Btn> draws procedurally now, so the shape attributes work…
            var ok = new ElementNode("Btn") { Id = "b" };
            ok.Attributes["radius"] = "8";
            Assert.IsEmpty(PureContainerVisualAttrRules.Check(ok));

            // …but weld fuses a Frame's glass CHILDREN, which a control does not have (spec §13.2).
            var weld = new ElementNode("Btn") { Id = "b" };
            weld.Attributes["weld"] = "16";
            var issues = PureContainerVisualAttrRules.Check(weld).ToList();
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("weld", issues[0].Message);
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
        public void ControlWithoutASurface_ReportsEveryOffendingAttr_NotJustTheFirst()
        {
            var n = new ElementNode("Image") { Id = "i" };
            n.Attributes["radius"] = "8";
            n.Attributes["glow"] = "4";
            Assert.AreEqual(2, PureContainerVisualAttrRules.Check(n).Count());
        }

        [Test]
        public void ControlWithoutASurface_VariantOverride_IsAlsoReported()
        {
            var n = new ElementNode("Image") { Id = "i" };
            n.VariantOverrides["radius"] = new List<(string, string)> { ("mobile", "8") };
            Assert.AreEqual(1, PureContainerVisualAttrRules.Check(n).Count());
        }
    }
}
