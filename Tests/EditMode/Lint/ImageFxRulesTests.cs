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
        public void Blur_arriving_through_a_class_is_still_flagged()
        {
            var issues = Walk("<Frame id='f' class='soft'/>", "<Style name='soft' blur='4'/>");
            Assert.IsTrue(issues.Any(i => i.Code == ImageFxRules.TagCode),
                "a style pack hides the attribute from the tag, not from the reader");
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

        // ---- PUI-FX-RADIUS ----

        [TestCase("blur", "13")]
        [TestCase("glow", "12.5")]
        [TestCase("blur", "40")]
        public void A_radius_past_the_kernel_is_a_warning(string attr, string value)
        {
            var issues = Self(Node("Icon", ("name", "ui:x"), (attr, value)));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(ImageFxRules.RadiusCode, issues[0].Code);
            StringAssert.Contains("12", issues[0].Message);
        }

        [TestCase("blur", "12")]
        [TestCase("glow", "8")]
        [TestCase("glow", "")]
        [TestCase("blur", "{{r}}")]
        public void A_radius_the_kernel_can_carry_is_quiet(string attr, string value)
        {
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
