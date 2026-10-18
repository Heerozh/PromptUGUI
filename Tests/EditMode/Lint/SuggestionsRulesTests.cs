using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <c>&lt;Suggestions&gt;</c>'s lint (spec 2026-10-18 §7). The placement pair is shared with
    /// <c>ScreenInstantiator</c>, which warns with the same issues at runtime.
    /// </summary>
    public class SuggestionsRulesTests
    {
        private static ElementNode ScreenRoot(string body)
        {
            var doc = UIDocumentParser.Parse("<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                                             + "<Screen name='S'>" + body + "</Screen></PromptUGUI>");
            return doc.Screens[0].Root.Children[0];
        }

        private static List<LintIssue> Outside(string body)
        {
            var parent = ScreenRoot(body);
            return parent.Children.SelectMany(c => SuggestionsRules.CheckOutside(parent, c)).ToList();
        }

        // ── outside ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Under_an_InputField_is_fine()
            => CollectionAssert.IsEmpty(Outside("<InputField id='f'><Suggestions itemTemplate='Row'/></InputField>"));

        [Test]
        public void Under_a_frame_is_outside()
        {
            var issues = Outside("<Frame id='f'><Suggestions id='s' itemTemplate='Row'/></Frame>");
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(SuggestionsRules.OutsideCode, issues[0].Code);
            StringAssert.Contains("<Frame", issues[0].Message);
        }

        [Test]
        public void Under_a_template_invocation_is_not_judged_before_expansion()
            => CollectionAssert.IsEmpty(Outside("<MyField><Suggestions itemTemplate='Row'/></MyField>"));

        [Test]
        public void Other_children_are_not_its_business()
            => CollectionAssert.IsEmpty(Outside("<Frame id='f'><Text/></Frame>"));

        // ── duplicate ──────────────────────────────────────────────────────────────────────

        [Test]
        public void One_is_fine()
            => CollectionAssert.IsEmpty(SuggestionsRules.CheckDuplicate(
                ScreenRoot("<InputField id='f'><Suggestions itemTemplate='Row'/></InputField>")));

        [Test]
        public void A_second_one_is_reported_once()
        {
            var issues = SuggestionsRules.CheckDuplicate(ScreenRoot(
                "<InputField id='f'><Suggestions id='a' itemTemplate='Row'/><Suggestions id='b' itemTemplate='Row'/></InputField>"))
                .ToList();
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(SuggestionsRules.DuplicateCode, issues[0].Code);
            Assert.AreEqual("b", issues[0].Id);
        }
    }
}
