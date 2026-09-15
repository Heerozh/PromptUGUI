using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// PUI-RAYCAST-TAG / PUI-RAYCAST-UNDECIDED — spec 2026-09-15 §5. The first is the usual
    /// "attribute on a tag that drops it" shape; the second is a screen-level walk that asks the
    /// OUTERMOST drawn surface to say whether it catches the pointer, which is exactly the line a
    /// click-through panel is missing.
    /// </summary>
    public class RaycastRulesTests
    {
        private static ElementNode Node(string tag, string raycast = "true")
        {
            var n = new ElementNode(tag) { Id = "p" };
            n.Attributes["raycastTarget"] = raycast;
            return n;
        }

        private static UIDocument Parse(string body, string src = null) =>
            UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>",
                src);

        private static List<LintIssue> Walk(string screenBody, string top = "") =>
            IRWalker.Walk(Parse(top + "<Screen name='S'>" + screenBody + "</Screen>")).ToList();

        private static List<LintIssue> Lint(string body) =>
            DocumentLinter.Walk(Parse(body, "main.ui"), "main.ui").ToList();

        private static List<LintIssue> Undecided(List<LintIssue> issues) =>
            issues.Where(i => i.Code == RaycastRules.UndecidedCode).ToList();

        // ===== PUI-RAYCAST-TAG =====

        [TestCase("Frame")]
        [TestCase("Image")]
        [TestCase("RawImage")]
        [TestCase("Text")]
        public void The_four_tags_that_expose_it_are_quiet(string tag)
        {
            Assert.IsEmpty(RaycastRules.CheckTag(Node(tag)).ToList());
        }

        [TestCase("VStack")]
        [TestCase("HStack")]
        [TestCase("Grid")]
        [TestCase("SafeArea")]
        public void A_pure_container_has_nothing_to_hit(string tag)
        {
            var issues = RaycastRules.CheckTag(Node(tag)).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(RaycastRules.TagCode, issues[0].Code);
            StringAssert.Contains("no Graphic", issues[0].Message);
        }

        [TestCase("Icon")]
        [TestCase("Decor")]
        [TestCase("Progress")]
        public void A_decorative_tag_is_always_click_through(string tag)
        {
            var issues = RaycastRules.CheckTag(Node(tag)).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(RaycastRules.TagCode, issues[0].Code);
            StringAssert.Contains("always click-through", issues[0].Message);
        }

        [TestCase("Btn")]
        [TestCase("Toggle")]
        [TestCase("Tab")]
        [TestCase("Slider")]
        [TestCase("ScrollList")]
        [TestCase("Carousel")]
        [TestCase("TabBar")]
        [TestCase("Markdown")]
        public void An_interactive_tag_catches_by_definition(string tag)
        {
            var issues = RaycastRules.CheckTag(Node(tag, "false")).ToList();

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual(RaycastRules.TagCode, issues[0].Code);
            StringAssert.Contains("by definition", issues[0].Message);
        }

        [Test]
        public void A_template_invocation_is_not_judged()
        {
            Assert.IsEmpty(RaycastRules.CheckTag(Node("Card")).ToList(),
                "a non-builtin tag is a template whose root the raw pass cannot see");
        }

        [Test]
        public void Tag_rule_sees_the_attribute_through_a_class()
        {
            var issues = Walk("<VStack id='v' class='hit'/>", "<Style name='hit' raycastTarget='true'/>");

            Assert.IsTrue(issues.Any(i => i.Code == RaycastRules.TagCode && i.Id == "v"));
        }

        [Test]
        public void Tag_rule_runs_from_the_walker()
        {
            Assert.IsTrue(Walk("<HStack id='h' raycastTarget='true'/>")
                .Any(i => i.Code == RaycastRules.TagCode && i.Id == "h"));
        }

        // ===== PUI-RAYCAST-UNDECIDED =====

        [Test]
        public void An_outermost_drawn_frame_that_does_not_say_is_reported()
        {
            var issues = Undecided(Walk("<Frame id='panel' color='#fff' radius='8'><Text>hi</Text></Frame>"));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("panel", issues[0].Id);
            StringAssert.Contains("raycastTarget=\"true\"", issues[0].Message);
        }

        [Test]
        public void An_outermost_image_is_a_drawn_surface_too()
        {
            var issues = Undecided(Walk("<Image id='backdrop' anchor='stretch' color='#000000A0'/>"));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("backdrop", issues[0].Id);
        }

        [Test]
        public void A_bare_frame_is_not_a_surface()
        {
            Assert.IsEmpty(Undecided(Walk("<Frame id='wrap' anchor='stretch'><Text>hi</Text></Frame>")));
        }

        [Test]
        public void Declaring_true_covers_everything_inside()
        {
            var issues = Undecided(Walk(
                "<Frame id='panel' color='#fff' raycastTarget='true'>" +
                "  <Frame id='inner' color='#000'/>" +
                "  <Image id='pic'/>" +
                "</Frame>"));

            Assert.IsEmpty(issues, "inside a catcher every surface is already covered");
        }

        [Test]
        public void Declaring_false_keeps_looking_inside()
        {
            var issues = Undecided(Walk(
                "<Image id='vignette' anchor='stretch' raycastTarget='false'>" +
                "  <Frame id='panel' color='#fff'/>" +
                "</Image>"));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("panel", issues[0].Id);
        }

        [Test]
        public void Only_the_outermost_undecided_surface_is_reported()
        {
            var issues = Undecided(Walk(
                "<Frame id='panel' color='#fff'>" +
                "  <Frame id='inner' color='#000'/>" +
                "</Frame>"));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("panel", issues[0].Id, "fix the outer one, re-lint, and the inner one shows if it still matters");
        }

        [Test]
        public void An_interactive_control_covers_its_children()
        {
            Assert.IsEmpty(Undecided(Walk(
                "<Btn id='b'><Frame class='ghost' anchor='stretch' color='#fff'/></Btn>")));
        }

        [Test]
        public void An_interactive_control_itself_is_never_reported()
        {
            Assert.IsEmpty(Undecided(Walk("<Btn id='b' color='#fff'>OK</Btn><Progress id='p' fillColor='#fff'/>")));
        }

        [Test]
        public void Layout_containers_are_looked_through()
        {
            var issues = Undecided(Walk(
                "<SafeArea><VStack><HStack><Frame id='panel' color='#fff'/></HStack></VStack></SafeArea>"));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("panel", issues[0].Id);
        }

        [Test]
        public void The_decision_may_come_from_a_class()
        {
            Assert.IsEmpty(Undecided(Walk("<Frame id='panel' class='panel'/>",
                "<Style name='panel' color='#fff' raycastTarget='true'/>")));
        }

        [Test]
        public void The_surface_may_come_from_a_class()
        {
            var issues = Undecided(Walk("<Frame id='card' class='card'/>", "<Style name='card' color='#fff'/>"));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("card", issues[0].Id);
        }

        [Test]
        public void An_unresolvable_class_stays_quiet()
        {
            Assert.IsEmpty(Undecided(Walk("<Frame id='card' class='fromLibrary'/>")),
                "the class may well carry raycastTarget; guessing would fail clean documents");
        }

        [Test]
        public void A_template_body_is_not_judged_where_it_is_declared()
        {
            var issues = Undecided(Lint(
                "<Template name='Skin'><Frame color='#fff'/></Template>" +
                "<Screen name='S'><Btn id='b'><Skin/></Btn></Screen>"));

            Assert.IsEmpty(issues, "inside the Btn the skin is covered; only the expanded tree knows that");
        }

        [Test]
        public void An_expanded_template_root_is_judged_in_place_and_points_at_the_invocation()
        {
            var issues = Undecided(Lint(
                "<Template name='Skin'><Frame color='#fff'/></Template>" +
                "<Screen name='S'><Skin id='skin'/></Screen>"));

            Assert.AreEqual(1, issues.Count);
            Assert.IsNotNull(issues[0].Via, "the body line alone cannot say which invocation");
        }

        [Test]
        public void An_add_block_child_is_treated_as_outermost()
        {
            var issues = Undecided(Walk(
                "<Frame id='root' anchor='stretch'/>" +
                "<Variant when='portrait'><Add into='#root'><Frame id='sheet' color='#fff'/></Add></Variant>"));

            Assert.AreEqual(1, issues.Count);
            Assert.AreEqual("sheet", issues[0].Id);
        }
    }
}
