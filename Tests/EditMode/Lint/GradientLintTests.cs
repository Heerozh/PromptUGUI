using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// Tests for gradient-related lint rules:
    ///   <see cref="ColorLiteralRules.GradientMalformedCode"/> (PUI-COLOR-GRADIENT-MALFORMED) and
    ///   <see cref="GradientModulateRules.GradientModulateCode"/> (PUI-GRADIENT-MODULATE).
    /// </summary>
    public class GradientLintTests
    {
        // ── ColorLiteralRules: gradient shape validation ─────────────────────────

        [Test]
        public void ValidGradient_TwoSegments_NoIssue()
        {
            var n = new IR.ElementNode("Image") { Id = "test" };
            n.Attributes["color"] = "#fff,#000";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void InvalidGradient_ThreeSegments_MalformedIssue()
        {
            var n = new IR.ElementNode("Image") { Id = "bad" };
            n.Attributes["color"] = "#fff,#000,#111";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ColorLiteralRules.GradientMalformedCode, issues[0].Code);
        }

        [Test]
        public void InvalidGradient_EmptyBottomSegment_MalformedIssue()
        {
            var n = new IR.ElementNode("Image") { Id = "bad" };
            n.Attributes["color"] = "#fff,";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ColorLiteralRules.GradientMalformedCode, issues[0].Code);
        }

        [Test]
        public void ValidShape_BadHexBottomSegment_LiteralInvalidIssue()
        {
            // Structural shape is valid (2 segments), but bottom segment is bad hex.
            var n = new IR.ElementNode("Image") { Id = "bad" };
            n.Attributes["color"] = "#fff,#zzz";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ColorLiteralRules.ColorLiteralCode, issues[0].Code);
        }

        [Test]
        public void ValidShape_TokenTopSegment_NoIssue()
        {
            // Token segment is not statically checked; shape is valid.
            var n = new IR.ElementNode("Image") { Id = "test" };
            n.Attributes["color"] = "tok,#000";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.IsEmpty(issues);
        }

        // ── GradientModulateRules ─────────────────────────────────────────────────

        [Test]
        public void HoverModulate_GradientValue_GradientModulateIssue()
        {
            var n = new IR.ElementNode("Btn") { Id = "b" };
            n.Attributes["hoverModulate"] = "#fff,#000";
            var issues = GradientModulateRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(GradientModulateRules.GradientModulateCode, issues[0].Code);
        }

        [Test]
        public void HoverColor_GradientValue_NoIssueFromGradientModulateRules()
        {
            // hoverColor is NOT a *Modulate attribute, so GradientModulateRules is silent.
            var n = new IR.ElementNode("Btn") { Id = "b" };
            n.Attributes["hoverColor"] = "#fff,#000";
            var issues = GradientModulateRules.Check(n).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void HoverColor_GradientValue_NoIssueFromColorLiteralRules()
        {
            // ColorLiteralRules only checks the 'color' attribute; hoverColor is not checked.
            var n = new IR.ElementNode("Btn") { Id = "b" };
            n.Attributes["hoverColor"] = "#fff,#000";
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void PressedModulate_SolidToken_NoIssue()
        {
            // No comma → not a gradient attempt.
            var n = new IR.ElementNode("Btn") { Id = "b" };
            n.Attributes["pressedModulate"] = "solidtoken";
            var issues = GradientModulateRules.Check(n).ToList();
            Assert.IsEmpty(issues);
        }

        [Test]
        public void SelectedModulate_GradientValue_GradientModulateIssue()
        {
            var n = new IR.ElementNode("Tab") { Id = "t" };
            n.Attributes["selectedModulate"] = "#aaa,#bbb";
            var issues = GradientModulateRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(GradientModulateRules.GradientModulateCode, issues[0].Code);
        }

        [Test]
        public void DisabledModulate_GradientValue_GradientModulateIssue()
        {
            var n = new IR.ElementNode("Toggle") { Id = "tg" };
            n.Attributes["disabledModulate"] = "#fff,#000";
            var issues = GradientModulateRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(GradientModulateRules.GradientModulateCode, issues[0].Code);
        }

        // ── IRWalker integration ──────────────────────────────────────────────────

        [Test]
        public void IRWalker_DispatchesGradientModulateRule()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Btn id='b' hoverModulate='#fff,#000'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsTrue(issues.Any(i =>
                i.Code == GradientModulateRules.GradientModulateCode && i.Id == "b"),
                "IRWalker must surface PUI-GRADIENT-MODULATE for hoverModulate gradient");
        }

        [Test]
        public void IRWalker_DispatchesGradientMalformedRule()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Image id='img' color='#fff,#000,#111'/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var issues = IRWalker.Walk(doc).ToList();
            Assert.IsTrue(issues.Any(i =>
                i.Code == ColorLiteralRules.GradientMalformedCode && i.Id == "img"),
                "IRWalker must surface PUI-COLOR-GRADIENT-MALFORMED for 3-segment gradient");
        }
    }
}
