using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Lint;
using PromptUGUI.Parser;
using PromptUGUI.Tests.EditMode.Controls;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <c>&lt;Collapsible&gt;</c>'s structural lint (spec 2026-08-31-collapsible-design §4.8) —
    /// through <see cref="IRWalker"/>, which is what the CLI and the runtime warning path share.
    /// </summary>
    public class CollapsibleRulesTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static List<LintIssue> Walk(string body, string styles = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                      + styles + "<Screen name='S'>" + body + "</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        private static bool Has(List<LintIssue> issues, string code) => issues.Any(i => i.Code == code);

        // ── height ─────────────────────────────────────────────────────────────────────────

        [Test]
        public void Height_is_rejected()
        {
            var issues = Walk("<Collapsible id='c' height='100'><Btn id='r'/></Collapsible>");
            Assert.IsTrue(Has(issues, CollapsibleRules.HeightCode));
            StringAssert.Contains("maxHeight",
                issues.First(i => i.Code == CollapsibleRules.HeightCode).Message,
                "the message points at the attribute that does cap the body");
        }

        [Test]
        public void Size_is_rejected_too()
            => Assert.IsTrue(Has(Walk("<Collapsible id='c' size='150x100'><Btn id='r'/></Collapsible>"),
                                 CollapsibleRules.HeightCode));

        [Test]
        public void A_variant_height_is_rejected()
            => Assert.IsTrue(Has(Walk("<Collapsible id='c' height.portrait='100'><Btn id='r'/></Collapsible>"),
                                 CollapsibleRules.HeightCode));

        [Test]
        public void A_height_arriving_through_a_class_is_rejected()
            => Assert.IsTrue(Has(
                Walk("<Collapsible id='c' class='tall'><Btn id='r'/></Collapsible>",
                     "<Style name='tall' height='100'/>"),
                CollapsibleRules.HeightCode));

        [Test]
        public void Width_and_maxHeight_are_fine()
            => Assert.IsFalse(Has(
                Walk("<Collapsible id='c' width='150' maxHeight='200' headerHeight='24'><Btn id='r'/></Collapsible>"),
                CollapsibleRules.HeightCode));

        [Test]
        public void Opening_a_panel_with_a_height_throws_at_runtime()
        {
            var ex = Assert.Throws<ParseException>(
                () => CollapsibleTests.OpenForLint("<Collapsible id='c' height='100'><Btn id='r'/></Collapsible>"));
            StringAssert.Contains(CollapsibleRules.HeightCode, ex.Message);
        }

        // ── <Header> ───────────────────────────────────────────────────────────────────────

        [Test]
        public void Header_must_come_first()
        {
            var issues = Walk(@"<Collapsible id='c'>
                                  <Btn id='r'/>
                                  <Header><Text>x</Text></Header>
                                </Collapsible>");
            Assert.IsTrue(Has(issues, CollapsibleRules.HeaderFirstCode));
        }

        [Test]
        public void Header_first_is_fine()
            => Assert.IsFalse(Has(
                Walk("<Collapsible id='c'><Header><Text>x</Text></Header><Btn id='r'/></Collapsible>"),
                CollapsibleRules.HeaderFirstCode));

        [Test]
        public void Two_headers_are_rejected()
            => Assert.IsTrue(Has(
                Walk(@"<Collapsible id='c'>
                         <Header><Text>a</Text></Header>
                         <Header><Text>b</Text></Header>
                       </Collapsible>"),
                CollapsibleRules.HeaderMultiCode));

        [Test]
        public void Header_and_caption_attributes_clash()
        {
            var issues = Walk("<Collapsible id='c' text='任务'><Header><Text>x</Text></Header></Collapsible>");
            Assert.IsTrue(Has(issues, CollapsibleRules.HeaderConflictCode));
            StringAssert.Contains("arrow",
                issues.First(i => i.Code == CollapsibleRules.HeaderConflictCode).Message,
                "…while saying which caption attributes still apply");
        }

        [Test]
        public void Arrow_attributes_do_not_clash_with_a_header()
            => Assert.IsFalse(Has(
                Walk("<Collapsible id='c' arrow='' arrowSize='12'><Header><Text>x</Text></Header></Collapsible>"),
                CollapsibleRules.HeaderConflictCode));

        [Test]
        public void Header_outside_a_collapsible_is_rejected()
            => Assert.IsTrue(Has(Walk("<Frame id='f'><Header><Text>x</Text></Header></Frame>"),
                                 CollapsibleRules.HeaderOutsideCode));

        // ── body vs header children ────────────────────────────────────────────────────────

        [Test]
        public void A_body_child_may_not_anchor_itself()
            => Assert.IsTrue(Has(
                Walk("<Collapsible id='c'><Btn id='r' anchor='center'/></Collapsible>"),
                "PUI-LAYOUT-ANCHOR"));

        [Test]
        public void A_header_child_may()
            => Assert.IsFalse(Has(
                Walk(@"<Collapsible id='c'>
                         <Header><Text id='t' anchor='center-left' margin='_,_,_,10'>x</Text></Header>
                         <Btn id='r'/>
                       </Collapsible>"),
                "PUI-LAYOUT-ANCHOR"),
                "the header bar is free-positioning — that is the point of the slot");

        // ── accordion ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Two_open_panels_in_one_group_are_flagged()
        {
            var issues = Walk(@"
                <Collapsible id='a' group='g' text='画面'><Btn id='r1'/></Collapsible>
                <Collapsible id='b' group='g' text='音频'><Btn id='r2'/></Collapsible>");
            Assert.IsTrue(Has(issues, CollapsibleRules.GroupMultiExpandedCode),
                          "expanded defaults to true, so both are authored open");
        }

        [Test]
        public void One_open_panel_per_group_is_fine()
            => Assert.IsFalse(Has(Walk(@"
                <Collapsible id='a' group='g' text='画面'><Btn id='r1'/></Collapsible>
                <Collapsible id='b' group='g' expanded='false' text='音频'><Btn id='r2'/></Collapsible>"),
                CollapsibleRules.GroupMultiExpandedCode));

        [Test]
        public void Different_groups_do_not_interfere()
            => Assert.IsFalse(Has(Walk(@"
                <Collapsible id='a' group='g1' text='画面'><Btn id='r1'/></Collapsible>
                <Collapsible id='b' group='g2' text='音频'><Btn id='r2'/></Collapsible>"),
                CollapsibleRules.GroupMultiExpandedCode));

        [Test]
        public void Ungrouped_panels_are_never_flagged()
            => Assert.IsFalse(Has(Walk(@"
                <Collapsible id='a' text='画面'><Btn id='r1'/></Collapsible>
                <Collapsible id='b' text='音频'><Btn id='r2'/></Collapsible>"),
                CollapsibleRules.GroupMultiExpandedCode));
    }
}
