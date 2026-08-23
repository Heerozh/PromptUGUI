using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// The glass rules decide whether a node is glass, and these attribute values arrive just as
    /// often through <c>&lt;Style&gt;</c> / <c>class=</c> as inline — that is the pattern the shipped
    /// skin sample establishes and the one glass.md tells authors to use. Reading only the node's own
    /// attributes turns every styled layout into a hard CLI error (the CLI exits non-zero, and
    /// CLAUDE.md has authors run it after every edit), which is worse than the silent attribute the
    /// rules exist to prevent.
    ///
    /// Where a class cannot be resolved at all — an imported commons library the CLI never sees —
    /// nothing can be proven either way, so the structural rules stay quiet rather than guess.
    /// </summary>
    public class GlassRulesStyleAwareTests
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

        private static bool Has(System.Collections.Generic.List<LintIssue> issues, string code)
            => issues.Any(i => i.Code == code);

        private const string GlassStyle = "<Style name='card' glass='true' radius='16'/>";

        // ---- glass arriving through a class ----

        [Test]
        public void GlassFromAClass_SatisfiesTheParamRule()
        {
            Assert.IsFalse(
                Has(Walk("<Frame id='f' class='card' frost='0.8'/>", GlassStyle),
                    GlassRules.ParamWithoutGlassCode),
                "glass=\"true\" reached the node through the style; the parameter is not ignored");
        }

        [Test]
        public void GlassParamFromAClass_OnANonGlassNode_IsStillFlagged()
        {
            Assert.IsTrue(
                Has(Walk("<Frame id='f' class='frosty'/>", "<Style name='frosty' frost='0.8'/>"),
                    GlassRules.ParamWithoutGlassCode),
                "a style that sets frost without glass leaves the parameter just as dead");
        }

        [Test]
        public void WeldMembersFromClasses_AreCounted()
        {
            Assert.IsFalse(
                Has(Walk(@"<Frame id='g' weld='10'>
    <Frame id='a' class='card'/>
    <Frame id='b' class='card'/>
  </Frame>", GlassStyle),
                    GlassRules.WeldMembersCode),
                "two styled glass children are a valid group");
        }

        [Test]
        public void WeldSelfFromAClass_IsFlagged()
        {
            Assert.IsTrue(
                Has(Walk("<Frame id='g' class='card' weld='10'><Frame id='a' class='card'/>" +
                         "<Frame id='b' class='card'/></Frame>", GlassStyle),
                    GlassRules.WeldSelfCode),
                "the carrier is glass through its class — same contradiction as writing it inline");
        }

        [Test]
        public void GroupParamOnAStyledMember_IsFlagged()
        {
            Assert.IsTrue(
                Has(Walk(@"<Frame id='g' weld='10'>
    <Frame id='a' class='card' frost='0.9'/>
    <Frame id='b' class='card'/>
  </Frame>", GlassStyle),
                    GlassRules.WeldParamPlacementCode),
                "frost is group-level wherever the glass flag came from");
        }

        // ---- inline still wins ----

        [Test]
        public void InlineGlassFalse_MasksTheClassValue()
        {
            // Merge precedence is inline > class, atomically per attribute name (StyleMerger), so a
            // carrier that opts out inline is not glass however its class is written — and the
            // weld-on-glass contradiction must not be reported.
            Assert.IsFalse(
                Has(Walk(@"<Frame id='g' class='card' glass='false' weld='10'>
    <Frame id='a' class='card'/>
    <Frame id='b' class='card'/>
  </Frame>", GlassStyle),
                    GlassRules.WeldSelfCode),
                "inline glass=\"false\" wins over the class, so the carrier is not glass");
        }

        // ---- unresolvable classes ----

        [Test]
        public void UnknownClass_SilencesTheStructuralRules()
        {
            // The style may live in an imported commons library the single-file CLI cannot see.
            // Same reasoning as StyleRules deliberately having no "unknown class name" check.
            var issues = Walk("<Frame id='f' class='from-commons' frost='0.8'/>");
            Assert.IsFalse(Has(issues, GlassRules.ParamWithoutGlassCode));
        }

        [Test]
        public void UnknownClassOnAWeldChild_SilencesTheMemberCount()
        {
            var issues = Walk(@"<Frame id='g' weld='10'>
    <Frame id='a' class='from-commons'/>
    <Frame id='b' class='from-commons'/>
  </Frame>");
            Assert.IsFalse(Has(issues, GlassRules.WeldMembersCode));
        }

        [Test]
        public void TemplatedClassValue_SilencesTheStructuralRules()
        {
            // class="{{skin}}" is resolved at expansion time; nothing is knowable here.
            var issues = Walk("<Frame id='f' class='{{skin}}' frost='0.8'/>", GlassStyle);
            Assert.IsFalse(Has(issues, GlassRules.ParamWithoutGlassCode));
        }

        // ---- border / glow placement inside a group ----

        [TestCase("borderWidth='1'")]
        [TestCase("borderColor='#fff'")]
        [TestCase("glow='8'")]
        [TestCase("glowColor='#fff'")]
        public void BorderOrGlowOnAWeldedMember_IsFlagged(string attr)
        {
            // A welded member is suppressed, so its border silently vanishes — and a per-block
            // border would draw exactly the dividing line the weld exists to remove. glass.md
            // assigns these to the container; the linter has to say so.
            Assert.IsTrue(
                Has(Walk($@"<Frame id='g' weld='10'>
    <Frame id='a' glass='true' {attr}/>
    <Frame id='b' glass='true'/>
  </Frame>"), GlassRules.WeldParamPlacementCode),
                $"{attr} on a welded member is silently dropped");
        }

        [Test]
        public void BorderOnTheWeldContainer_IsFine()
        {
            Assert.IsFalse(
                Has(Walk(@"<Frame id='g' weld='10' borderWidth='1' glow='6'>
    <Frame id='a' glass='true'/>
    <Frame id='b' glass='true'/>
  </Frame>"), GlassRules.WeldParamPlacementCode),
                "the fused outline's border belongs on the carrier");
        }
    }
}
