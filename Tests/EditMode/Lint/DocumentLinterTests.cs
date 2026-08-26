using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <see cref="DocumentLinter"/> is the two-pass (raw + expanded) walk the UIXmlLint CLI runs.
    /// See the 2026-08-26 theme-driven-style spec §9: the runtime warning path has always seen the
    /// EXPANDED tree while the CLI only ever saw the raw IR, which is why rules that reason about a
    /// node's resolved configuration had to stay quiet whenever <c>class=</c> or a template was
    /// involved.
    /// </summary>
    public class DocumentLinterTests
    {
        private static UIDocument Parse(string body) =>
            UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>");

        private static List<LintIssue> Walk(string body) =>
            DocumentLinter.Walk(Parse(body)).ToList();

        // "Attribute A is set but the B it needs is missing" is the one rule shape that turns a
        // style-blind read into a FALSE POSITIVE — and the CLI turns a false positive into a non-zero
        // exit code. The raw pass sees no fillColor / no sprite because both arrive through class=,
        // and dedup then carries that verdict out even though the expanded pass disagrees. Rules of
        // this shape have to go through StyleAttributeView.
        [Test]
        public void ProgressWithItsFillColourInAClass_IsNotReportedAsUnfilled()
        {
            var issues = Walk(
                "<Style name='prog' fillColor='#58A63C'/>"
                + "<Screen name='S'><Progress id='p' class='prog' height='16' value='0.4'/></Screen>");

            CollectionAssert.IsEmpty(
                issues.Where(i => i.Code == ProgressAttributeRules.NoFillCode).ToList(),
                "the class supplies fillColor, so there is nothing wrong with this Progress");
        }

        [Test]
        public void MaskSelfWithItsSpriteInAClass_IsNotReportedAsSpriteless()
        {
            var issues = Walk(
                "<Style name='pane' sprite='ui:panel'/>"
                + "<Screen name='S'><Image id='m' class='pane' mask='self' width='40' height='40'/></Screen>");

            CollectionAssert.IsEmpty(
                issues.Where(i => i.Code == MaskAttributeRules.SelfNoSpriteCode).ToList(),
                "the class supplies the sprite the stencil mask needs");
        }

        // …and the rules must still fire when the attribute really is missing everywhere, class or
        // not. Otherwise "no false positives" would just mean "no rule".
        [Test]
        public void TheSameRulesStillFire_WhenNoClassSuppliesTheMissingAttribute()
        {
            var progress = Walk(
                "<Style name='prog' bgColor='#888'/>"
                + "<Screen name='S'><Progress id='p' class='prog' height='16' value='0.4'/></Screen>");
            Assert.AreEqual(1, progress.Count(i => i.Code == ProgressAttributeRules.NoFillCode));

            var mask = Walk(
                "<Style name='pane' color='#fff'/>"
                + "<Screen name='S'><Image id='m' class='pane' mask='self' width='40' height='40'/></Screen>");
            Assert.AreEqual(1, mask.Count(i => i.Code == MaskAttributeRules.SelfNoSpriteCode));
        }

        // A rule that reads the node's written attributes is unaffected by the second pass — and
        // must not start reporting twice now that the tree is walked twice.
        [Test]
        public void RawIssue_IsReportedExactlyOnce_ThoughBothPassesSeeIt()
        {
            var issues = Walk("<Screen name='S'><Frame id='f' mask='self'/></Screen>");

            Assert.AreEqual(1, issues.Count(i => i.Code == MaskAttributeRules.FrameSelfCode),
                "mask='self' survives expansion verbatim, so both passes find it — dedup must collapse them");
        }

        // The motivating case. PureContainerVisualAttrRules reads n.Attributes; before expansion the
        // sprite lives in a <Style> the node merely references, so the raw pass is blind to it.
        [Test]
        public void ClassSuppliedAttribute_IsOnlyVisibleAfterExpansion()
        {
            const string body = @"
                <Style name='boxed' sprite='ui:panel'/>
                <Screen name='S'><VStack id='v' class='boxed' anchor='stretch'/></Screen>";

            var rawOnly = IRWalker.Walk(Parse(body)).ToList();
            Assert.IsFalse(rawOnly.Any(i => i.Code == PureContainerVisualAttrRules.VisualAttrCode),
                "guard: the raw walk cannot see an attribute that arrives through class=");

            var both = Walk(body);
            Assert.IsTrue(both.Any(i => i.Code == PureContainerVisualAttrRules.VisualAttrCode && i.Id == "v"),
                "the expanded pass merges the style pack onto the node, making 'sprite' on a pure "
                + "container visible");
        }

        [Test]
        public void UnknownStyleName_IsReportedAsAnExpansionFailure()
        {
            var issues = Walk("<Screen name='S'><Frame id='f' class='does-not-exist'/></Screen>");

            var expansion = issues.SingleOrDefault(i => i.Code == DocumentLinter.ExpansionCode);
            Assert.IsNotNull(expansion.Message, "an unresolvable class name must not wait until UI.Open()");
            StringAssert.Contains("does-not-exist", expansion.Message);
        }

        [Test]
        public void ImportedStyle_IsResolvedThroughTheLookup()
        {
            var lib = Parse("<Style name='boxed' sprite='ui:panel'/>");
            var entry = Parse(@"
                <Import src='skin.ui'/>
                <Screen name='S'><VStack id='v' class='boxed' anchor='stretch'/></Screen>");

            var issues = DocumentLinter
                .Walk(entry, "main.ui", src => src == "skin.ui" ? lib : null)
                .ToList();

            Assert.IsTrue(issues.Any(i => i.Code == PureContainerVisualAttrRules.VisualAttrCode && i.Id == "v"),
                "following <Import> is what retires StyleAttributeView.IsUncertain's silence");
        }

        // A project whose commons come from Addressables has no filesystem closure to hand over.
        // That must cost coverage, never turn a clean document into a failure.
        [Test]
        public void UnresolvableImports_SkipExpandedPass_ButKeepRawRules()
        {
            var entry = Parse(@"
                <Import src='skin.ui'/>
                <Screen name='S'>
                  <Frame id='f' mask='self'/>
                  <VStack id='v' class='boxed' anchor='stretch'/>
                </Screen>");

            var issues = DocumentLinter.Walk(entry, "main.ui", imports: null).ToList();

            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.FrameSelfCode),
                "raw rules still apply when the closure is unavailable");
            Assert.IsFalse(issues.Any(i => i.Code == DocumentLinter.ExpansionCode),
                "class='boxed' names a style only the unseen library declares — reporting it as an "
                + "expansion failure would fail every Addressables-backed project");
        }

        // Guards the accumulation bug this class was written for: an expanded-only finding used to
        // be printed but not counted, so the CLI exited 0 on a failing document.
        [Test]
        public void ExpandedOnlyFindings_AreEnumerated_NotJustPrinted()
        {
            var issues = Walk(@"
                <Style name='boxed' sprite='ui:panel'/>
                <Screen name='S'><VStack id='v' class='boxed' anchor='stretch'/></Screen>");

            Assert.AreEqual(1, issues.Count,
                "the caller counts what it enumerates; an expanded-only issue must be in the sequence");
        }
    }
}
