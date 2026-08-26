using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <c>PUI-MASK-FRAME-SELF</c> narrowed to what it actually means (spec §9.4).
    ///
    /// <para>The rule was written when <c>&lt;Frame&gt;</c> had no <c>Graphic</c> at all, so its
    /// message — "requires an Image graphic … but Frame has none" — was simply true. A Frame that
    /// writes a procedural visual attribute now grows a <c>ProceduralPanel</c>, and uGUI's
    /// <c>Mask</c> asks for a <c>Graphic</c>, not an <c>Image</c>. So that Frame CAN mask, and
    /// rounded avatars / rounded scroll areas stop being impossible.</para>
    ///
    /// <para>Two structures still cannot, and both fail silently — the reason they are errors rather
    /// than documentation: a bare Frame has no Graphic, and a <c>weld</c> carrier keeps its fused
    /// pane on a <c>GlassWeld</c> child, leaving the carrier itself graphic-less.</para>
    /// </summary>
    public class MaskProceduralRulesTests
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

        private static bool Has(List<LintIssue> issues, string code) => issues.Any(i => i.Code == code);

        // ---- the narrowing ----

        [Test]
        public void MaskSelf_OnAProceduralFrame_IsAllowed()
        {
            Assert.IsFalse(
                Has(Walk("<Frame id='f' radius='16' color='#333' mask='self'/>"),
                    MaskAttributeRules.FrameSelfCode),
                "a procedural Frame has a Graphic of its own, which is all uGUI's Mask asks for");
        }

        [Test]
        public void MaskSelf_OnAFrameWithOnlyAColor_IsAllowed()
        {
            // `color` alone attaches the panel (Frame.Color -> Panel.SetFill), so it counts.
            Assert.IsFalse(
                Has(Walk("<Frame id='f' color='#333' mask='self'/>"), MaskAttributeRules.FrameSelfCode));
        }

        [Test]
        public void MaskSelf_OnAGlassFrame_IsAllowed()
        {
            Assert.IsFalse(
                Has(Walk("<Frame id='f' glass='true' radius='12' mask='self'/>"),
                    MaskAttributeRules.FrameSelfCode));
        }

        [Test]
        public void MaskSelf_OnABareFrame_StillReports()
        {
            Assert.IsTrue(
                Has(Walk("<Frame id='f' mask='self'/>"), MaskAttributeRules.FrameSelfCode),
                "nothing draws, so there is no Graphic and the clip silently does nothing");
        }

        // ---- class= carries the procedural attributes just as often as the node does ----

        [Test]
        public void MaskSelf_WithTheShapeComingFromAClass_IsAllowed()
        {
            Assert.IsFalse(
                Has(Walk("<Frame id='f' class='card' mask='self'/>",
                         "<Style name='card' radius='16' color='#333'/>"),
                    MaskAttributeRules.FrameSelfCode),
                "skins carry the shape in a <Style>; a rule blind to that reports correct XML broken");
        }

        [Test]
        public void MaskSelf_WithAnUnresolvableClass_StaysQuiet()
        {
            Assert.IsFalse(
                Has(Walk("<Frame id='f' class='from-a-commons-library' mask='self'/>"),
                    MaskAttributeRules.FrameSelfCode),
                "the single-file CLI cannot see an imported style, so it must not guess");
        }

        // ---- weld: the fused pane is on a child, so the carrier cannot mask with it ----

        [Test]
        public void MaskSelf_OnAWeldCarrier_Reports()
        {
            var issues = Walk(
                @"<Frame id='f' weld='16' mask='self'>
                    <Frame glass='true' radius='8'/>
                    <Frame glass='true' radius='8'/>
                  </Frame>");

            Assert.IsTrue(Has(issues, MaskAttributeRules.WeldSelfCode),
                "GlassGroupPanel.Attach puts the fused pane on a GlassWeld child; the carrier keeps "
                + "no Graphic of its own");
            Assert.IsFalse(Has(issues, MaskAttributeRules.FrameSelfCode),
                "one diagnosis, not two — the weld message is the specific one");
        }

        [Test]
        public void MaskSelf_OnAWeldCarrierThatAlsoDraws_StillReports()
        {
            // radius= attaches a panel on the carrier, but the group suppresses it while welding
            // (SetSuppressed -> no geometry, no material), so it is still useless as a mask source.
            Assert.IsTrue(
                Has(Walk(@"<Frame id='f' weld='16' radius='12' mask='self'>
                             <Frame glass='true' radius='8'/>
                             <Frame glass='true' radius='8'/>
                           </Frame>"),
                    MaskAttributeRules.WeldSelfCode));
        }

        // ---- the neighbouring rules must keep working on a procedural Frame ----

        [Test]
        public void MaskPadding_OnAProceduralMaskSelfFrame_StillReports()
        {
            Assert.IsTrue(
                Has(Walk("<Frame id='f' radius='16' mask='self' maskPadding='8'/>"),
                    MaskAttributeRules.PaddingNoRectCode),
                "a stencil mask has no padding concept whatever draws it");
        }

        [Test]
        public void ShowMask_WithoutMaskSelf_Reports()
        {
            Assert.IsTrue(
                Has(Walk("<Frame id='f' radius='16' mask='rect' showMask='false'/>"),
                    MaskAttributeRules.ShowMaskNoSelfCode),
                "RectMask2D has no graphic to show or hide — same as on <Image>");
        }

        [Test]
        public void ShowMask_WithMaskSelf_IsAllowed()
        {
            Assert.IsFalse(
                Has(Walk("<Frame id='f' radius='16' mask='self' showMask='false'/>"),
                    MaskAttributeRules.ShowMaskNoSelfCode));
        }

        [Test]
        public void MaskBogus_OnAProceduralFrame_StillReportsTheValue()
        {
            Assert.IsTrue(
                Has(Walk("<Frame id='f' radius='16' mask='circle'/>"), MaskAttributeRules.ValueCode));
        }
    }
}
