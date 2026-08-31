using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    // PUI-HUG-TAG / PUI-HUG-SCALE / PUI-HUG-STRETCH-CHILD —
    // spec 2026-08-31-hug-reveal-flip-checked-design §1.3 / §1.4.4.
    public class HugRulesTests
    {
        private static ElementNode Node(string tag, string width = null, string height = null,
                                        string scale = null, string id = "p")
        {
            var n = new ElementNode(tag) { Id = id };
            if (width != null) n.Attributes["width"] = width;
            if (height != null) n.Attributes["height"] = height;
            if (scale != null) n.Attributes["scale"] = scale;
            return n;
        }

        private static IEnumerable<LintIssue> Walk(string body)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                      + body + "</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml));
        }

        [TestCase("hug", true)]
        [TestCase("  hug ", true)]
        [TestCase("clamp(_, hug, 200)", true)]
        [TestCase("clamp(100 , hug , 200)", true)]
        [TestCase("clamp(100, stretch, 200)", false)]
        [TestCase("clamp(100, 46%, 200)", false)]
        [TestCase("stretch", false)]
        [TestCase("200", false)]
        [TestCase("Hug", false)]
        [TestCase(null, false)]
        public void IsHugValue_detects_bare_and_clamped_hug(string value, bool expected)
        {
            Assert.AreEqual(expected, HugRules.IsHugValue(value));
        }

        [Test]
        public void HasHug_sees_variant_overrides()
        {
            var n = Node("VStack", height: "200");
            n.VariantOverrides["height"] = new List<(string, string)> { ("portrait", "hug") };

            Assert.IsTrue(HugRules.HasHug(n, "height"));
            Assert.IsFalse(HugRules.HasHug(n, "width"));
        }

        // ── PUI-HUG-TAG ──────────────────────────────────────────────────────────────────

        [TestCase("VStack")]
        [TestCase("HStack")]
        [TestCase("Grid")]
        [TestCase("ScrollList")]
        public void CheckHugTag_allows_the_containers_with_a_content_size(string tag)
        {
            Assert.IsEmpty(HugRules.CheckHugTag(Node(tag, height: "hug")).ToList());
        }

        [Test]
        public void CheckHugTag_points_a_frame_at_a_stack()
        {
            var issues = HugRules.CheckHugTag(Node("Frame", height: "hug")).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(HugRules.TagCode, issues[0].Code);
            StringAssert.Contains("<VStack>", issues[0].Message);
        }

        [Test]
        public void CheckHugTag_points_a_leaf_at_native()
        {
            var issues = HugRules.CheckHugTag(Node("Image", width: "hug")).ToList();

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("native", issues[0].Message);
        }

        [Test]
        public void CheckHugTag_fires_once_per_node_even_with_two_hug_axes()
        {
            Assert.AreEqual(1, HugRules.CheckHugTag(Node("Frame", width: "hug", height: "hug")).Count());
        }

        [Test]
        public void CheckHugTag_sees_a_clamped_hug()
        {
            Assert.AreEqual(1, HugRules.CheckHugTag(Node("Frame", height: "clamp(_, hug, 200)")).Count());
        }

        [Test]
        public void CheckHugTag_ignores_a_tag_without_hug()
        {
            Assert.IsEmpty(HugRules.CheckHugTag(Node("Frame", height: "clamp(100, 46%, 200)")).ToList());
        }

        // ── PUI-HUG-SCALE ────────────────────────────────────────────────────────────────

        [Test]
        public void CheckHugScale_flags_hug_and_scale_on_one_node()
        {
            var issues = HugRules.CheckHugScale(Node("VStack", height: "hug", scale: "0.5")).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(HugRules.ScaleCode, issues[0].Code);
            StringAssert.Contains("move scale to a child", issues[0].Message);
        }

        [Test]
        public void CheckHugScale_sees_a_variant_hug()
        {
            var n = Node("VStack", height: "200", scale: "0.5");
            n.VariantOverrides["height"] = new List<(string, string)> { ("portrait", "clamp(_, hug, 200)") };

            Assert.AreEqual(1, HugRules.CheckHugScale(n).Count());
        }

        [Test]
        public void CheckHugScale_is_quiet_without_scale()
        {
            Assert.IsEmpty(HugRules.CheckHugScale(Node("VStack", height: "hug")).ToList());
        }

        // ── PUI-HUG-STRETCH-CHILD ────────────────────────────────────────────────────────

        [Test]
        public void CheckHugStretchChild_flags_a_stretch_child_on_the_hugged_axis()
        {
            var parent = Node("VStack", height: "hug");
            var child = Node("Btn", height: "stretch", id: "c");

            var issues = HugRules.CheckHugStretchChild(parent, child).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(HugRules.StretchChildCode, issues[0].Code);
            StringAssert.Contains("collapses to 0", issues[0].Message);
        }

        [Test]
        public void CheckHugStretchChild_ignores_the_cross_axis()
        {
            var parent = Node("VStack", height: "hug");
            var child = Node("Btn", width: "stretch", id: "c");

            Assert.IsEmpty(HugRules.CheckHugStretchChild(parent, child).ToList());
        }

        [Test]
        public void CheckHugStretchChild_follows_the_stack_direction()
        {
            var parent = Node("HStack", width: "hug");
            var child = Node("Btn", width: "stretch*2", id: "c");

            Assert.AreEqual(1, HugRules.CheckHugStretchChild(parent, child).Count());
        }

        [Test]
        public void CheckHugStretchChild_is_quiet_without_a_hugged_parent()
        {
            var parent = Node("VStack", height: "200");
            var child = Node("Btn", height: "stretch", id: "c");

            Assert.IsEmpty(HugRules.CheckHugStretchChild(parent, child).ToList());
        }

        // ── IRWalker dispatch ────────────────────────────────────────────────────────────

        [Test]
        public void Walker_reports_hug_on_a_frame()
        {
            var issues = Walk("<Frame id='f' height='hug'/>").ToList();

            Assert.AreEqual(1, issues.Count(i => i.Code == HugRules.TagCode));
        }

        [Test]
        public void Walker_reports_hug_plus_scale()
        {
            var issues = Walk("<VStack id='v' height='hug' scale='0.5'><Btn height='20'/></VStack>").ToList();

            Assert.AreEqual(1, issues.Count(i => i.Code == HugRules.ScaleCode));
        }

        [Test]
        public void Walker_reports_a_stretch_child_under_a_hugged_stack()
        {
            var issues = Walk("<VStack id='v' height='hug'><Btn id='b' height='stretch'/></VStack>").ToList();

            Assert.AreEqual(1, issues.Count(i => i.Code == HugRules.StretchChildCode));
        }

        [Test]
        public void Walker_is_quiet_on_a_well_formed_hug()
        {
            var issues = Walk("<VStack id='v' anchor='top-left' width='150' height='hug'><Btn id='b' height='20'/></VStack>")
                .ToList();

            Assert.IsEmpty(issues.Where(i => i.Code.StartsWith("PUI-HUG")).ToList());
        }

        [Test]
        public void Walker_sees_hug_arriving_through_a_class()
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>"
                      + "<Style name='card' height='hug'/>"
                      + "<Screen name='S'><Frame id='f' class='card'/></Screen></PromptUGUI>";

            var issues = IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();

            Assert.AreEqual(1, issues.Count(i => i.Code == HugRules.TagCode),
                "a style pack must not be able to smuggle hug onto a tag that cannot measure it");
        }
    }
}
