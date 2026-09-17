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

        // Theme-scoped rules (PUI-THEME-STYLE-SHAPE) run from DocumentLinter, not IRWalker.
        private static List<LintIssue> LintDoc(string body, string extra)
            => DocumentLinter.Walk(UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + extra
                + "<Screen name='S'>" + body + "</Screen></PromptUGUI>")).ToList();

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

        // ── the procedural-surface rules, generalized per layer (M1) ───────────────────────

        [TestCase("<Scrollbar sprite='ui:bar' radius='pill'/>")]
        [TestCase("<Scrollbar handle='ui:knob' handleRadius='pill'/>")]
        [TestCase("<Scrollbar handle='ui:knob' handleGlow='3'/>")]
        [TestCase("<Scrollbar handle='ui:knob' handleBorderWidth='1'/>")]
        public void A_layer_sprite_and_that_layer_shape_conflict(string bar)
            => Assert.IsTrue(Has(Walk($"<ScrollList id='l'>{bar}</ScrollList>"), ProceduralSurfaceRules.SpriteConflictCode));

        [TestCase("<Scrollbar handle='none' handleRadius='pill'/>")]
        [TestCase("<Scrollbar handle='' handleGlow='3'/>")]
        [TestCase("<Scrollbar sprite='ui:bar' handleRadius='pill'/>")]
        [TestCase("<Scrollbar handle='ui:knob' radius='pill'/>")]
        public void Layers_do_not_conflict_across_each_other(string bar)
            => Assert.IsFalse(Has(Walk($"<ScrollList id='l'>{bar}</ScrollList>"), ProceduralSurfaceRules.SpriteConflictCode));

        [TestCase("<Slider id='s' fill='ui:x' fillRadius='4'/>")]
        [TestCase("<Slider id='s' handle='ui:x' handleRadius='pill'/>")]
        [TestCase("<Progress id='p' frame='ui:x' frameRadius='4'/>")]
        public void The_other_controls_inner_layers_get_the_same_check(string node)
            => Assert.IsTrue(Has(Walk(node), ProceduralSurfaceRules.SpriteConflictCode));

        // <Progress>'s primary surface is its FILL (spec 2026-09-18), whose sprite attribute is
        // `fill`, not `sprite` — the primary-surface check has to look there.
        [TestCase("<Progress id='p' fill='ui:x' glow='4'/>")]
        [TestCase("<Progress id='p' fill='ui:x' borderWidth='1'/>")]
        [TestCase("<Progress id='p' fill='ui:x' haze='8'/>")]
        public void Progress_primary_surface_conflicts_on_fill(string node)
            => Assert.IsTrue(Has(Walk(node), ProceduralSurfaceRules.SpriteConflictCode));

        // …except radius: with a bitmap fill it goes to the clip mask, never to the fill.
        [TestCase("<Progress id='p' fill='ui:x' radius='8'/>")]
        [TestCase("<Progress id='p' fill='ui:x' radius='8' bgColor='#222'/>")]
        [TestCase("<Progress id='p' fill='none' glow='4'/>")]
        public void Progress_radius_over_a_bitmap_fill_is_the_mask_path(string node)
            => Assert.IsFalse(Has(Walk(node), ProceduralSurfaceRules.SpriteConflictCode));

        [TestCase("handleRadius='abc'")]
        [TestCase("handleGlow='-1'")]
        [TestCase("handleBorderWidth='x'")]
        public void Handle_shape_values_are_checked(string attr)
            => Assert.IsTrue(Has(Walk($"<ScrollList id='l'><Scrollbar {attr}/></ScrollList>"), StyleRules.ProceduralValueCode));

        [TestCase("handleRadius='pill'")]
        [TestCase("handleRadius='2,2,0,0'")]
        [TestCase("handleGlow='3'")]
        [TestCase("handleBorderWidth='0.4'")]
        public void Good_handle_shape_values_pass(string attr)
            => Assert.IsFalse(Has(Walk($"<ScrollList id='l'><Scrollbar {attr}/></ScrollList>"), StyleRules.ProceduralValueCode));

        [Test]
        public void An_inner_radius_on_a_slider_is_checked_too()
            => Assert.IsTrue(Has(Walk("<Slider id='s' fillRadius='abc'/>"), StyleRules.ProceduralValueCode));

        [Test]
        public void A_base_less_handle_variant_self_heals_when_the_handle_has_no_base_shape()
            => Assert.IsFalse(Has(Walk("<ScrollList id='l'><Scrollbar handleGlow.portrait='3'/></ScrollList>"),
                                  VariantBaseRules.NoBaseCode));

        [Test]
        public void A_base_less_handle_variant_sticks_when_another_handle_attribute_pins_the_surface()
        {
            var issues = Walk("<ScrollList id='l'><Scrollbar handleRadius='pill' handleGlow.portrait='3'/></ScrollList>");
            Assert.IsTrue(Has(issues, VariantBaseRules.NoBaseCode));
            StringAssert.Contains("handleGlow", First(issues, VariantBaseRules.NoBaseCode).Message);
        }

        [Test]
        public void A_base_less_track_variant_is_not_pinned_by_the_handle()
            => Assert.IsFalse(Has(Walk("<ScrollList id='l'><Scrollbar handleRadius='pill' glow.portrait='3'/></ScrollList>"),
                                  VariantBaseRules.NoBaseCode));

        [Test]
        public void A_theme_that_owns_the_whole_handle_set_is_exempt_from_the_shape_rule()
            => Assert.IsFalse(Has(
                LintDoc("<ScrollList id='l'><Scrollbar class='bar'/></ScrollList>",
                     "<Style name='bar' handle='ui:knob'/>"
                     + "<Theme name='px'><Style name='bar' handle='ui:knob'/></Theme>"
                     + "<Theme name='hud'><Style name='bar' handle='none' handleRadius='pill' handleGlow='3'/></Theme>"),
                ThemeStyleRules.ShapeCode));

        [Test]
        public void A_theme_that_holds_half_the_handle_set_is_reported()
            => Assert.IsTrue(Has(
                LintDoc("<ScrollList id='l'><Scrollbar class='bar'/></ScrollList>",
                     "<Style name='bar' handle='ui:knob'/>"
                     + "<Theme name='px'><Style name='bar' handle='ui:knob'/></Theme>"
                     + "<Theme name='hud'><Style name='bar' handle='none' handleRadius='pill' handleGlow='3'/></Theme>"
                     + "<Theme name='half'><Style name='bar' handle='none' handleRadius='pill'/></Theme>"),
                ThemeStyleRules.ShapeCode));
    }
}
