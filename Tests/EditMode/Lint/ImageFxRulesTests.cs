using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    // PUI-FX-TAG / -TYPE / -ATTR / -MASK / -RADIUS — spec 2026-09-02 §6.
    public class ImageFxRulesTests
    {
        private static ElementNode Node(string tag, params (string, string)[] attrs)
        {
            var n = new ElementNode(tag) { Id = "p" };
            foreach (var (k, v) in attrs) n.Attributes[k] = v;
            return n;
        }

        private static List<LintIssue> Tag(ElementNode n) => ImageFxRules.CheckTag(n).ToList();

        private static List<LintIssue> Self(ElementNode n) =>
            ImageFxRules.CheckImage(n, StyleAttributeView.Empty).ToList();

        /// <summary><c>&lt;Style&gt;</c> packs are top-level, so they go in <paramref name="top"/>;
        /// the nodes wearing them go in <paramref name="body"/>.</summary>
        private static List<LintIssue> Walk(string body, string top = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + top +
                      "<Screen name='S'>" + body + "</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        // ---- PUI-FX-TAG ----

        [TestCase("Image")]
        [TestCase("Icon")]
        public void Blur_is_fine_on_a_sprite_graphic(string tag)
        {
            Assert.IsEmpty(Tag(Node(tag, ("blur", "4"))));
        }

        [TestCase("Frame")]
        [TestCase("Btn")]
        [TestCase("Text")]
        [TestCase("VStack")]
        public void Blur_on_anything_else_is_flagged(string tag)
        {
            var issues = Tag(Node(tag, ("blur", "4")));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFxRules.TagCode, issues[0].Code);
            StringAssert.Contains("<Image>", issues[0].Message);
        }

        [Test]
        public void Blur_on_a_RawImage_says_it_is_not_in_this_milestone()
        {
            var issues = Tag(Node("RawImage", ("blur", "4")));

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("RawImage", issues[0].Message);
        }

        [Test]
        public void A_template_parameter_is_never_judged()
        {
            Assert.IsEmpty(Tag(Node("Frame", ("blur", "{{amount}}"))));
        }

        [Test]
        public void A_template_invocation_is_not_judged_by_its_parameters()
        {
            // On the raw pass an invocation is a node named after its template, and its attributes
            // can only be <Param>s (TemplateExpander throws on anything else); the expanded pass
            // judges the real node in place. A parameter that happens to be called blur is not
            // blur= on the wrong tag.
            var issues = Walk("<Card id='c' blur='4'/>",
                              "<Template name='Card'><Param name='blur' default='0'/>" +
                              "<Image sprite='ui:x' blur='{{blur}}'/></Template>");

            Assert.IsFalse(issues.Any(i => i.Code == ImageFxRules.TagCode),
                string.Join("\n", issues.Select(i => i.Message)));
        }

        [Test]
        public void Blur_arriving_through_a_class_is_still_flagged()
        {
            var issues = Walk("<Frame id='f' class='soft'/>", "<Style name='soft' blur='4'/>");
            Assert.IsTrue(issues.Any(i => i.Code == ImageFxRules.TagCode),
                "a style pack hides the attribute from the tag, not from the reader");
        }

        // ---- PUI-FX-TAG: grayscale (spec 2026-10-06) ----

        [TestCase("Image")]
        [TestCase("Icon")]
        public void Grayscale_is_fine_on_a_sprite_graphic(string tag)
        {
            Assert.IsEmpty(Tag(Node(tag, ("grayscale", "true"))));
        }

        [TestCase("Frame")]
        [TestCase("VStack")]
        [TestCase("Decor")]
        public void Grayscale_on_anything_else_is_flagged(string tag)
        {
            var issues = Tag(Node(tag, ("grayscale", "true")));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFxRules.TagCode, issues[0].Code);
            StringAssert.Contains("grayscale=", issues[0].Message);
            StringAssert.Contains("<Image> / <Icon>", issues[0].Message);
        }

        [TestCase("RawImage", "tint=")]
        [TestCase("Text", "color=")]
        [TestCase("Btn", "interactable=\"false\"")]
        [TestCase("Collapsible", "interactable=\"false\"")]
        public void Grayscale_on_a_tag_that_has_its_own_way_says_which(string tag, string hint)
        {
            var issues = Tag(Node(tag, ("grayscale", "true")));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFxRules.TagCode, issues[0].Code);
            StringAssert.Contains(hint, issues[0].Message);
        }

        [Test]
        public void Blur_and_grayscale_on_the_wrong_tag_are_two_findings()
        {
            Assert.AreEqual(2, Tag(Node("Frame", ("blur", "4"), ("grayscale", "true"))).Count);
        }

        [Test]
        public void Grayscale_arriving_through_a_class_is_still_flagged()
        {
            var issues = Walk("<Frame id='f' class='locked'/>", "<Style name='locked' grayscale='true'/>");
            Assert.IsTrue(issues.Any(i => i.Code == ImageFxRules.TagCode));
        }

        [Test]
        public void A_grayscale_parameter_is_judged_neither_at_the_call_nor_in_the_body()
        {
            var issues = Walk("<ItemSlot id='s' grayscale='true'/>",
                              "<Template name='ItemSlot'><Param name='grayscale' default='false'/>" +
                              "<Icon name='ui:x' grayscale='{{grayscale}}'/></Template>");

            Assert.IsFalse(issues.Any(i => i.Code == ImageFxRules.TagCode || i.Code == ImageFxRules.ValueCode),
                string.Join("\n", issues.Select(i => i.Message)));
        }

        // ---- PUI-FX-TYPE ----

        [TestCase("sliced")]
        [TestCase("tiled")]
        [TestCase("filled")]
        public void Fx_on_a_non_simple_type_is_an_error(string type)
        {
            var issues = Self(Node("Image", ("sprite", "ui:x"), ("glow", "6"), ("type", type)));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFxRules.TypeCode, issues[0].Code);
            StringAssert.Contains("simple", issues[0].Message);
        }

        [TestCase("simple")]
        [TestCase("contain")]
        [TestCase("cover")]
        public void The_types_that_draw_one_quad_are_fine(string type)
        {
            Assert.IsEmpty(Self(Node("Image", ("sprite", "ui:x"), ("glow", "6"), ("type", type))));
        }

        [TestCase("sliced")]
        [TestCase("tiled")]
        public void Intensity_needs_no_quad_so_any_type_is_fine(string type)
        {
            // Exposure runs on whatever the Image draws; only blur / glow need the single quad.
            Assert.IsEmpty(Self(Node("Image", ("sprite", "ui:x"), ("intensity", "3"), ("type", type))));
        }

        [Test]
        public void No_type_at_all_is_fine()
        {
            // The sprite may still turn out to be a 9-slice, but only the runtime can see that —
            // it warns there instead (FxImageTests).
            Assert.IsEmpty(Self(Node("Image", ("sprite", "ui:x"), ("blur", "4"))));
        }

        [Test]
        public void The_type_is_judged_after_the_class_is_merged()
        {
            var issues = Walk("<Image id='m' class='card' sprite='ui:x' glow='6'/>",
                              "<Style name='card' type='sliced'/>");
            Assert.IsTrue(issues.Any(i => i.Code == ImageFxRules.TypeCode));
        }

        // ---- PUI-FX-ATTR ----

        [Test]
        public void A_glowColor_with_no_glow_draws_nothing()
        {
            var issues = Self(Node("Icon", ("name", "ui:x"), ("glowColor", "#ff0000")));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFxRules.AttrCode, issues[0].Code);
        }

        [Test]
        public void A_glowColor_beside_a_zero_glow_draws_nothing_either()
        {
            var issues = Self(Node("Icon", ("name", "ui:x"), ("glow", "0"), ("glowColor", "#ff0000")));
            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFxRules.AttrCode, issues[0].Code);
        }

        [Test]
        public void A_glowColor_with_a_glow_is_the_normal_case()
        {
            Assert.IsEmpty(Self(Node("Icon", ("name", "ui:x"), ("glow", "6"), ("glowColor", "#ff0000"))));
        }

        // ---- PUI-FX-MASK ----

        [Test]
        public void Fx_on_a_stencil_mask_source_is_a_warning()
        {
            var issues = Self(Node("Image", ("sprite", "ui:x"), ("glow", "6"), ("mask", "self")));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFxRules.MaskCode, issues[0].Code);
        }

        [Test]
        public void Fx_with_a_rect_mask_is_fine()
        {
            Assert.IsEmpty(Self(Node("Image", ("sprite", "ui:x"), ("glow", "6"), ("mask", "rect"))));
        }

        // ---- PUI-FX-VALUE (spec 2026-10-06) ----

        [TestCase("maybe")]
        [TestCase("")]
        [TestCase("1")]
        [TestCase("yes")]
        public void A_grayscale_that_is_not_a_bool_is_flagged(string value)
        {
            var issues = Self(Node("Icon", ("name", "ui:x"), ("grayscale", value)));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFxRules.ValueCode, issues[0].Code);
            StringAssert.Contains("true or false", issues[0].Message);
        }

        [TestCase("true")]
        [TestCase("False")]
        [TestCase(" true ")]
        [TestCase("{{locked}}")]
        public void A_bool_or_a_template_parameter_is_fine(string value)
        {
            // The same acceptance as the runtime's bool.Parse: case and surrounding spaces don't matter.
            Assert.IsEmpty(Self(Node("Image", ("sprite", "ui:x"), ("grayscale", value))));
        }

        [Test]
        public void An_empty_grayscale_says_how_a_variant_goes_back()
        {
            // "" is the way back for intensity / glow, so it is the likely mistake here (GS-D4).
            var issues = Self(Node("Icon", ("name", "ui:x"), ("grayscale", "")));
            StringAssert.Contains("=\"false\"", issues[0].Message);
        }

        [Test]
        public void A_variant_value_is_judged_too()
        {
            var issues = Walk("<Icon id='i' name='ui:x' grayscale='false' grayscale.locked='yes'/>");
            Assert.AreEqual(1, issues.Count(i => i.Code == ImageFxRules.ValueCode));
        }

        [Test]
        public void The_runtime_check_leaves_the_value_to_the_parser()
        {
            // ControlMeta's bool.Parse already refuses it, with the node's location; a warning on top
            // would be a second message for one mistake.
            Assert.IsEmpty(ImageFxRules.CheckImage(Node("Icon", ("name", "ui:x"), ("grayscale", "maybe"))).ToList());
        }

        // ---- radius: not lint's call ----

        [TestCase("blur", "7")]
        [TestCase("glow", "6.5")]
        [TestCase("blur", "40")]
        [TestCase("blur", "6")]
        [TestCase("glow", "4")]
        [TestCase("glow", "")]
        [TestCase("blur", "{{r}}")]
        public void A_radius_is_never_judged_by_its_size(string attr, string value)
        {
            // Whether a radius leaves gaps between the kernel's taps depends on the texture's mip
            // chain and on the drawn size, neither of which lint can see — so a static threshold
            // nags just as loudly at an atlas that HAS mipmaps, and no number would not. The whole
            // diagnostic is FxImage.WarnIfKernelLeavesGaps (spec §14.5): per texture, in texels,
            // and only when the fragment really has to stay on the lod-0 kernel.
            Assert.IsEmpty(Self(Node("Icon", ("name", "ui:x"), (attr, value))));
        }

        // ---- values: the existing numeric rule covers them ----

        [TestCase("blur", "abc")]
        [TestCase("blur", "-1")]
        public void A_bad_radius_is_reported_by_the_shared_pixel_rule(string attr, string value)
        {
            // blur joins borderWidth / glow / innerGlow in StyleRules' pixel-value check rather than
            // getting a code of its own — one grammar, one message, one place to fix it.
            var issues = StyleRules.Check(Node("Image", (attr, value))).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(StyleRules.ProceduralValueCode, issues[0].Code);
        }

        [Test]
        public void A_bad_radius_inside_a_style_pack_is_reported_too()
        {
            var issues = Walk("<Image id='m' class='soft'/>", "<Style name='soft' blur='nope'/>");
            Assert.IsTrue(issues.Any(i => i.Code == StyleRules.ProceduralValueCode));
        }

        // ---- registration ----

        [Test]
        public void The_walker_runs_these_rules_for_Icon_too()
        {
            // <Icon> had no self-check branch in IRWalker before this feature; without one the whole
            // set would be silently dead on half the tags it covers.
            var issues = Walk("<Icon id='i' name='ui:x' glowColor='#fff'/>");
            Assert.IsTrue(issues.Any(i => i.Code == ImageFxRules.AttrCode));
        }
    }
}
