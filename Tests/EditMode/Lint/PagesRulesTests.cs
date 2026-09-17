using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <c>&lt;Pages&gt;</c>'s structural lint (spec 2026-09-17-pages-design §4.5) — through
    /// <see cref="IRWalker"/>, which is what the CLI and the runtime warning path share.
    /// </summary>
    public class PagesRulesTests
    {
        private static List<LintIssue> Walk(string body, string top = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + top
                      + "<Screen name='S'>" + body + "</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        private static List<LintIssue> Of(List<LintIssue> issues, string code) =>
            issues.Where(i => i.Code == code).ToList();

        private static List<LintIssue> PagesIssues(List<LintIssue> issues) =>
            issues.Where(i => i.Code.StartsWith("PUI-PAGES-")).ToList();

        [Test]
        public void Clean_pages_has_no_issues()
        {
            var issues = Walk("<Pages id='p' selected='a'><Frame id='a'/><Frame id='b'/></Pages>");
            Assert.IsEmpty(PagesIssues(issues));
        }

        [Test]
        public void A_template_invocation_with_an_id_is_a_page()
        {
            // Pre-expansion the CLI cannot see Card's body, but the id sits on the invocation.
            var issues = Walk("<Pages id='p' selected='card'><Card id='card'/><Frame id='b'/></Pages>");
            Assert.IsEmpty(PagesIssues(issues));
        }

        // ── PUI-PAGES-CHILD-ID ────────────────────────────────────────────────────────────

        [Test]
        public void Child_without_id_is_reported()
        {
            var issues = Of(Walk("<Pages id='p'><Frame id='a'/><Frame/></Pages>"), PagesRules.ChildIdCode);
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("<Frame>", issues[0].Message);
            StringAssert.Contains("id", issues[0].Message);
        }

        // ── PUI-PAGES-SELECTED ────────────────────────────────────────────────────────────

        [Test]
        public void Selected_naming_no_page_is_reported()
        {
            var issues = Of(Walk("<Pages id='p' selected='zzz'><Frame id='a'/><Frame id='b'/></Pages>"),
                            PagesRules.SelectedCode);
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("'zzz'", issues[0].Message);
            StringAssert.Contains("a, b", issues[0].Message, "the message lists the pages there are");
        }

        [Test]
        public void Selected_variant_value_is_checked_too()
        {
            var issues = Of(Walk("<Pages id='p' selected='a' selected.portrait='zzz'><Frame id='a'/><Frame id='b'/></Pages>"),
                            PagesRules.SelectedCode);
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("selected.portrait", issues[0].Message);
        }

        [Test]
        public void Selected_naming_a_page_in_every_variant_is_fine()
        {
            var issues = Walk("<Pages id='p' selected='a' selected.portrait='b'><Frame id='a'/><Frame id='b'/></Pages>");
            Assert.IsEmpty(Of(issues, PagesRules.SelectedCode));
        }

        // ── PUI-PAGES-CHILD-HIDDEN ────────────────────────────────────────────────────────

        [Test]
        public void Page_declaring_hidden_is_reported()
        {
            var issues = Of(Walk("<Pages id='p'><Frame id='a'/><Frame id='b' hidden='true'/></Pages>"),
                            PagesRules.ChildHiddenCode);
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("'b'", issues[0].Message);
            StringAssert.Contains("selected=", issues[0].Message, "the message names the way out");
        }

        [Test]
        public void Page_declaring_hidden_under_a_variant_is_reported()
        {
            var issues = Of(Walk("<Pages id='p'><Frame id='a'/><Frame id='b' hidden.portrait='true'/></Pages>"),
                            PagesRules.ChildHiddenCode);
            Assert.AreEqual(1, issues.Count);
        }

        [Test]
        public void Page_pulling_hidden_from_a_class_is_reported()
        {
            var issues = Of(Walk("<Pages id='p'><Frame id='a'/><Frame id='b' class='gone'/></Pages>",
                                 "<Style name='gone' hidden='true'/>"),
                            PagesRules.ChildHiddenCode);
            Assert.AreEqual(1, issues.Count);
        }

        [Test]
        public void An_unresolvable_class_keeps_the_hidden_rule_quiet()
        {
            var issues = Walk("<Pages id='p'><Frame id='a'/><Frame id='b' class='imported'/></Pages>");
            Assert.IsEmpty(Of(issues, PagesRules.ChildHiddenCode));
        }

        // ── PUI-PAGES-EMPTY ───────────────────────────────────────────────────────────────

        [Test]
        public void Empty_pages_is_reported()
        {
            var issues = Of(Walk("<Pages id='p'/>"), PagesRules.EmptyCode);
            Assert.AreEqual(1, issues.Count);
        }

        // ── PUI-PAGES-ADD-TARGET ──────────────────────────────────────────────────────────

        [Test]
        public void Add_into_pages_is_reported()
        {
            var issues = Of(Walk(
                "<Pages id='p'><Frame id='a'/></Pages>" +
                "<Variant when='m'><Add into='#p'><Frame id='x'/></Add></Variant>"),
                PagesRules.AddTargetCode);
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("'#p'", issues[0].Message);
            StringAssert.Contains("selected", issues[0].Message, "the message names the way out");
        }

        [Test]
        public void Add_into_a_nested_path_ending_at_pages_is_reported()
        {
            var issues = Of(Walk(
                "<Frame id='root'><Pages id='p'><Frame id='a'/></Pages></Frame>" +
                "<Variant when='m'><Add into='#root/p'><Frame id='x'/></Add></Variant>"),
                PagesRules.AddTargetCode);
            Assert.AreEqual(1, issues.Count);
        }

        [Test]
        public void Add_into_a_page_is_fine()
        {
            var issues = Walk(
                "<Pages id='p'><Frame id='a'/></Pages>" +
                "<Variant when='m'><Add into='#a'><Frame id='x'/></Add></Variant>");
            Assert.IsEmpty(Of(issues, PagesRules.AddTargetCode));
        }
    }
}
