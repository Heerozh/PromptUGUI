using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>The sfx= grammar, the lookup chain and the three lint rules (spec 2026-11-23-ui-sfx-cues §5, §8).</summary>
    public class SfxRulesTests
    {
        private static UIDocument Parse(string body) =>
            UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>");

        private static List<LintIssue> Raw(string body) => IRWalker.Walk(Parse(body)).ToList();

        private static List<LintIssue> Both(string body) => DocumentLinter.Walk(Parse(body)).ToList();

        private static int Count(IEnumerable<LintIssue> issues, string code) => issues.Count(i => i.Code == code);

        private static string Screen(string inner) => $"<Screen name='S'>{inner}</Screen>";

        private static SfxSpec Spec(string value)
        {
            Assert.IsTrue(SfxRules.TryParse(value, out var spec, out var error), error);
            return spec;
        }

        // ───── grammar ─────

        [Test]
        public void A_bare_key_covers_every_action_but_deny()
        {
            var s = Spec("confirm");
            Assert.IsTrue(s.TryDecide("click", out var key));
            Assert.AreEqual("confirm", key);
            Assert.IsTrue(s.TryDecide("expand", out key));
            Assert.AreEqual("confirm", key);
            Assert.IsFalse(s.TryDecide("deny", out _), "deny is the library's refusal, not the element's own action");
        }

        [Test]
        public void None_mutes_every_action_but_deny()
        {
            var s = Spec("none");
            Assert.IsTrue(s.TryDecide("click", out var key));
            Assert.IsNull(key, "decided: muted");
            Assert.IsFalse(s.TryDecide("deny", out _));

            var all = Spec("none deny:none");
            Assert.IsTrue(all.TryDecide("deny", out key));
            Assert.IsNull(key);
        }

        [Test]
        public void Named_actions_beat_the_bare_key_and_mix_with_it()
        {
            var s = Spec("drawer collapse:none");
            Assert.IsTrue(s.TryDecide("expand", out var key));
            Assert.AreEqual("drawer", key);
            Assert.IsTrue(s.TryDecide("collapse", out key));
            Assert.IsNull(key);

            var t = Spec("checked:switch_on, unchecked:switch_off");
            Assert.IsTrue(t.TryDecide("unchecked", out key));
            Assert.AreEqual("switch_off", key);
            Assert.IsFalse(t.TryDecide("click", out _), "no bare key: an unnamed action falls through");

            Assert.IsTrue(Spec("deny:locked").TryDecide("deny", out key));
            Assert.AreEqual("locked", key);
        }

        [Test]
        public void Keys_are_free_form_but_for_spaces_commas_and_colons()
        {
            Assert.AreEqual("ui/confirm", Spec("ui/confirm").Bare);
            Assert.AreEqual("pages.flip", Spec("pages.flip").Bare);
            Assert.AreEqual("Confirm", Spec("Confirm").Bare, "keys are the game's, case kept");
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Empty_is_not_written(string value) => Assert.IsTrue(Spec(value).IsEmpty);

        [TestCase("a b", "two keys")]
        [TestCase("click:", "no key")]
        [TestCase(":x", "no action")]
        [TestCase("click:a click:b", "twice")]
        [TestCase("Click:a", "not an action name")]
        [TestCase("click:a:b", "cannot contain ':'")]
        public void Malformed_values_are_refused(string value, string fragment)
        {
            Assert.IsFalse(SfxRules.TryParse(value, out var spec, out var error));
            StringAssert.Contains(fragment, error);
            Assert.IsTrue(spec.IsEmpty, "a refused value reads as not written");
        }

        [Test]
        public void The_chain_is_read_nearest_first_and_falls_back_to_the_default_key()
        {
            var tab = Spec("special");
            var bar = Spec("select:page_turn");
            Assert.AreEqual("special", SfxSpec.Resolve(new[] { tab, bar }, "select", "tab.select"));
            Assert.AreEqual("page_turn", SfxSpec.Resolve(new[] { SfxSpec.Empty, bar }, "select", "tab.select"));
            Assert.AreEqual("tab.deny", SfxSpec.Resolve(new[] { tab, bar }, "deny", "tab.deny"), "neither decides deny");
            Assert.IsNull(SfxSpec.Resolve(new[] { Spec("none"), bar }, "select", "tab.select"), "a mute decides");
            Assert.AreEqual("tab.select", SfxSpec.Resolve(new SfxSpec[] { null, SfxSpec.Empty }, "select", "tab.select"));
            Assert.AreEqual("btn.click", SfxRules.DefaultKey("btn", "click"));
        }

        // ───── PUI-SFX-VALUE ─────

        [Test]
        public void Value_is_checked_inline_on_variants_in_styles_in_theme_styles_and_on_the_screen()
        {
            Assert.AreEqual(1, Count(Raw(Screen("<Btn sfx='click:'/>")), SfxRules.ValueCode), "inline");
            Assert.AreEqual(1, Count(Raw(Screen("<Btn sfx.mobile='a b'/>")), SfxRules.ValueCode), "variant");
            Assert.AreEqual(1, Count(Raw("<Style name='s' sfx='click:'/>" + Screen("")), SfxRules.ValueCode), "style");
            Assert.AreEqual(1, Count(Raw("<Style name='s' sfx='x'/><Theme name='t'><Style name='s' sfx=':x'/></Theme>"
                                         + Screen("")), SfxRules.ValueCode), "theme style");
            Assert.AreEqual(1, Count(Raw("<Screen name='S' sfx='open:'/>"), SfxRules.ValueCode), "<Screen>");
            Assert.AreEqual(0, Count(Raw(Screen("<Btn sfx='confirm deny:none'/>")), SfxRules.ValueCode));
        }

        [Test]
        public void A_template_placeholder_is_not_judged()
        {
            Assert.AreEqual(0, Count(Raw("<Template name='B'><Param name='s' default=''/><Btn sfx='{{s}}'/></Template>"
                                         + Screen("")), SfxRules.ValueCode));
        }

        // ───── PUI-SFX-ACTION ─────

        [Test]
        public void An_action_the_tag_never_does_is_reported()
        {
            Assert.AreEqual(1, Count(Raw(Screen("<Btn sfx='checked:x'/>")), SfxRules.ActionCode));
            Assert.AreEqual(1, Count(Raw(Screen("<Toggle sfx='click:x'/>")), SfxRules.ActionCode));
            Assert.AreEqual(1, Count(Raw(Screen("<Popover sfx='deny:x'/>")), SfxRules.ActionCode), "a popover has no deny");
            Assert.AreEqual(1, Count(Raw("<Screen name='S' sfx='click:x'/>"), SfxRules.ActionCode));
            Assert.AreEqual(1, Count(Raw(Screen("<TabBar sfx='expand:x'/>")), SfxRules.ActionCode));
        }

        [Test]
        public void A_misspelt_action_is_reported()
        {
            var issue = Raw(Screen("<Btn sfx='clik:x'/>")).Single(i => i.Code == SfxRules.ActionCode);
            StringAssert.Contains("clik", issue.Message);
        }

        [Test]
        public void The_actions_each_element_takes_are_not_reported()
        {
            Assert.AreEqual(0, Count(Raw(Screen(
                "<Btn sfx='click:a deny:b'/><Toggle sfx='checked:a unchecked:b deny:c'/>" +
                "<TabBar sfx='select:a deselect:b deny:c'><Tab sfx='select:a'>x</Tab></TabBar>" +
                "<TabMenu sfx='expand:a collapse:b select:c'/>" +
                "<Dropdown sfx='expand:a select:b collapse:c deny:d'/>" +
                "<Popover sfx='expand:a collapse:b'/><Collapsible sfx='expand:a collapse:b deny:c'/>" +
                "<Carousel sfx='flip:a'/><Frame drop='item' sfx='land:a'/>")), SfxRules.ActionCode));
            Assert.AreEqual(0, Count(Raw("<Screen name='S' sfx='open:a close:b'/>"), SfxRules.ActionCode));
        }

        [Test]
        public void Lift_land_and_return_are_allowed_anywhere()
        {
            Assert.AreEqual(0, Count(Raw(Screen("<Btn sfx='lift:a land:b return:c'/><Frame sfx='lift:a'/>")),
                                     SfxRules.ActionCode),
                "drag and drop or reorder can be switched on by the call site, the list, or C#");
        }

        [Test]
        public void Styles_and_template_body_roots_are_not_judged_for_actions()
        {
            var body = "<Style name='shared' sfx='click:a checked:b expand:c'/>" +
                       "<Template name='Row'><HStack sfx='click:x'/></Template>" +
                       Screen("<Btn class='shared'/><Toggle class='shared'/><Collapsible class='shared'/>");
            Assert.AreEqual(0, Count(Both(body), SfxRules.ActionCode));
        }

        [Test]
        public void An_action_written_at_the_call_site_is_judged_against_the_instance_root()
        {
            var body = "<Template name='Primary'><Btn/></Template>" + Screen("<Primary sfx='checked:x'/>");
            Assert.AreEqual(1, Count(Both(body), SfxRules.ActionCode));
        }

        // ───── PUI-SFX-INERT ─────

        [Test]
        public void Sfx_on_an_element_that_never_sounds_is_reported()
        {
            Assert.AreEqual(1, Count(Raw(Screen("<Frame sfx='x'/>")), SfxRules.InertCode));
            Assert.AreEqual(1, Count(Raw(Screen("<Text sfx='x'>t</Text>")), SfxRules.InertCode));
            Assert.AreEqual(1, Count(Raw(Screen("<ScrollList sfx='x'/>")), SfxRules.InertCode), "no reorder");
        }

        [Test]
        public void Sfx_on_Pages_is_reported_with_the_button_recipe()
        {
            var issue = Raw(Screen("<Pages sfx='x' selected='a'><Frame id='a'/></Pages>"))
                .Single(i => i.Code == SfxRules.InertCode);
            StringAssert.Contains("pages.flip", issue.Message);
        }

        [Test]
        public void Elements_that_sound_or_are_looked_up_are_not_reported()
        {
            Assert.AreEqual(0, Count(Raw(Screen(
                "<Btn sfx='a'/><TabBar sfx='a'/><Popover sfx='a'/><Carousel sfx='a'/>" +
                "<Frame drag='unit' sfx='a'/><Frame drop='unit' sfx='a'/><Frame drag='none' sfx='a'/>" +
                "<ScrollList reorder='true' sfx='a'/><ScrollList reorder.mobile='true' sfx='a'/>")), SfxRules.InertCode));
            Assert.AreEqual(0, Count(Raw("<Screen name='S' sfx='none'/>"), SfxRules.InertCode));
        }

        [Test]
        public void Inert_exemptions()
        {
            Assert.AreEqual(0, Count(Both("<Template name='Row'><HStack sfx='a'/></Template>" + Screen("")),
                                     SfxRules.InertCode),
                "a template body's root may be a list row, or get drag= from its call site");
            Assert.AreEqual(0, Count(Raw(Screen("<ScrollList reorder='true'><HStack sfx='a'/></ScrollList>")),
                                     SfxRules.InertCode),
                "a static row of a sortable list");
            Assert.AreEqual(0, Count(Raw(Screen("<Image sfx='lift:pick'/>")), SfxRules.InertCode),
                "drag actions written: the author turns drag on from C#");
            Assert.AreEqual(0, Count(Both("<Style name='s' sfx='a'/>" + Screen("<Text class='s'>t</Text>")),
                                     SfxRules.InertCode),
                "what a style broadcasts");
            Assert.AreEqual(0, Count(Raw("<Style name='d' drag='unit'/>" + Screen("<Frame class='d' sfx='a'/>")),
                                     SfxRules.InertCode),
                "drag= arriving through a style");
        }

        [Test]
        public void A_call_site_sfx_on_a_template_whose_root_never_sounds_is_reported()
        {
            var body = "<Template name='Card'><VStack><Btn id='buy'>Buy</Btn></VStack></Template>"
                       + Screen("<Card sfx='buy'/>");
            var issue = Both(body).Single(i => i.Code == SfxRules.InertCode);
            StringAssert.Contains("<Param", issue.Message);
        }

        [Test]
        public void A_call_site_sfx_on_a_template_whose_root_sounds_is_fine()
        {
            Assert.AreEqual(0, Count(Both("<Template name='Primary'><Btn sfx='confirm'/></Template>"
                                          + Screen("<Primary sfx='none'/>")), SfxRules.InertCode));
            Assert.AreEqual(0, Count(Both("<Template name='Row'><HStack sfx='a'/></Template>" + Screen("<Row/>")),
                                     SfxRules.InertCode),
                "the body's own value on a root that does not sound is the body's business — it may be a row elsewhere");
        }

        // ───── self-heal ─────

        [Test]
        public void A_base_less_sfx_variant_is_not_stuck()
        {
            Assert.AreEqual(0, Count(Both(Screen("<Btn sfx.mobile='none'/>")), VariantBaseRules.NoBaseCode));
        }

        [Test]
        public void Themes_may_disagree_on_sfx()
        {
            var issues = Both("<Style name='btn' color='#fff'/>" +
                              "<Theme name='a'><Style name='btn' sfx='retro'/></Theme>" +
                              "<Theme name='b'><Style name='btn' color='#000'/></Theme>" +
                              Screen("<Btn class='btn'/>"));
            Assert.IsEmpty(issues.Where(i => i.Code == ThemeStyleRules.ShapeCode),
                "sfx self-heals, so switching to a theme without it resets it");
        }
    }
}
