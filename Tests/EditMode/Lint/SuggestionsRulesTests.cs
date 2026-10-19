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

        // ── through IRWalker (the CLI and the runtime warning path share it) ─────────────────

        private static List<LintIssue> Walk(string body, string extra = "")
            => IRWalker.Walk(UIDocumentParser.Parse("<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                                                    + extra + "<Screen name='S'>" + body + "</Screen></PromptUGUI>"))
                       .ToList();

        private static List<LintIssue> Lint(string body, string extra = "")
            => DocumentLinter.Walk(UIDocumentParser.Parse("<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                                                          + extra + "<Screen name='S'>" + body + "</Screen></PromptUGUI>"))
                             .ToList();

        private static bool Has(List<LintIssue> issues, string code) => issues.Any(i => i.Code == code);

        private static string Field(string listAttrs, string fieldAttrs = "", string children = "")
            => $"<InputField id='f' {fieldAttrs}><Suggestions id='s' {listAttrs}>{children}</Suggestions></InputField>";

        private const string Row = "<Template name='Row'><Text id='label'/></Template>";

        [Test]
        public void The_walker_reports_outside_and_duplicate()
        {
            Assert.IsTrue(Has(Walk("<Frame><Suggestions itemTemplate='Row'/></Frame>"), SuggestionsRules.OutsideCode));
            Assert.IsTrue(Has(Walk("<InputField><Suggestions itemTemplate='Row'/><Suggestions itemTemplate='Row'/></InputField>"),
                              SuggestionsRules.DuplicateCode));
            Assert.IsFalse(Has(Walk(Field("itemTemplate='Row'")), SuggestionsRules.OutsideCode));
        }

        [Test]
        public void A_missing_or_empty_itemTemplate()
        {
            Assert.IsTrue(Has(Walk(Field("")), SuggestionsRules.TemplateCode));
            Assert.IsTrue(Has(Walk(Field("itemTemplate=''")), SuggestionsRules.TemplateCode));
            Assert.IsFalse(Has(Walk(Field("itemTemplate='Row'")), SuggestionsRules.TemplateCode));
        }

        [Test]
        public void Layout_attributes_are_ignored()
        {
            foreach (var attr in new[] { "anchor='center'", "size='10x10'", "width='300'", "height='40'", "margin='4'",
                                         "pivot='0,0'", "scale='2'", "hidden='true'" })
                Assert.IsTrue(Has(Walk(Field("itemTemplate='Row' " + attr)), SuggestionsRules.LayoutAttrCode), attr);
            Assert.IsFalse(Has(Walk(Field("itemTemplate='Row' maxHeight='200' gap='4'")), SuggestionsRules.LayoutAttrCode));
        }

        [Test]
        public void Children_other_than_Decor_and_Scrollbar()
        {
            Assert.IsTrue(Has(Walk(Field("itemTemplate='Row'", children: "<Text/>")), SuggestionsRules.ChildCode));
            Assert.IsFalse(Has(Walk(Field("itemTemplate='Row'", children: "<Decor kind='corner'/><Scrollbar/>")),
                               SuggestionsRules.ChildCode));
        }

        [Test]
        public void A_placement_that_is_neither_below_nor_above()
        {
            Assert.IsTrue(Has(Walk(Field("itemTemplate='Row' placement='left'")), SuggestionsRules.EnumCode));
            Assert.IsTrue(Has(Walk(Field("itemTemplate='Row' placement.mobile='up'")), SuggestionsRules.EnumCode));
            Assert.IsFalse(Has(Walk(Field("itemTemplate='Row' placement='above'")), SuggestionsRules.EnumCode));
        }

        [Test]
        public void A_placement_may_add_an_alignment()
        {
            Assert.IsFalse(Has(Walk(Field("itemTemplate='Row' placement='below-end'")), SuggestionsRules.EnumCode));
            Assert.IsFalse(Has(Walk(Field("itemTemplate='Row' placement='above-start'")), SuggestionsRules.EnumCode));
            Assert.IsTrue(Has(Walk(Field("itemTemplate='Row' placement='below-middle'")), SuggestionsRules.EnumCode));
            Assert.IsTrue(Has(Walk(Field("itemTemplate='Row' placement='end'")), SuggestionsRules.EnumCode));
        }

        [Test]
        public void A_negative_or_non_numeric_popupWidth()
        {
            Assert.IsTrue(Has(Walk(Field("itemTemplate='Row' popupWidth='-1'")), SuggestionsRules.ValueCode));
            Assert.IsTrue(Has(Walk(Field("itemTemplate='Row' popupWidth='wide'")), SuggestionsRules.ValueCode));
            Assert.IsFalse(Has(Walk(Field("itemTemplate='Row' popupWidth='360'")), SuggestionsRules.ValueCode));
        }

        [Test]
        public void A_negative_or_non_numeric_maxHeight_or_gap()
        {
            Assert.IsTrue(Has(Walk(Field("itemTemplate='Row' maxHeight='-1'")), SuggestionsRules.ValueCode));
            Assert.IsTrue(Has(Walk(Field("itemTemplate='Row' maxHeight='tall'")), SuggestionsRules.ValueCode));
            Assert.IsTrue(Has(Walk(Field("itemTemplate='Row' gap='-2'")), SuggestionsRules.ValueCode));
            Assert.IsFalse(Has(Walk(Field("itemTemplate='Row' maxHeight='0' gap='0'")), SuggestionsRules.ValueCode));
        }

        [Test]
        public void A_multi_line_host()
        {
            Assert.IsTrue(Has(Walk(Field("itemTemplate='Row'", "lineType='multi-newline'")), SuggestionsRules.MultilineCode));
            Assert.IsFalse(Has(Walk(Field("itemTemplate='Row'", "lineType='single'")), SuggestionsRules.MultilineCode));
            Assert.IsFalse(Has(Walk("<InputField lineType='multi-newline'/>"), SuggestionsRules.MultilineCode),
                           "only a field that has a list");
        }

        [Test]
        public void A_row_template_with_a_selectable_is_reported_on_the_expanded_pass()
        {
            var issues = Lint(Field("itemTemplate='BtnRow'"), "<Template name='BtnRow'><Btn id='b'>x</Btn></Template>");
            Assert.IsTrue(Has(issues, SuggestionsRules.RowSelectableCode));
            Assert.IsFalse(Has(Lint(Field("itemTemplate='Row'"), Row), SuggestionsRules.RowSelectableCode));
        }

        // ── extensions of existing rules ───────────────────────────────────────────────────

        [Test]
        public void A_scrollbar_under_it_is_not_outside()
            => Assert.IsFalse(Has(Walk(Field("itemTemplate='Row'", children: "<Scrollbar/>")), ScrollbarRules.OutsideCode));

        [Test]
        public void A_non_integer_padding_is_reported()
            => Assert.IsTrue(Has(Walk(Field("itemTemplate='Row' padding='2.5'")), PaddingRules.Code));

        [Test]
        public void It_takes_the_pointer_so_what_is_inside_needs_no_raycastTarget()
            // The panel is its own catcher; a drawn surface inside it is already covered. Under its
            // <InputField> that is moot (the field covers its subtree); a misplaced one is the case.
            => Assert.IsFalse(Has(Walk("<Frame><Suggestions id='s' itemTemplate='Row'><Image id='deco'/></Suggestions></Frame>"),
                                  RaycastRules.UndecidedCode));

        [Test]
        public void A_small_font_in_a_row_template_is_reported()
        {
            // An iPhone SE under a 1080×1920 reference: 0.347 dp per unit, so 3 units is ~1 dp.
            var policy = new FontFloorPolicy
            {
                Floor = 7f,
                Targets = new List<FontFloorTarget> { new() { name = "SE", screen = "750x1334@2x" } },
                TextDefaultSize = 36f,
            };
            var doc = UIDocumentParser.Parse("<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                                             + "<Template name='Tiny'><Text id='t' fontSize='3'>x</Text></Template>"
                                             + "<Screen name='S' reference='1080x1920'>" + Field("itemTemplate='Tiny'")
                                             + "</Screen></PromptUGUI>", "main.ui.xml");
            Assert.IsTrue(DocumentLinter.Walk(doc, "main.ui.xml", fontFloor: policy)
                                        .Any(i => i.Code == FontFloorRules.Code && i.Id == "t"),
                          "the row's own text, found through itemTemplate");
        }

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
