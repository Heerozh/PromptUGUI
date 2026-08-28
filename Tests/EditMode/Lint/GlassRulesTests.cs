using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// Every failure mode here is silent at runtime — an attribute nothing reads — so the CLI is
    /// where an author finds out.
    /// </summary>
    public class GlassRulesTests
    {
        private static System.Collections.Generic.List<LintIssue> Walk(string body, string top = "")
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{top}
  <Screen name='S'>
{body}
  </Screen>
</PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        private static bool Has(System.Collections.Generic.List<LintIssue> issues, string code,
                                string id = null)
            => issues.Any(i => i.Code == code && (id == null || i.Id == id));

        // ---- glass parameters without glass mode ----

        [TestCase("frost='0.8'")]
        [TestCase("depth='6'")]
        [TestCase("dispersion='0.2'")]
        [TestCase("lightAngle='45'")]
        [TestCase("lightIntensity='0.3'")]
        [TestCase("saturation='1.4'")]
        [TestCase("noise='0.1'")]
        public void GlassParamWithoutGlassMode_IsFlagged(string attr)
        {
            Assert.IsTrue(Has(Walk($"<Frame id='f' {attr}/>"),
                GlassRules.ParamWithoutGlassCode, "f"));
        }

        [Test]
        public void GlassParamWithGlassMode_IsFine()
        {
            Assert.IsFalse(Has(Walk("<Frame id='f' glass='true' frost='0.8'/>"),
                GlassRules.ParamWithoutGlassCode));
        }

        [Test]
        public void VariantOnlyGlass_IsNotFlagged()
        {
            // glass.mobile="true" is a legitimate way to make a panel glass in one layout only;
            // flagging its parameters would fire on correct documents.
            Assert.IsFalse(Has(Walk("<Frame id='f' glass.mobile='true' frost='0.8'/>"),
                GlassRules.ParamWithoutGlassCode));
        }

        [Test]
        public void GlassParamsOnAWeldContainer_AreFine()
        {
            // The container is not glass itself but owns the group-level parameters.
            var issues = Walk(@"<Frame id='g' weld='10' frost='0.8' lightAngle='30'>
      <Frame id='a' glass='true'/>
      <Frame id='b' glass='true'/>
    </Frame>");
            Assert.IsFalse(Has(issues, GlassRules.ParamWithoutGlassCode));
        }

        // ---- weld structure ----

        [Test]
        public void WeldAndGlassOnTheSameNode_IsFlagged()
        {
            Assert.IsTrue(Has(Walk(@"<Frame id='g' weld='10' glass='true'>
      <Frame id='a' glass='true'/>
      <Frame id='b' glass='true'/>
    </Frame>"), GlassRules.WeldSelfCode, "g"));
        }

        [TestCase(0)]
        [TestCase(1)]
        public void WeldWithFewerThanTwoBlocks_IsFlagged(int blocks)
        {
            var children = string.Concat(Enumerable.Range(0, blocks)
                .Select(i => $"<Frame id='b{i}' glass='true'/>"));
            Assert.IsTrue(Has(Walk($"<Frame id='g' weld='10'>{children}</Frame>"),
                GlassRules.WeldMembersCode, "g"));
        }

        [Test]
        public void WeldWithTwoBlocks_IsFine()
        {
            Assert.IsFalse(Has(Walk(@"<Frame id='g' weld='10'>
      <Frame id='a' glass='true'/>
      <Frame id='b' glass='true'/>
    </Frame>"), GlassRules.WeldMembersCode));
        }

        [Test]
        public void WeldBeyondTheShaderLimit_IsFlagged()
        {
            var children = string.Concat(Enumerable.Range(0, GlassRules.MaxWeldMembers + 1)
                .Select(i => $"<Frame id='b{i}' glass='true'/>"));
            Assert.IsTrue(Has(Walk($"<Frame id='g' weld='10'>{children}</Frame>"),
                GlassRules.WeldMembersCode, "g"));
        }

        // ---- parameter placement across the group ----

        [TestCase("depth='6'")]
        [TestCase("color='#fff'")]
        [TestCase("radius='16'")]
        public void PerBlockParamOnTheContainer_IsFlagged(string attr)
        {
            Assert.IsTrue(Has(Walk($@"<Frame id='g' weld='10' {attr}>
      <Frame id='a' glass='true'/>
      <Frame id='b' glass='true'/>
    </Frame>"), GlassRules.WeldParamPlacementCode, "g"));
        }

        [TestCase("frost='0.8'")]
        [TestCase("lightAngle='30'")]
        [TestCase("saturation='1.4'")]
        [TestCase("innerGlow='8'")]
        [TestCase("innerGlowColor='#fff'")]
        public void GroupParamOnABlock_IsFlagged(string attr)
        {
            // Two halves of one continuous pane cannot be frosted differently or lit from different
            // angles — the container owns those.
            Assert.IsTrue(Has(Walk($@"<Frame id='g' weld='10'>
      <Frame id='a' glass='true' {attr}/>
      <Frame id='b' glass='true'/>
    </Frame>"), GlassRules.WeldParamPlacementCode, "a"));
        }

        [Test]
        public void PerBlockParamOnABlock_IsFine()
        {
            Assert.IsFalse(Has(Walk(@"<Frame id='g' weld='10'>
      <Frame id='a' glass='true' depth='6' radius='16' color='#fff/0.1'/>
      <Frame id='b' glass='true' depth='2'/>
    </Frame>"), GlassRules.WeldParamPlacementCode));
        }

        // ---- value grammar (shared with the runtime setters) ----

        [TestCase("frost='2'")]
        [TestCase("frost='-1'")]
        [TestCase("frost='heavy'")]
        [TestCase("noise='5'")]
        [TestCase("depth='-3'")]
        [TestCase("innerGlow='soft'")]
        [TestCase("innerGlow='-2'")]
        public void BadValues_AreFlagged(string attr)
        {
            Assert.IsTrue(Has(Walk($"<Frame id='f' glass='true' {attr}/>"),
                StyleRules.ProceduralValueCode, "f"));
        }

        [Test]
        public void NonBooleanGlass_IsFlagged()
        {
            Assert.IsTrue(Has(Walk("<Frame id='f' glass='yes'/>"),
                StyleRules.ProceduralValueCode, "f"));
        }

        [Test]
        public void BadValuesInAStyle_AreFlaggedWhereTheyAreWritten()
        {
            // <Style> is not an ElementNode, so it needs its own pass or these never surface.
            var issues = Walk("<Frame id='f' class='glassy'/>",
                "<Style name='glassy' glass='true' frost='9'/>");
            Assert.IsTrue(Has(issues, StyleRules.ProceduralValueCode, "glassy"));
        }

        [Test]
        public void GlassParamsOnLayoutOnlyContainers_AreFlagged()
        {
            foreach (var tag in new[] { "VStack", "HStack", "Grid", "SafeArea" })
                Assert.IsTrue(
                    Has(Walk($"<{tag} id='c' glass='true' frost='0.5'/>"),
                        PureContainerVisualAttrRules.VisualAttrCode, "c"),
                    $"<{tag}> draws nothing, so glass on it is silently dropped");
        }

        // ---- corner treatments do not survive the fusion ----

        [TestCase("cut 16")]
        [TestCase("notch 8")]
        [TestCase("hexagon")]
        [TestCase("0, cut 12, 0, 0")]
        public void CornerTreatmentOnAWeldedBlock_IsFlagged(string radius)
        {
            // The group packs one radius vector per member and smooth-unions the fields, which
            // rounds every corner back off. The block still draws — as a plain round corner — so
            // this is a warning about a shape the author will not get, not a broken document.
            Assert.IsTrue(Has(Walk($@"<Frame id='g' weld='16'>
      <Frame id='a' glass='true' radius='{radius}'/>
      <Frame id='b' glass='true'/>
    </Frame>"), GlassRules.WeldCornerCode, "a"));
        }

        [Test]
        public void RoundRadiusOnAWeldedBlock_IsFine()
        {
            Assert.IsFalse(Has(Walk(@"<Frame id='g' weld='16'>
      <Frame id='a' glass='true' radius='12'/>
      <Frame id='b' glass='true' radius='pill'/>
    </Frame>"), GlassRules.WeldCornerCode));
        }

        [Test]
        public void CornerTreatmentOutsideAWeldGroup_IsFine()
        {
            Assert.IsFalse(Has(Walk("<Frame id='f' radius='cut 16'/>"), GlassRules.WeldCornerCode));
        }

        [Test]
        public void VariantOnlyCornerTreatmentOnAWeldedBlock_IsFlagged()
        {
            // Shape and weld can arrive from two different theme packs, so the pairing is at least
            // as likely to appear in one layout only.
            Assert.IsTrue(Has(Walk(@"<Frame id='g' weld='16'>
      <Frame id='a' glass='true' radius.mobile='cut 16'/>
      <Frame id='b' glass='true'/>
    </Frame>"), GlassRules.WeldCornerCode, "a"));
        }

        [Test]
        public void UnparseableRadiusOnAWeldedBlock_IsLeftToTheSyntaxRules()
        {
            Assert.IsFalse(Has(Walk(@"<Frame id='g' weld='16'>
      <Frame id='a' glass='true' radius='bevel 4'/>
      <Frame id='b' glass='true'/>
    </Frame>"), GlassRules.WeldCornerCode));
        }
    }
}
