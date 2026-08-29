using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <c>PUI-TABMENU-CHILD</c>, <c>PUI-TABMENU-ITEM-WIDTH</c> and <c>PUI-EXPAND-NO-SOURCE</c>, plus
    /// the widening of <c>PUI-TAB-PARENT</c> to accept a <c>&lt;TabMenu&gt;</c> parent.
    /// </summary>
    public class TabMenuRulesTests
    {
        private static List<LintIssue> Walk(string body)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        private static bool Has(List<LintIssue> issues, string code) => issues.Any(i => i.Code == code);

        // ── PUI-TAB-PARENT ────────────────────────────────────────────────────────────────

        [Test]
        public void A_Tab_inside_a_TabMenu_is_not_orphaned()
        {
            var issues = Walk("<TabMenu id='m'><Tab id='a' text='A'/></TabMenu>");
            Assert.IsFalse(Has(issues, TabRules.TabParentCode),
                           "<TabMenu> is a tab group too — its rows are exactly <Tab>s");
        }

        [Test]
        public void A_Tab_under_a_plain_Frame_is_still_orphaned()
        {
            var issues = Walk("<Frame><Tab id='a' text='A'/></Frame>");
            Assert.IsTrue(Has(issues, TabRules.TabParentCode));
        }

        // ── PUI-TABMENU-CHILD ─────────────────────────────────────────────────────────────

        [Test]
        public void A_non_row_child_is_reported()
        {
            var issues = Walk("<TabMenu id='m'><Tab id='a' text='A'/><Text id='t'>hi</Text></TabMenu>");
            Assert.IsTrue(Has(issues, TabMenuRules.ChildCode));
        }

        [Test]
        public void Decor_is_allowed_as_a_child()
        {
            var issues = Walk("<TabMenu id='m'><Tab id='a' text='A'/><Decor kind='bracket' at='top-left'/></TabMenu>");
            Assert.IsFalse(Has(issues, TabMenuRules.ChildCode),
                           "<Decor> decorates the panel and never becomes a row");
        }

        [Test]
        public void A_wrapper_containing_a_Tab_is_allowed()
        {
            var issues = Walk("<TabMenu id='m'><Frame id='row'><Tab id='a' text='A'/></Frame></TabMenu>");
            Assert.IsFalse(Has(issues, TabMenuRules.ChildCode));
        }

        [Test]
        public void A_template_invocation_is_not_second_guessed()
        {
            // The CLI cannot expand it, so it may well contain a <Tab>.
            var issues = Walk("<TabMenu id='m' itemTemplate='Row'><Row text='A'/></TabMenu>");
            Assert.IsFalse(Has(issues, TabMenuRules.ChildCode));
        }

        // ── PUI-TABMENU-ITEM-WIDTH ────────────────────────────────────────────────────────

        [Test]
        public void A_width_on_a_row_is_reported()
        {
            var issues = Walk("<TabMenu id='m'><Tab id='a' text='A' width='120'/></TabMenu>");
            Assert.IsTrue(Has(issues, TabMenuRules.ItemWidthCode));
        }

        [Test]
        public void A_width_on_a_wrapper_row_is_reported()
        {
            var issues = Walk("<TabMenu id='m'><Frame id='row' width='120'><Tab id='a' text='A'/></Frame></TabMenu>");
            Assert.IsTrue(Has(issues, TabMenuRules.ItemWidthCode));
        }

        [Test]
        public void A_variant_only_width_is_reported_too()
        {
            var issues = Walk("<TabMenu id='m'><Tab id='a' text='A' width.mobile='120'/></TabMenu>");
            Assert.IsTrue(Has(issues, TabMenuRules.ItemWidthCode),
                          "a variant override is just as ignored as a base one");
        }

        [Test]
        public void A_height_on_a_row_is_fine()
        {
            var issues = Walk("<TabMenu id='m'><Tab id='a' text='A' height='40'/></TabMenu>");
            Assert.IsFalse(Has(issues, TabMenuRules.ItemWidthCode), "rows keep their own height");
        }

        [Test]
        public void A_width_inside_a_TabBar_is_still_fine()
        {
            var issues = Walk("<TabBar id='b'><Tab id='a' text='A' width='120'/></TabBar>");
            Assert.IsFalse(Has(issues, TabMenuRules.ItemWidthCode),
                           "a bar honours per-tab widths; only a menu forces them to span");
        }

        // ── Layout-group child rules ──────────────────────────────────────────────────────

        [Test]
        public void A_row_may_not_anchor_itself()
        {
            var issues = Walk("<TabMenu id='m'><Tab id='a' text='A' anchor='top'/></TabMenu>");
            Assert.IsTrue(Has(issues, LayoutGroupChildRules.AnchorCode),
                          "<TabMenu> lays its rows out, exactly like <TabBar>");
        }

        // ── PUI-EXPAND-NO-SOURCE ──────────────────────────────────────────────────────────

        [Test]
        public void A_bare_expand_outside_a_menu_is_reported()
        {
            var issues = Walk("<Frame><Animation id='t' on='expand' type='fadein'><Frame/></Animation></Frame>");
            Assert.IsTrue(Has(issues, StateTriggerRules.NoMenuCode));
        }

        [Test]
        public void A_bare_collapse_outside_a_menu_is_reported()
        {
            var issues = Walk("<Frame><Trigger id='t' on='collapse'><Frame/></Trigger></Frame>");
            Assert.IsTrue(Has(issues, StateTriggerRules.NoMenuCode));
        }

        [Test]
        public void A_bare_expand_inside_a_menu_is_fine()
        {
            var issues = Walk(@"<TabMenu id='m'>
                                  <Animation id='t' on='expand' type='fadein'><Tab id='a' text='A'/></Animation>
                                </TabMenu>");
            Assert.IsFalse(Has(issues, StateTriggerRules.NoMenuCode));
        }

        [Test]
        public void A_targeted_expand_is_left_to_runtime()
        {
            var issues = Walk("<Frame><Trigger id='t' on='expand@m'><Frame/></Trigger></Frame>");
            Assert.IsFalse(Has(issues, StateTriggerRules.NoMenuCode),
                           "@id resolves against ScopedIds at runtime — not statically checkable");
        }

        [Test]
        public void A_template_body_is_exempt()
        {
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><Trigger id='t' on='expand'><Tab id='a'/></Trigger></Template>
  <Screen name='S'><TabMenu id='m' itemTemplate='Row'/></Screen>
</PromptUGUI>";
            var issues = IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
            Assert.IsFalse(Has(issues, StateTriggerRules.NoMenuCode),
                           "the menu ancestor is supplied at invocation, not in the body");
        }

        // ── A clean document stays clean ──────────────────────────────────────────────────

        [Test]
        public void A_well_formed_menu_reports_nothing()
        {
            var issues = Walk(@"
              <TabMenu id='m' popupWidth='240' padding='8' spacing='4' radius='12'>
                <Tab id='a' text='World' bind='pw' isOn='true' height='44'/>
                <Tab id='b' text='Guild' bind='pg' height='44'/>
                <Decor kind='bracket' at='top-left'/>
              </TabMenu>
              <Frame id='pw'/>
              <Frame id='pg'/>");
            Assert.IsEmpty(issues, "issues: " + string.Join(", ", issues.Select(i => i.Code + " " + i.Message)));
        }
    }
}
