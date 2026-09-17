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
        [TestCase("intensity='bright'")]
        [TestCase("intensity='0.5'")]
        [TestCase("intensity='NaN'")]
        [TestCase("haze='cloudy'")]
        [TestCase("haze='-4'")]
        [TestCase("hazeDrift='-1'")]
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

            Assert.IsTrue(Has(Walk("<Frame id='f' class='dim'/>", "<Style name='dim' intensity='0'/>"),
                StyleRules.ProceduralValueCode, "dim"));
        }

        // ---- intensity: not a glass parameter, and not for glass (spec 2026-09-12 §5.4) ----

        [Test]
        public void IntensityWithoutGlass_IsNotAGlassParam()
        {
            // The one thing GlassRules must NOT say about it: "add glass=\"true\"" would name the
            // opposite of the fix.
            var issues = Walk("<Frame id='f' color='#4f88ff' glow='12' intensity='3'/>");
            Assert.IsFalse(Has(issues, GlassRules.ParamWithoutGlassCode, "f"));
            Assert.IsFalse(Has(issues, GlassRules.IntensityOnGlassCode, "f"));
        }

        [TestCase("glass='true' intensity='3'")]
        [TestCase("glass.mobile='true' intensity='3'")]
        [TestCase("glass='true' intensity.mobile='3'")]
        public void IntensityOnGlass_IsFlagged(string attrs)
        {
            // Glass paints the backdrop, which is not light the surface emits: the runtime zeroes
            // the value, so the author has to hear about it here.
            var issues = Walk($"<Frame id='f' {attrs}/>");
            Assert.IsTrue(Has(issues, GlassRules.IntensityOnGlassCode, "f"));
            StringAssert.Contains("intensity", issues.First(i => i.Code == GlassRules.IntensityOnGlassCode).Message);
        }

        [Test]
        public void IntensityOnAWeldContainer_IsFlagged()
        {
            // A weld container draws the fused glass pane — glass through and through.
            Assert.IsTrue(Has(Walk(@"<Frame id='g' weld='10' intensity='3'>
      <Frame id='a' glass='true'/>
      <Frame id='b' glass='true'/>
    </Frame>"), GlassRules.IntensityOnGlassCode, "g"));
        }

        [Test]
        public void IntensityOnAWeldedBlock_IsFlaggedAsGlass_NotAsPlacement()
        {
            // It is not a group parameter the container could hold either: nothing glass can take
            // it, so the diagnostic is the glass one, once.
            var issues = Walk(@"<Frame id='g' weld='10'>
      <Frame id='a' glass='true' intensity='3'/>
      <Frame id='b' glass='true'/>
    </Frame>");
            Assert.IsTrue(Has(issues, GlassRules.IntensityOnGlassCode, "a"));
            Assert.IsFalse(Has(issues, GlassRules.WeldParamPlacementCode, "a"));
        }

        // ---- haze: not a glass parameter, and not for glass (spec 2026-09-17 haze §5.4) ----

        [Test]
        public void HazeWithoutGlass_IsNotAGlassParam()
        {
            // Same trap as intensity: "add glass=\"true\"" would name the opposite of the fix.
            var issues = Walk("<Frame id='f' color='#101828' haze='40' hazeColor='cyan' hazeDrift='6'/>");
            Assert.IsFalse(Has(issues, GlassRules.ParamWithoutGlassCode, "f"));
            Assert.IsFalse(Has(issues, GlassRules.HazeOnGlassCode, "f"));
        }

        [TestCase("glass='true' haze='40'")]
        [TestCase("glass.mobile='true' haze='40'")]
        [TestCase("glass='true' haze.mobile='40'")]
        public void HazeOnGlass_IsFlagged(string attrs)
        {
            // Glass paints the backdrop; the runtime zeroes the fog there (H-D4), so the author has
            // to hear about it here.
            var issues = Walk($"<Frame id='f' {attrs}/>");
            Assert.IsTrue(Has(issues, GlassRules.HazeOnGlassCode, "f"));
            StringAssert.Contains("haze", issues.First(i => i.Code == GlassRules.HazeOnGlassCode).Message);
        }

        [Test]
        public void HazeColorAloneOnGlass_IsNotFlagged()
        {
            // Only `haze` switches the fog on. A theme may hand hazeColor to every surface and
            // switch the fog on per control; the glass ones must not turn into noise.
            Assert.IsFalse(Has(Walk("<Frame id='f' glass='true' hazeColor='cyan'/>"),
                GlassRules.HazeOnGlassCode, "f"));
        }

        [Test]
        public void HazeOnAWeldContainer_IsFlagged()
        {
            Assert.IsTrue(Has(Walk(@"<Frame id='g' weld='10' haze='40'>
      <Frame id='a' glass='true'/>
      <Frame id='b' glass='true'/>
    </Frame>"), GlassRules.HazeOnGlassCode, "g"));
        }

        [Test]
        public void HazeOnAWeldedBlock_IsFlaggedAsGlass_NotAsPlacement()
        {
            var issues = Walk(@"<Frame id='g' weld='10'>
      <Frame id='a' glass='true' haze='40'/>
      <Frame id='b' glass='true'/>
    </Frame>");
            Assert.IsTrue(Has(issues, GlassRules.HazeOnGlassCode, "a"));
            Assert.IsFalse(Has(issues, GlassRules.WeldParamPlacementCode, "a"));
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

        // ---- seam: the width of the thickness step ----

        [Test]
        public void SeamWithoutWeld_IsFlagged()
        {
            // Only the group shader reads it: a single pane has no second block to be thicker than.
            Assert.IsTrue(Has(Walk("<Frame id='f' glass='true' seam='4'/>"),
                GlassRules.SeamWithoutWeldCode, "f"));
        }

        [Test]
        public void SeamWithoutWeld_IsNotAlsoReportedAsAGlassParam()
        {
            // One attribute, one diagnostic: PUI-GLASS-PARAM-NO-GLASS would name the wrong fix
            // (adding glass="true" does not make seam work — adding weld does).
            var issues = Walk("<Frame id='f' seam='4'/>");
            Assert.IsTrue(Has(issues, GlassRules.SeamWithoutWeldCode, "f"));
            Assert.IsFalse(Has(issues, GlassRules.ParamWithoutGlassCode, "f"));
        }

        [Test]
        public void SeamOnAWeldContainer_IsFine()
        {
            Assert.IsFalse(Has(Walk(@"<Frame id='g' weld='10' seam='4'>
      <Frame id='a' glass='true' depth='6'/>
      <Frame id='b' glass='true' depth='2'/>
    </Frame>"), GlassRules.SeamWithoutWeldCode));
        }

        [Test]
        public void SeamOnAVariantOnlyWeldContainer_IsFine()
        {
            // weld and seam can arrive from two different theme packs, exactly as shape and weld do.
            Assert.IsFalse(Has(Walk(@"<Frame id='g' weld.mobile='10' seam='4'>
      <Frame id='a' glass='true' depth='6'/>
      <Frame id='b' glass='true' depth='2'/>
    </Frame>"), GlassRules.SeamWithoutWeldCode));
        }

        [Test]
        public void SeamOnABlock_IsFlaggedAsMisplaced()
        {
            // One continuous pane is welded one way — seam is the container's.
            Assert.IsTrue(Has(Walk(@"<Frame id='g' weld='10'>
      <Frame id='a' glass='true' seam='4'/>
      <Frame id='b' glass='true'/>
    </Frame>"), GlassRules.WeldParamPlacementCode, "a"));
        }

        [TestCase("seam='wide'")]
        [TestCase("seam='NaN'")]
        public void BadSeamValues_AreFlagged(string attr)
        {
            Assert.IsTrue(Has(Walk($@"<Frame id='g' weld='10' {attr}>
      <Frame id='a' glass='true'/>
      <Frame id='b' glass='true'/>
    </Frame>"), StyleRules.ProceduralValueCode, "g"));
        }

        [Test]
        public void NegativeSeam_IsFine()
        {
            // The sign picks which side of the raised block's contour the step's ramp falls on.
            Assert.IsFalse(Has(Walk(@"<Frame id='g' weld='10' seam='-4'>
      <Frame id='a' glass='true' depth='6'/>
      <Frame id='b' glass='true' depth='2'/>
    </Frame>"), StyleRules.ProceduralValueCode));
        }
    }
}
