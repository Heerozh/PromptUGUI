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

        // ── common libraries (2026-09-18 commons-settings spec §4.7) ───────────────────────────
        //
        // A common library is the <Import> every document implicitly has. The runtime folds it into
        // the commons pool (DocumentLoader → MergeCommons) BEFORE expansion, so a class= or a
        // namespaced tag that lives only there resolves fine at UI.Open() — and a linter that does
        // not know the list reports every such reference as PUI-EXPAND. That false positive is what
        // made the host copy its theme's style pack into local files.

        /// <summary>A parsed document tagged with the src it is looked up by.</summary>
        private static (string Src, UIDocument Doc) Parse(string body, string src) =>
            (src, UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>", src));

        private static List<LintIssue> WalkWithCommons(
            (string Src, UIDocument Doc) entry, IReadOnlyList<ImportRef> commons,
            params (string Src, UIDocument Doc)[] libraries)
        {
            var bySrc = new Dictionary<string, UIDocument>();
            foreach (var lib in libraries) bySrc[lib.Src] = lib.Doc;
            return DocumentLinter
                .Walk(entry.Doc, entry.Src, src => bySrc.TryGetValue(src, out var d) ? d : null, commons)
                .ToList();
        }

        private static ImportRef Commons(string src, string ns = null) => new ImportRef(src, ns);

        [Test]
        public void StyleDeclaredOnlyInACommonLibrary_IsNotAnExpansionFailure()
        {
            var commons = Parse("<Style name='badge' sprite='ui:pill'/>", "commons.ui");
            var entry = Parse(
                "<Screen name='S'><VStack id='v' class='badge' anchor='stretch'/></Screen>", "main.ui");

            var issues = WalkWithCommons(entry, new[] { Commons("commons.ui") }, commons);

            Assert.IsFalse(issues.Any(i => i.Code == DocumentLinter.ExpansionCode),
                "the runtime merges the commons pool before expansion; so must the linter");
            Assert.IsTrue(issues.Any(i => i.Code == PureContainerVisualAttrRules.VisualAttrCode && i.Id == "v"),
                "…and the expanded pass then sees the sprite the commons class supplies");
        }

        [Test]
        public void NamespacedCommonsTemplate_FindingIsAttributedToTheLibrary()
        {
            var lib = Parse("<Template name='Card'><Frame id='card' mask='self'/></Template>", "lib.ui");
            var entry = Parse("<Screen name='S'><ui.Card/></Screen>", "main.ui");

            var issues = WalkWithCommons(entry, new[] { Commons("lib.ui", "ui") }, lib);

            Assert.IsFalse(issues.Any(i => i.Code == DocumentLinter.ExpansionCode),
                "<ui.Card/> is what a library loaded with as='ui' is invoked as");
            var issue = issues.Single(i => i.Code == MaskAttributeRules.FrameSelfCode && i.Id == "card");
            Assert.AreEqual("lib.ui", issue.Origin, "the defect was written in the library");
            StringAssert.StartsWith("main.ui:", issue.Via, "…and reached through this invocation");
        }

        [Test]
        public void ThemeInACommonLibrary_SuppliesTheBaseline_AndGetsItsOwnPass()
        {
            var theme = Parse(@"
                <Style name='boxed' color='#112233'/>
                <Theme name='plain'><Style name='boxed' color='#fff'/></Theme>
                <Theme name='sprited'><Style name='boxed' color='#fff' sprite='ui:panel'/></Theme>",
                "theme.ui");
            var entry = Parse(
                "<Screen name='S'><VStack id='v' class='boxed' anchor='stretch'/></Screen>", "main.ui");

            var issues = WalkWithCommons(entry, new[] { Commons("theme.ui") }, theme);

            Assert.IsFalse(issues.Any(i => i.Code == ThemeStyleRules.NoBaselineCode),
                "the global <Style> the theme packs rest on is declared in the same library");
            Assert.IsTrue(issues.Any(i => i.Code == PureContainerVisualAttrRules.VisualAttrCode && i.Id == "v"),
                "'sprite' only exists under the 'sprited' skin — the per-theme pass must run for commons themes too");
        }

        [Test]
        public void ThemeInACommonLibrary_WithNoBaselineAnywhere_IsStillReported()
        {
            var theme = Parse("<Theme name='dark'><Style name='orphan' color='#000'/></Theme>", "theme.ui");
            var entry = Parse("<Screen name='S'><Frame id='f'/></Screen>", "main.ui");

            var issues = WalkWithCommons(entry, new[] { Commons("theme.ui") }, theme);

            Assert.IsTrue(issues.Any(i => i.Code == ThemeStyleRules.NoBaselineCode && i.Id == "orphan"));
        }

        [Test]
        public void EntryRedeclaringACommonsStyle_IsAnExpansionFailure_WordedLikeTheRuntime()
        {
            var commons = Parse("<Style name='badge' sprite='ui:pill'/>", "commons.ui");
            var entry = Parse(@"
                <Style name='badge' sprite='ui:other'/>
                <Screen name='S'><Frame id='f' class='badge'/></Screen>", "main.ui");

            var issues = WalkWithCommons(entry, new[] { Commons("commons.ui") }, commons);

            var issue = issues.Single(i => i.Code == DocumentLinter.ExpansionCode);
            StringAssert.Contains("conflicts with commons pool", issue.Message);
        }

        [Test]
        public void CommonLibraryDeclaringAScreen_IsAnExpansionFailure()
        {
            var commons = Parse("<Screen name='X'><Frame id='x'/></Screen>", "commons.ui");
            var entry = Parse("<Screen name='S'><Frame id='f'/></Screen>", "main.ui");

            var issues = WalkWithCommons(entry, new[] { Commons("commons.ui") }, commons);

            var issue = issues.Single(i => i.Code == DocumentLinter.ExpansionCode);
            StringAssert.Contains("<Screen> not allowed", issue.Message);
        }

        [Test]
        public void ThemeDeclaredInBothCommonsAndEntry_IsAnExpansionFailure()
        {
            var commons = Parse("<Theme name='dark'><Color name='bg' value='#000'/></Theme>", "commons.ui");
            var entry = Parse(@"
                <Theme name='dark'><Color name='bg' value='#111'/></Theme>
                <Screen name='S'><Frame id='f'/></Screen>", "main.ui");

            var issues = WalkWithCommons(entry, new[] { Commons("commons.ui") }, commons);

            var issue = issues.Single(i => i.Code == DocumentLinter.ExpansionCode);
            StringAssert.Contains("duplicate <Theme name=\"dark\">", issue.Message);
        }

        // Same policy as an unresolvable <Import>: no closure, no expanded pass, no false positive.
        [Test]
        public void CommonsWithoutALookup_SkipExpandedPass_ButKeepRawRules()
        {
            var entry = Parse(@"
                <Screen name='S'>
                  <Frame id='f' mask='self'/>
                  <VStack id='v' class='badge' anchor='stretch'/>
                </Screen>", "main.ui");

            var issues = DocumentLinter
                .Walk(entry.Doc, entry.Src, imports: null, commons: new[] { Commons("commons.ui") })
                .ToList();

            Assert.IsTrue(issues.Any(i => i.Code == MaskAttributeRules.FrameSelfCode));
            Assert.IsFalse(issues.Any(i => i.Code == DocumentLinter.ExpansionCode),
                "class='badge' may well live in the library the caller could not read");
        }
    }
}
