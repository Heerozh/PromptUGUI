using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Lint;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <c>&lt;Popover&gt;</c>'s lint and the rules it extends (spec 2026-10-19 §7): the PUI-POPOVER-* self
    /// checks, PUI-SHOW-EXPAND-SELF, and the hug / padding / raycast / menu-source tables a popover joins. A
    /// popover is in no flow, so its parent's layout checks skip it; its own children are a column.
    /// </summary>
    public class PopoverRulesTests
    {
        private const string Prelude = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";

        private static List<LintIssue> Walk(string body, string extra = "")
            => IRWalker.Walk(UIDocumentParser.Parse(Prelude + extra + "<Screen name='S'>" + body + "</Screen></PromptUGUI>"))
                       .ToList();

        private static List<LintIssue> Lint(string body, string extra = "")
            => DocumentLinter.Walk(UIDocumentParser.Parse(Prelude + extra + "<Screen name='S'>" + body + "</Screen></PromptUGUI>"))
                             .ToList();

        private static bool Has(List<LintIssue> issues, string code) => issues.Any(i => i.Code == code);

        private static string Pop(string attrs, string children = "<Frame width='40' height='20'/>")
            => $"<Btn id='b' anchor='center' size='40x40'>B</Btn><Popover id='p' {attrs}>{children}</Popover>";

        // ── PUI-POPOVER-ON ──────────────────────────────────────────────────────────────────────

        [Test]
        public void on_must_be_click_at_an_id_or_manual()
        {
            Assert.IsTrue(Has(Walk(Pop("on='click'")), PopoverRules.OnCode), "a bare click has no trigger");
            Assert.IsTrue(Has(Walk(Pop("on='hover-enter@b'")), PopoverRules.OnCode));
            Assert.IsTrue(Has(Walk(Pop("on='click@'")), PopoverRules.OnCode));
            Assert.IsFalse(Has(Walk(Pop("on='click@b'")), PopoverRules.OnCode));
            Assert.IsFalse(Has(Walk(Pop("on='manual'")), PopoverRules.OnCode));
            Assert.IsFalse(Has(Walk(Pop("")), PopoverRules.OnCode), "manual by default");
        }

        // ── PUI-POPOVER-ENUM / -VALUE ─────────────────────────────────────────────────────────────

        [Test]
        public void placement_and_dismiss_must_be_known_values_variants_included()
        {
            Assert.IsTrue(Has(Walk(Pop("placement='above'")), PopoverRules.EnumCode));
            Assert.IsTrue(Has(Walk(Pop("placement='top-middle'")), PopoverRules.EnumCode));
            Assert.IsTrue(Has(Walk(Pop("placement='top' placement.portrait='under'")), PopoverRules.EnumCode));
            Assert.IsTrue(Has(Walk(Pop("dismiss='close'")), PopoverRules.EnumCode));
            Assert.IsFalse(Has(Walk(Pop("placement='bottom-end' placement.portrait='right-center' dismiss='block'")),
                               PopoverRules.EnumCode));
            Assert.IsFalse(Has(Walk(Pop("placement='left' dismiss='pass'")), PopoverRules.EnumCode));
        }

        [Test]
        public void gap_and_transition_must_parse()
        {
            Assert.IsTrue(Has(Walk(Pop("gap='-2'")), PopoverRules.ValueCode));
            Assert.IsTrue(Has(Walk(Pop("gap='wide'")), PopoverRules.ValueCode));
            Assert.IsTrue(Has(Walk(Pop("transition='fast'")), PopoverRules.ValueCode));
            Assert.IsFalse(Has(Walk(Pop("gap='0' transition='150ms'")), PopoverRules.ValueCode));
            Assert.IsFalse(Has(Walk(Pop("gap='6.5' transition='0.2s'")), PopoverRules.ValueCode));
        }

        // ── over (spec §17): PUI-POPOVER-ENUM / -GAP ─────────────────────────────────────────────

        [Test]
        public void over_is_a_placement()
        {
            Assert.IsFalse(Has(Walk(Pop("placement='over' placement.portrait='over-end'")), PopoverRules.EnumCode));
            Assert.IsFalse(Has(Walk(Pop("placement='over-center'")), PopoverRules.EnumCode));
            Assert.IsTrue(Has(Walk(Pop("placement='over-top'")), PopoverRules.EnumCode));
        }

        [Test]
        public void A_gap_says_nothing_when_every_placement_is_over()
        {
            Assert.IsTrue(Has(Walk(Pop("placement='over-end' gap='8'")), PopoverRules.GapCode));
            Assert.IsTrue(Has(Walk(Pop("placement='over' gap.portrait='2'")), PopoverRules.GapCode), "a gap on a variant");
            Assert.IsTrue(Has(Walk(Pop("placement='over-end' placement.portrait='over-start' gap='8'")),
                              PopoverRules.GapCode), "every variant over too");
            Assert.IsTrue(Has(Lint(Pop("class='pop' gap='8'"), "<Style name='pop' placement='over-end'/>"),
                              PopoverRules.GapCode), "placement from a class");

            Assert.IsFalse(Has(Walk(Pop("gap='8'")), PopoverRules.GapCode), "no placement: bottom-start");
            Assert.IsFalse(Has(Walk(Pop("placement='over-end' placement.portrait='top-end' gap='8'")),
                               PopoverRules.GapCode), "the gap still serves the portrait side");
            Assert.IsFalse(Has(Walk(Pop("placement='over-end'")), PopoverRules.GapCode), "no gap written");
        }

        // ── PUI-POPOVER-SIZE ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Stretch_percent_and_native_sizes_are_errors()
        {
            foreach (var attr in new[] { "width='stretch'", "height='40%'", "size='stretchx40'", "width='clamp(_, stretch, 200)'",
                                         "height='stretch(2)'" })
                Assert.IsTrue(Has(Walk(Pop(attr)), PopoverRules.SizeCode), attr);
            foreach (var attr in new[] { "width='200'", "height='hug'", "width='clamp(_, hug, 200)'", "size='120x80'" })
                Assert.IsFalse(Has(Walk(Pop(attr)), PopoverRules.SizeCode), attr);
        }

        // ── PUI-POPOVER-LAYOUT-ATTR ───────────────────────────────────────────────────────────────

        [Test]
        public void Layout_attributes_are_ignored()
        {
            foreach (var attr in new[] { "anchor='center'", "margin='4'", "pivot='0,0'", "flow='false'", "scale='2'",
                                         "hidden='true'", "hidden.portrait='true'" })
                Assert.IsTrue(Has(Walk(Pop(attr)), PopoverRules.LayoutAttrCode), attr);
            Assert.IsFalse(Has(Walk(Pop("width='120' gap='4' padding='6'")), PopoverRules.LayoutAttrCode));
        }

        // ── PUI-POPOVER-PARENT ────────────────────────────────────────────────────────────────────

        [Test]
        public void A_popover_where_it_would_be_taken_for_a_row_a_tab_or_a_page()
        {
            const string p = "<Popover id='p'><Frame width='40' height='20'/></Popover>";
            foreach (var parent in new[]
                     {
                         $"<ScrollList id='l' width='100' height='100'>{p}</ScrollList>",
                         $"<TabBar id='t'><Tab id='x' text='X'/>{p}</TabBar>",
                         $"<TabMenu id='t'><Tab id='x' text='X'/>{p}</TabMenu>",
                         $"<Carousel id='c' width='100' height='100'>{p}</Carousel>",
                         $"<Pages id='g' anchor='stretch'>{p}</Pages>",
                     })
                Assert.IsTrue(Has(Walk(parent), PopoverRules.ParentCode), parent);
        }

        [Test]
        public void A_popover_inside_a_Btn_Tab_or_Toggle_takes_its_states()
        {
            Assert.IsTrue(Has(Walk("<Btn id='b' size='40x40'><Frame><Popover id='p'/></Frame></Btn>"), PopoverRules.ParentCode));
            Assert.IsTrue(Has(Walk("<Toggle id='t' size='40x40'><Popover id='p'/></Toggle>"), PopoverRules.ParentCode));
            Assert.IsFalse(Has(Walk("<Frame id='f' size='40x40'><Popover id='p'/></Frame>"), PopoverRules.ParentCode));
            Assert.IsFalse(Has(Walk("<VStack id='v'><Popover id='p'/></VStack>"), PopoverRules.ParentCode));
        }

        // ── PUI-POPOVER-HUG-WIDTH ─────────────────────────────────────────────────────────────────

        [Test]
        public void A_hugging_width_with_only_stretch_children_is_zero()
        {
            Assert.IsTrue(Has(Walk(Pop("", "<Text width='stretch' height='20'/>")), PopoverRules.HugWidthCode),
                          "no width is a hug");
            Assert.IsTrue(Has(Walk(Pop("width='clamp(_, hug, 300)'", "<Frame width='stretch' height='20'/>")),
                              PopoverRules.HugWidthCode));
            Assert.IsFalse(Has(Walk(Pop("", "<Text width='stretch' height='20'/><Frame width='100' height='20'/>")),
                               PopoverRules.HugWidthCode), "one sized child gives the column its width");
            Assert.IsFalse(Has(Walk(Pop("width='200'", "<Text width='stretch' height='20'/>")), PopoverRules.HugWidthCode));
        }

        // ── Extended rules ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Hug_is_legal_on_a_popover()
            => Assert.IsFalse(Has(Walk(Pop("width='hug' height='clamp(_, hug, 300)'")), HugRules.TagCode));

        [Test]
        public void A_stretch_child_in_a_hugging_popover_collapses()
        {
            Assert.IsTrue(Has(Walk(Pop("height='hug'", "<Frame width='40' height='stretch'/>")), HugRules.StretchChildCode));
            Assert.IsTrue(Has(Walk(Pop("", "<Frame width='40' height='stretch'/>")), HugRules.StretchChildCode),
                          "no height is a hug");
            Assert.IsFalse(Has(Walk(Pop("height='200'", "<Frame width='40' height='stretch'/>")), HugRules.StretchChildCode));
        }

        [Test]
        public void Scale_with_hug_is_not_an_error_on_a_popover()
        {
            var issues = Walk(Pop("width='hug' height='clamp(_, 20, 300)' scale='2'"));
            Assert.IsFalse(Has(issues, HugRules.ScaleCode));
            Assert.IsFalse(Has(issues, ClampRules.ClampScaleCode));
            Assert.IsTrue(Has(issues, PopoverRules.LayoutAttrCode), "scale is simply ignored");
        }

        [Test]
        public void Padding_must_be_whole_numbers()
            => Assert.IsTrue(Has(Walk(Pop("padding='4.5'")), PaddingRules.Code));

        [Test]
        public void The_panel_decides_raycast_for_its_subtree()
        {
            Assert.IsTrue(Has(Walk(Pop("raycastTarget='false'")), RaycastRules.TagCode), "the panel always takes the pointer");
            Assert.IsFalse(Has(Walk("<Popover id='p'><Frame color='#336699' width='40' height='20'/></Popover>"),
                               RaycastRules.UndecidedCode), "everything inside it is already under a catcher");
        }

        [Test]
        public void A_bare_expand_inside_a_popover_has_a_source()
            => Assert.IsFalse(Has(Walk("<Popover id='p'><Trigger on='expand'><Frame width='4' height='4'/></Trigger></Popover>"),
                                  StateTriggerRules.NoMenuCode));

        // ── The parent's layout checks skip a popover; its own children are a column ─────────────

        [Test]
        public void A_popover_in_a_stack_or_grid_is_not_checked_as_a_flow_child()
        {
            var inStack = Walk("<VStack id='v'><Popover id='p' anchor='center' margin='4'><Frame width='4' height='4'/></Popover></VStack>");
            Assert.IsFalse(Has(inStack, LayoutGroupChildRules.AnchorCode));
            Assert.IsFalse(Has(inStack, LayoutGroupChildRules.MarginCode));
            Assert.IsTrue(Has(inStack, PopoverRules.LayoutAttrCode), "reported once, by the popover's own rule");

            Assert.IsFalse(Has(Walk("<Grid id='g' cellSize='40x40'><Popover id='p' width='100'/></Grid>"),
                               LayoutGroupChildRules.GridChildSizeCode));
            Assert.IsFalse(Has(Walk("<Frame id='f'><Popover id='p' flow='false'/></Frame>"), LayoutGroupChildRules.FlowOutsideCode));
            Assert.IsFalse(Has(Walk("<Frame id='f'><Popover id='p' anchor='top-left' margin='_,_,4,_'/></Frame>"),
                               MarginAnchorRules.InertSideCode));
        }

        [Test]
        public void Its_children_are_checked_as_a_column()
            => Assert.IsTrue(Has(Walk(Pop("", "<Frame anchor='center' width='40' height='20'/>")), LayoutGroupChildRules.AnchorCode));

        // ── PUI-SHOW-EXPAND-SELF ──────────────────────────────────────────────────────────────────

        private const string Block = "<Frame width='4' height='4'/>";

        [Test]
        public void A_bare_show_expand_in_its_own_panel_is_an_error()
        {
            Assert.IsTrue(Has(Walk($"<Popover id='p'><Show on='expand'>{Block}</Show></Popover>"),
                              StateTriggerRules.ShowExpandSelfCode));
            Assert.IsTrue(Has(Walk($"<TabMenu id='m'><Tab id='x' text='X'/><Show on='collapse'>{Block}</Show></TabMenu>"),
                              StateTriggerRules.ShowExpandSelfCode));
            Assert.IsTrue(Has(Walk($"<Collapsible id='c' text='C'><Show on='expand'>{Block}</Show></Collapsible>"),
                              StateTriggerRules.ShowExpandSelfCode), "a collapsible's body is its panel");
        }

        [Test]
        public void A_show_in_a_header_or_pointing_elsewhere_is_fine()
        {
            Assert.IsFalse(Has(Walk($"<Collapsible id='c' text='C'><Header><Show on='expand'>{Block}</Show></Header>{Block}</Collapsible>"),
                               StateTriggerRules.ShowExpandSelfCode));
            Assert.IsFalse(Has(Walk($"<Popover id='p'><Collapsible id='c' text='C'><Header><Show on='expand'>{Block}</Show></Header>{Block}</Collapsible></Popover>"),
                               StateTriggerRules.ShowExpandSelfCode), "the nearest source is the collapsible, from its header");
            Assert.IsFalse(Has(Walk($"<Collapsible id='c' text='C'>{Block}</Collapsible><Popover id='p'><Show on='expand@c'>{Block}</Show></Popover>"),
                               StateTriggerRules.ShowExpandSelfCode));
            Assert.IsFalse(Has(Walk($"<Popover id='p'><Trigger on='expand'>{Block}</Trigger></Popover>"),
                               StateTriggerRules.ShowExpandSelfCode), "a trigger fires on the edge — that is fine");
        }

        [Test]
        public void A_template_body_is_judged_by_the_expanded_pass()
        {
            const string tpl = "<Template name='Mark'><Show on='expand'><Frame width='4' height='4'/></Show></Template>";
            Assert.IsFalse(Has(Walk("<Popover id='p'><Mark/></Popover>", tpl), StateTriggerRules.ShowExpandSelfCode),
                           "the raw pass cannot see into the invocation");
            Assert.IsTrue(Has(Lint("<Popover id='p'><Mark/></Popover>", tpl), StateTriggerRules.ShowExpandSelfCode));
        }

        // ── Runtime mirror ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Layout_attributes_are_ignored_with_a_runtime_warning()
        {
            UI.ResetForTests();
            try
            {
                PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
                UI.LoadDocument("t", Prelude + "<Screen name='S'><Popover id='p' anchor='center'><Frame width='4' height='4'/></Popover>"
                                     + "</Screen></PromptUGUI>");
                LogAssert.Expect(LogType.Warning, new Regex(Regex.Escape("<Popover id='p'>: 'anchor' has no effect")));
                UI.Open("S");
            }
            finally
            {
                UI.ResetForTests();
            }
        }
    }
}
