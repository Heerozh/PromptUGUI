using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    // PUI-FLIP-TAG / PUI-FLIP-VALUE — spec 2026-08-31-hug-reveal-flip-checked-design §3.3.
    public class RotateFlipRulesTests
    {
        private static ElementNode Node(string tag, string rotation = null, string flip = null)
        {
            var n = new ElementNode(tag) { Id = "p" };
            if (rotation != null) n.Attributes["rotation"] = rotation;
            if (flip != null) n.Attributes["flip"] = flip;
            return n;
        }

        private static List<LintIssue> Walk(string body)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                      + body + "</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        [TestCase("Image")]
        [TestCase("Icon")]
        [TestCase("RawImage")]
        public void The_three_mesh_leaves_are_allowed(string tag)
        {
            Assert.IsEmpty(RotateFlipRules.Check(Node(tag, rotation: "90")).ToList());
        }

        [TestCase("Btn")]
        [TestCase("Frame")]
        [TestCase("Text")]
        [TestCase("VStack")]
        public void Any_other_tag_is_flagged(string tag)
        {
            var issues = RotateFlipRules.Check(Node(tag, rotation: "90")).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(RotateFlipRules.TagCode, issues[0].Code);
            StringAssert.Contains("inner <Image>", issues[0].Message);
        }

        [Test]
        public void Flip_on_a_container_is_flagged_too()
        {
            var issues = RotateFlipRules.Check(Node("Frame", flip: "x")).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(RotateFlipRules.TagCode, issues[0].Code);
            StringAssert.Contains("flip=", issues[0].Message);
        }

        [Test]
        public void A_tag_without_either_attribute_is_quiet()
        {
            Assert.IsEmpty(RotateFlipRules.Check(Node("Frame")).ToList());
        }

        [TestCase("x")]
        [TestCase("y")]
        [TestCase("xy")]
        [TestCase("none")]
        [TestCase("")]
        public void Valid_flip_values_pass(string value)
        {
            Assert.IsEmpty(RotateFlipRules.Check(Node("Image", flip: value)).ToList());
        }

        [TestCase("z")]
        [TestCase("yx")]
        [TestCase("X")]
        [TestCase("horizontal")]
        public void Invalid_flip_values_are_flagged(string value)
        {
            var issues = RotateFlipRules.Check(Node("Image", flip: value)).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(RotateFlipRules.ValueCode, issues[0].Code);
        }

        [TestCase("90")]
        [TestCase("-45")]
        [TestCase("22.5")]
        [TestCase("{{angle}}")]
        public void Valid_rotation_values_pass(string value)
        {
            Assert.IsEmpty(RotateFlipRules.Check(Node("Icon", rotation: value)).ToList());
        }

        [TestCase("90deg")]
        [TestCase("quarter")]
        public void Invalid_rotation_values_are_flagged(string value)
        {
            var issues = RotateFlipRules.Check(Node("Icon", rotation: value)).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(RotateFlipRules.ValueCode, issues[0].Code);
            StringAssert.Contains("clockwise degrees", issues[0].Message);
        }

        [Test]
        public void Variant_values_are_checked_as_well()
        {
            var n = Node("Image", flip: "x");
            n.VariantOverrides["flip"] = new List<(string, string)> { ("portrait", "z") };

            var issues = RotateFlipRules.Check(n).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(RotateFlipRules.ValueCode, issues[0].Code);
        }

        // ── IRWalker dispatch ────────────────────────────────────────────────────────────

        [Test]
        public void Walker_reports_rotation_on_a_button()
        {
            var issues = Walk("<Btn id='b' rotation='90'>x</Btn>");

            Assert.AreEqual(1, issues.Count(i => i.Code == RotateFlipRules.TagCode));
        }

        [Test]
        public void Walker_sees_flip_arriving_through_a_class()
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                      + "<Style name='mirrored' flip='x'/>"
                      + "<Screen name='S'><Frame id='f' class='mirrored'/></Screen></PromptUGUI>";

            var issues = IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Code == RotateFlipRules.TagCode));
        }

        [Test]
        public void Walker_is_quiet_on_a_rotated_icon()
        {
            var issues = Walk("<Icon id='i' name='ui:x' rotation='180'/>");

            Assert.IsEmpty(issues.Where(i => i.Code.StartsWith("PUI-FLIP")).ToList());
        }
    }
}
