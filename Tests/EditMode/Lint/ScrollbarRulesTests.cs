using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <c>&lt;Scrollbar&gt;</c>'s lint (spec 2026-09-12-scrollbar-part-element-design §4.5) — through
    /// <see cref="IRWalker"/>, which the CLI and the runtime warning path share.
    /// </summary>
    public class ScrollbarRulesTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static List<LintIssue> Walk(string body, string extra = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                      + extra + "<Screen name='S'>" + body + "</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        private static List<LintIssue> WalkDoc(string doc)
            => IRWalker.Walk(UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + doc + "</PromptUGUI>")).ToList();

        private static bool Has(List<LintIssue> issues, string code) => issues.Any(i => i.Code == code);
        private static LintIssue First(List<LintIssue> issues, string code) => issues.First(i => i.Code == code);

        // ── placement ──────────────────────────────────────────────────────────────────────

        [Test]
        public void A_scrollbar_under_a_scrolllist_is_fine()
            => Assert.IsFalse(Has(Walk("<ScrollList id='l'><Scrollbar/></ScrollList>"), ScrollbarRules.OutsideCode));

        [Test]
        public void A_scrollbar_under_a_dropdown_is_fine()
            => Assert.IsFalse(Has(Walk("<Dropdown id='d'><Scrollbar/></Dropdown>"), ScrollbarRules.OutsideCode));

        [Test]
        public void A_scrollbar_under_a_frame_is_outside()
        {
            var issues = Walk("<Frame id='f'><Scrollbar id='b'/></Frame>");
            Assert.IsTrue(Has(issues, ScrollbarRules.OutsideCode));
            StringAssert.Contains("<Frame", First(issues, ScrollbarRules.OutsideCode).Message);
        }

        [Test]
        public void A_scrollbar_wrapped_in_a_show_is_outside_too()
            => Assert.IsTrue(Has(
                Walk("<ScrollList id='l'><Show on='state-hover'><Scrollbar/></Show></ScrollList>"),
                ScrollbarRules.OutsideCode));

        [Test]
        public void A_scrollbar_under_a_template_invocation_is_not_judged_before_expansion()
            // The CLI cannot see where <MyList>'s <Slot/> sits; the expanded pass decides.
            => Assert.IsFalse(Has(Walk("<MyList><Scrollbar/></MyList>"), ScrollbarRules.OutsideCode));

        [Test]
        public void A_template_body_rooted_in_a_scrollbar_is_fine()
            => Assert.IsFalse(Has(
                WalkDoc("<Template name='Bar'><Scrollbar thickness='6'/></Template><Screen name='S'><Frame/></Screen>"),
                ScrollbarRules.OutsideCode));

        [Test]
        public void Two_scrollbars_under_one_host_are_a_duplicate()
        {
            var issues = Walk("<ScrollList id='l'><Scrollbar id='a'/><Scrollbar id='b'/></ScrollList>");
            Assert.IsTrue(Has(issues, ScrollbarRules.DuplicateCode));
            Assert.AreEqual("b", First(issues, ScrollbarRules.DuplicateCode).Id, "reported on the second one");
        }

        [Test]
        public void A_scrollbar_directly_inside_an_add_block_is_rejected()
            => Assert.IsTrue(Has(
                Walk("<ScrollList id='l'/><Variant when='m'><Add into='#l'><Scrollbar/></Add></Variant>"),
                ScrollbarRules.InAddCode));

        [Test]
        public void A_whole_list_added_by_a_variant_may_carry_its_scrollbar()
            => Assert.IsFalse(Has(
                Walk("<Frame id='f'/><Variant when='m'><Add into='#f'><ScrollList id='l'><Scrollbar/></ScrollList></Add></Variant>"),
                ScrollbarRules.InAddCode));

        // ── attributes ─────────────────────────────────────────────────────────────────────

        [TestCase("anchor='center'")]
        [TestCase("size='10x10'")]
        [TestCase("width='10'")]
        [TestCase("height='10'")]
        [TestCase("margin='1,1,1,1'")]
        [TestCase("pivot='0.5,0.5'")]
        [TestCase("flow='false'")]
        [TestCase("scale='2'")]
        [TestCase("hidden='true'")]
        [TestCase("width.portrait='10'")]
        public void Layout_attributes_are_rejected(string attr)
            => Assert.IsTrue(Has(Walk($"<ScrollList id='l'><Scrollbar {attr}/></ScrollList>"), ScrollbarRules.LayoutAttrCode));

        [Test]
        public void The_width_message_points_at_thickness()
        {
            var issues = Walk("<ScrollList id='l'><Scrollbar width='6'/></ScrollList>");
            StringAssert.Contains("thickness", First(issues, ScrollbarRules.LayoutAttrCode).Message);
        }

        [Test]
        public void The_hidden_message_points_at_thickness_zero()
        {
            var issues = Walk("<ScrollList id='l'><Scrollbar hidden='true'/></ScrollList>");
            StringAssert.Contains("thickness=\"0\"", First(issues, ScrollbarRules.LayoutAttrCode).Message);
        }

        [Test]
        public void Id_class_if_and_variants_are_fine()
            => Assert.IsFalse(Has(
                Walk("<ScrollList id='l'><Scrollbar id='b' class='bar' if='true' thickness='6' thickness.portrait='4'/></ScrollList>",
                     "<Style name='bar' color='white'/>"),
                ScrollbarRules.LayoutAttrCode));

        [Test]
        public void Children_are_rejected()
            => Assert.IsTrue(Has(Walk("<ScrollList id='l'><Scrollbar><Decor kind='line'/></Scrollbar></ScrollList>"), ScrollbarRules.ChildCode));

        // ── values ─────────────────────────────────────────────────────────────────────────

        [TestCase("thickness='abc'")]
        [TestCase("thickness='-1'")]
        [TestCase("spacing='x'")]
        [TestCase("padding='1,2,3'")]
        [TestCase("padding='-1'")]
        [TestCase("padding='a'")]
        public void Bad_values_are_rejected(string attr)
            => Assert.IsTrue(Has(Walk($"<ScrollList id='l'><Scrollbar {attr}/></ScrollList>"), ScrollbarRules.ValueCode));

        [TestCase("thickness='6'")]
        [TestCase("thickness='0'")]
        [TestCase("spacing='-3'")]
        [TestCase("spacing='6.5'")]
        [TestCase("padding='1.5'")]
        [TestCase("padding='4,1.5'")]
        [TestCase("padding='4, 1.5'")]
        [TestCase("padding='0'")]
        [TestCase("padding=''")]
        [TestCase("thickness=''")]
        [TestCase("spacing=''")]
        public void Good_values_pass(string attr)
            => Assert.IsFalse(Has(Walk($"<ScrollList id='l'><Scrollbar {attr}/></ScrollList>"), ScrollbarRules.ValueCode));

        [Test]
        public void Padding_that_swallows_the_thickness_is_a_value_error()
            => Assert.IsTrue(Has(Walk("<ScrollList id='l'><Scrollbar thickness='6' padding='0,3'/></ScrollList>"), ScrollbarRules.ValueCode));

        [Test]
        public void Padding_that_swallows_the_default_thickness_is_a_value_error_too()
            => Assert.IsTrue(Has(Walk("<ScrollList id='l'><Scrollbar padding='0,10'/></ScrollList>"), ScrollbarRules.ValueCode));

        [Test]
        public void Padding_that_leaves_a_handle_passes()
            => Assert.IsFalse(Has(Walk("<ScrollList id='l'><Scrollbar thickness='6' padding='0,2.5'/></ScrollList>"), ScrollbarRules.ValueCode));

        [Test]
        public void Values_arriving_through_a_class_are_checked_against_each_other()
            => Assert.IsTrue(Has(
                Walk("<ScrollList id='l'><Scrollbar class='thin' padding='0,3'/></ScrollList>",
                     "<Style name='thin' thickness='6'/>"),
                ScrollbarRules.ValueCode));

        [Test]
        public void A_template_placeholder_is_not_a_value()
            => Assert.IsFalse(Has(
                WalkDoc("<Template name='Bar'><Param name='t' default='6'/><Scrollbar thickness='{{t}}'/></Template><Screen name='S'><Frame/></Screen>"),
                ScrollbarRules.ValueCode));

        [Test]
        public void Overlay_with_spacing_is_reported()
            => Assert.IsTrue(Has(Walk("<ScrollList id='l'><Scrollbar overlay='true' spacing='6'/></ScrollList>"), ScrollbarRules.OverlaySpacingCode));

        [Test]
        public void Overlay_false_with_spacing_is_fine()
            => Assert.IsFalse(Has(Walk("<ScrollList id='l'><Scrollbar overlay='false' spacing='6'/></ScrollList>"), ScrollbarRules.OverlaySpacingCode));

        // ── the retired host attributes ────────────────────────────────────────────────────

        [TestCase("ScrollList", "scrollbar=''")]
        [TestCase("ScrollList", "scrollbarColor='white'")]
        [TestCase("ScrollList", "scrollbarHandle=''")]
        [TestCase("ScrollList", "scrollbarHandleColor='white'")]
        [TestCase("ScrollList", "scrollbarWidth='6'")]
        [TestCase("ScrollList", "scrollbarOverlay='true'")]
        [TestCase("ScrollList", "scrollbarWidth.portrait='6'")]
        [TestCase("Dropdown", "scrollbar=''")]
        [TestCase("Dropdown", "scrollbarHandleColor='white'")]
        public void Retired_host_attributes_are_reported(string tag, string attr)
        {
            var issues = Walk($"<{tag} id='x' {attr}/>");
            Assert.IsTrue(Has(issues, ScrollbarRules.RetiredAttrCode));
            StringAssert.Contains("<Scrollbar", First(issues, ScrollbarRules.RetiredAttrCode).Message,
                "the message points at the child element that replaced them");
        }

        [Test]
        public void Retired_attributes_in_a_style_pack_are_reported()
            => Assert.IsTrue(Has(
                Walk("<ScrollList id='l' class='list'/>", "<Style name='list' scrollbar='' scrollbarColor='white'/>"),
                ScrollbarRules.RetiredAttrCode));

        [Test]
        public void Retired_attributes_in_a_theme_style_are_reported()
            => Assert.IsTrue(Has(
                Walk("<ScrollList id='l' class='list'/>",
                     "<Style name='list' color='white'/><Theme name='t'><Style name='list' scrollbarHandleColor='red'/></Theme>"),
                ScrollbarRules.RetiredAttrCode));

        [Test]
        public void A_scrollbar_child_carries_no_retired_attribute()
            => Assert.IsFalse(Has(
                Walk("<ScrollList id='l'><Scrollbar sprite='' color='white' handle='' handleColor='white'/></ScrollList>"),
                ScrollbarRules.RetiredAttrCode));
    }
}
