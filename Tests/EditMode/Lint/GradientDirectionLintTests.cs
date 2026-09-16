using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// The direction / multi-stop grammar through the static rules (spec 2026-09-17 §8): shape
    /// errors surface as PUI-COLOR-GRADIENT-MALFORMED on any colour attribute, a third colour on
    /// TMP text is PUI-GRADIENT-STOP-NO-SURFACE, and the three procedural slots that used to be
    /// solid-only trip nothing at all.
    /// </summary>
    public class GradientDirectionLintTests
    {
        private static List<LintIssue> Walk(string body)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                      $"<Screen name='S'>{body}</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        private static bool Has(string body, string code) => Walk(body).Any(i => i.Code == code);

        // ── shape errors ────────────────────────────────────────────────────────

        [TestCase("to right, #fff")]
        [TestCase("#fff, to right, #000")]
        [TestCase("45deg #fff, #000")]
        [TestCase("to up, #fff, #000")]
        [TestCase("#fff, #000, #111, #222, #333")]
        [TestCase("#fff 60%, #000 30%, #111")]
        [TestCase("#fff, 30%, 60%, #000")]
        public void MalformedShape_IsReported(string value)
        {
            var n = new IR.ElementNode("Image") { Id = "bad" };
            n.Attributes["color"] = value;
            var issues = ColorLiteralRules.Check(n).ToList();
            Assert.AreEqual(1, issues.Count, value);
            Assert.AreEqual(ColorLiteralRules.GradientMalformedCode, issues[0].Code);
        }

        [TestCase("to right, #fff, #000")]
        [TestCase("45DEG, #fff, #000")]
        [TestCase("to bottom right, #fff, #00000080 50%, #fff")]
        [TestCase("#fff, #000, #111, #222")]
        [TestCase("to left, #fff, 30%, #000 40%, #111 60%, #222")]
        public void WellFormedShape_IsFine(string value)
        {
            var n = new IR.ElementNode("Image") { Id = "ok" };
            n.Attributes["color"] = value;
            Assert.IsEmpty(ColorLiteralRules.Check(n).ToList(), value);
        }

        [Test]
        public void DirectionSegment_IsNotCheckedAsAColourLiteral()
        {
            // "to bottom right" must never reach the hex validator.
            var n = new IR.ElementNode("Image") { Id = "ok" };
            n.Attributes["color"] = "to bottom right, #fff, #000";
            Assert.IsFalse(ColorLiteralRules.Check(n).Any(i => i.Code == ColorLiteralRules.ColorLiteralCode));
        }

        [Test]
        public void BadLiteral_InsideAThirdStop_IsStillCaught()
        {
            var n = new IR.ElementNode("Image") { Id = "bad" };
            n.Attributes["color"] = "#fff, #000, #zzz";
            Assert.IsTrue(ColorLiteralRules.Check(n).Any(i => i.Code == ColorLiteralRules.ColorLiteralCode));
        }

        // ── TMP text ────────────────────────────────────────────────────────────

        [Test]
        public void Text_Direction_IsFine()
        {
            Assert.IsFalse(Has("<Text id='t' color='to right, #fff, #000'>hi</Text>", GradientStopRules.NoSurfaceCode));
        }

        [Test]
        public void Text_ThirdColour_IsReported()
        {
            Assert.IsTrue(Has("<Text id='t' color='#fff, #888, #000'>hi</Text>", GradientStopRules.NoSurfaceCode));
        }

        [Test]
        public void Label_ThirdColour_IsReported()
        {
            Assert.IsTrue(Has("<Btn id='b' textColor='to right, #fff, #888, #000'>hi</Btn>", GradientStopRules.NoSurfaceCode));
        }

        // ── the procedural slots ────────────────────────────────────────────────

        [TestCase("<Frame id='f' borderWidth='1' borderColor='to right, #fff, #000, #fff'/>")]
        [TestCase("<Frame id='f' glow='8' glowColor='45deg, #fff, #000'/>")]
        [TestCase("<Frame id='f' innerGlow='8' innerGlowColor='#fff, 70%, #000'/>")]
        [TestCase("<Btn id='b' radius='8' borderWidth='1' borderColor='to bottom right, #fff, #000 50%, #fff'>x</Btn>")]
        [TestCase("<Frame id='f'><Decor id='d' kind='line' glow='4' glowColor='to right, #fff, #000'/></Frame>")]
        public void ProceduralSlots_TakeTheFullGrammar_Quietly(string body)
        {
            Assert.IsEmpty(Walk(body).Where(i => i.Code.StartsWith("PUI-GRADIENT") || i.Code.StartsWith("PUI-COLOR")).ToList());
        }

        [Test]
        public void Modulate_StillRejectsADirectedGradient()
        {
            Assert.IsTrue(Has("<Btn id='b' hoverModulate='to right, #fff, #000'>x</Btn>", GradientModulateRules.GradientModulateCode));
        }
    }
}
