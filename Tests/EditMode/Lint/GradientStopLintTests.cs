using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <c>PUI-GRADIENT-STOP-NO-SURFACE</c>: a gradient stop position only exists per-fragment, in the
    /// procedural shader. A vertex-coloured Graphic has nothing but corner vertices to hang it on, so
    /// the ramp comes out spanning the full height whatever the author wrote — silent at runtime,
    /// which makes the CLI the only place they find out.
    /// </summary>
    public class GradientStopLintTests
    {
        private static List<LintIssue> Walk(string body, string top = "")
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{top}
  <Screen name='S'>
{body}
  </Screen>
</PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        private static bool HasStopIssue(string body, string top = "")
            => Walk(body, top).Any(i => i.Code == GradientStopRules.NoSurfaceCode);

        // ── tags with no procedural surface at all ───────────────────────────────

        [TestCase("<Image id='g' color='#fff 70%,#000'/>")]
        [TestCase("<Icon id='g' name='ui:coin' color='#fff 70%,#000'/>")]
        [TestCase("<RawImage id='g' color='#fff 70%,#000'/>")]
        public void SpriteGraphic_WithStops_IsReported(string node)
        {
            Assert.IsTrue(HasStopIssue(node));
        }

        [Test]
        public void Text_WithStops_IsReported()
        {
            // TMP's gradient is per-character; four glyph corners have nowhere to put a stop.
            Assert.IsTrue(HasStopIssue("<Text id='t' color='#fff 70%,#000'>hi</Text>"));
        }

        [Test]
        public void PlainGradient_NoStops_IsFine()
        {
            Assert.IsFalse(HasStopIssue("<Image id='g' color='#fff,#000'/>"));
        }

        // ── the procedural tags ─────────────────────────────────────────────────

        [Test]
        public void Frame_WithStops_IsFine()
        {
            // Frame's colour always goes to a ProceduralPanel — the shader path, where stops work.
            Assert.IsFalse(HasStopIssue("<Frame id='f' color='#fff 70%,#000'/>"));
        }

        [Test]
        public void Decor_WithStops_IsFine()
        {
            Assert.IsFalse(HasStopIssue("<Decor id='d' kind='bracket' color='#fff 70%,#000'/>"));
        }

        // ── controls: depends on whether the surface is declared ────────────────

        [Test]
        public void Btn_WithoutProceduralAttrs_IsReported()
        {
            Assert.IsTrue(HasStopIssue("<Btn id='b' color='#fff 70%,#000'>ok</Btn>"));
        }

        [Test]
        public void Btn_WithRadius_IsFine()
        {
            Assert.IsFalse(HasStopIssue("<Btn id='b' radius='8' color='#fff 70%,#000'>ok</Btn>"));
        }

        [Test]
        public void Btn_WithGlassFromAClass_IsFine()
        {
            Assert.IsFalse(HasStopIssue(
                "<Btn id='b' class='card' color='#fff 70%,#000'>ok</Btn>",
                "<Style name='card' glass='true' radius='16'/>"));
        }

        [Test]
        public void Btn_WithAnUnresolvableClass_StaysQuiet()
        {
            // The class may carry radius from an imported commons the CLI never sees.
            Assert.IsFalse(HasStopIssue("<Btn id='b' class='from-commons' color='#fff 70%,#000'>ok</Btn>"));
        }

        [Test]
        public void Btn_StateColour_FollowsTheMainSurface()
        {
            Assert.IsTrue(HasStopIssue("<Btn id='b' hoverColor='#fff 70%,#000'>ok</Btn>"));
            Assert.IsFalse(HasStopIssue("<Btn id='b' radius='8' hoverColor='#fff 70%,#000'>ok</Btn>"));
        }

        [Test]
        public void Progress_BgColour_IsTheMainSurface()
        {
            Assert.IsTrue(HasStopIssue("<Progress id='p' bgColor='#fff 70%,#000'/>"));
            Assert.IsFalse(HasStopIssue("<Progress id='p' radius='4' bgColor='#fff 70%,#000'/>"));
        }

        // ── inner layers: gated by their own shape attribute ────────────────────

        [Test]
        public void Slider_FillColour_NeedsFillRadius()
        {
            Assert.IsTrue(HasStopIssue("<Slider id='s' fillColor='#fff 70%,#000'/>"));
            Assert.IsFalse(HasStopIssue("<Slider id='s' fillRadius='4' fillColor='#fff 70%,#000'/>"));
        }

        [Test]
        public void Slider_HandleColour_NeedsHandleRadius()
        {
            Assert.IsTrue(HasStopIssue("<Slider id='s' handleColor='#fff 70%,#000'/>"));
            Assert.IsFalse(HasStopIssue("<Slider id='s' handleRadius='4' handleColor='#fff 70%,#000'/>"));
        }

        [Test]
        public void Progress_FrameColour_NeedsFrameRadius()
        {
            Assert.IsTrue(HasStopIssue("<Progress id='p' frameColor='#fff 70%,#000'/>"));
            Assert.IsFalse(HasStopIssue("<Progress id='p' frameRadius='4' frameColor='#fff 70%,#000'/>"));
        }

        [Test]
        public void Slider_MainRadius_DoesNotCoverTheFillLayer()
        {
            // radius shapes the groove, not the filled segment — fillColor still has no surface.
            Assert.IsTrue(HasStopIssue("<Slider id='s' radius='4' fillColor='#fff 70%,#000'/>"));
        }

        // ── inner layers that have no surface under any spelling ────────────────

        [Test]
        public void Toggle_CheckmarkColour_IsAlwaysReported()
        {
            Assert.IsTrue(HasStopIssue("<Toggle id='t' radius='6' checkmarkColor='#fff 70%,#000'/>"));
        }

        [Test]
        public void Dropdown_PopupColour_IsAlwaysReported()
        {
            Assert.IsTrue(HasStopIssue("<Dropdown id='d' radius='6' popupColor='#fff 70%,#000'/>"));
        }

        [Test]
        public void LabelColour_IsAlwaysReported()
        {
            Assert.IsTrue(HasStopIssue("<Btn id='b' radius='8' textColor='#fff 70%,#000'>ok</Btn>"));
        }

        // ── malformed stops belong to the other rule ────────────────────────────

        [Test]
        public void MalformedStop_IsAShapeError_NotANoSurfaceOne()
        {
            var issues = Walk("<Image id='g' color='#fff 70,#000'/>");
            Assert.IsTrue(issues.Any(i => i.Code == ColorLiteralRules.GradientMalformedCode));
            Assert.IsFalse(issues.Any(i => i.Code == GradientStopRules.NoSurfaceCode),
                "an unparseable stop has no position to be wrong about");
        }

        [Test]
        public void StopsDoNotBreakTheHexCheck()
        {
            var issues = Walk("<Frame id='f' color='#4a6fa5 70%,#c9a227'/>");
            Assert.IsFalse(issues.Any(i => i.Code == ColorLiteralRules.ColorLiteralCode));
        }
    }
}
