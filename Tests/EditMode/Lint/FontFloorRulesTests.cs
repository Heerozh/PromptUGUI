using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    /// <summary>
    /// <c>PUI-FONT-FLOOR</c>: the size a text actually renders at on each target screen, in dp,
    /// against the project's floor (2026-10-12 font-floor spec). The numbers are the spec's §1 table:
    /// an iPhone SE (750x1334@2x) under reference 1080x1920 is 0.694 px per unit = 0.347 dp per unit.
    /// </summary>
    public class FontFloorRulesTests
    {
        private const string SE = "SE=750x1334@2x";
        private const string SELandscape = "SE-L=1334x750@2x";
        private const string ProMax = "ProMax=1320x2868@3x";
        private const string Phone16 = "16=1179x2556@3x";

        private static UIDocument Parse(string body, string src = "main.ui.xml") =>
            UIDocumentParser.Parse(
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>", src);

        /// <summary>Each target is <c>name=screen</c> or <c>name=screen|variants</c>.</summary>
        private static FontFloorPolicy Policy(float floor, params string[] targets)
        {
            var rows = new List<FontFloorTarget>();
            foreach (var t in targets)
            {
                var eq = t.IndexOf('=');
                var bar = t.IndexOf('|');
                rows.Add(new FontFloorTarget
                {
                    name = t.Substring(0, eq),
                    screen = bar < 0 ? t.Substring(eq + 1) : t.Substring(eq + 1, bar - eq - 1),
                    variants = bar < 0 ? null : t.Substring(bar + 1),
                });
            }
            return new FontFloorPolicy { Floor = floor, Targets = rows, TextDefaultSize = 36f };
        }

        private static List<LintIssue> Lint(string body, FontFloorPolicy policy) =>
            DocumentLinter.Walk(Parse(body), "main.ui.xml", fontFloor: policy)
                .Where(i => i.Code == FontFloorRules.Code).ToList();

        private static List<LintIssue> Lint(string body, params string[] targets) =>
            Lint(body, Policy(11f, targets));

        private static string Screen(string children, string attrs = "reference='1080x1920'") =>
            $"<Screen name='S' {attrs}>{children}</Screen>";

        // ---- §8-1, 2: the canvas factor of one device -------------------------------------------

        [Test]
        public void A_text_below_the_floor_on_a_target_is_reported_with_the_fix()
        {
            var issues = Lint(Screen("<Text id='hint' fontSize='28'/>"), SE);

            Assert.AreEqual(1, issues.Count);
            var m = issues[0].Message;
            StringAssert.Contains("<Text id='hint'>", m);
            StringAssert.Contains("fontSize 28 renders at 9.7dp on 'SE'", m);
            StringAssert.Contains("750x1334@2x portrait", m);
            StringAssert.Contains("canvas ×0.694 from reference 1080x1920", m);
            StringAssert.Contains("below the 11dp floor", m);
            StringAssert.Contains("Needs fontSize ≥ 32", m);
            Assert.AreEqual("main.ui.xml", issues[0].Origin);
            Assert.Greater(issues[0].Line, 0);
        }

        [Test]
        public void The_same_text_on_a_larger_device_passes()
        {
            CollectionAssert.IsEmpty(Lint(Screen("<Text fontSize='28'/>"), ProMax));
        }

        // ---- §8-3, 4: orientation picks the variant ---------------------------------------------

        [Test]
        public void Each_orientation_resolves_its_own_reference()
        {
            var issues = Lint(
                Screen("<Text fontSize='28'/>", "reference='1280x720' reference.portrait='1080x1920'"),
                SE, SELandscape);

            Assert.AreEqual(1, issues.Count, "landscape: 1334/1280 → 0.52dp per unit, 28 → 14.6dp passes");
            StringAssert.Contains("on 'SE' (750x1334@2x portrait", issues[0].Message);
            StringAssert.Contains("from reference 1080x1920", issues[0].Message);
        }

        [Test]
        public void A_variant_fontSize_is_resolved_per_target_and_named()
        {
            var issues = Lint(
                Screen("<Text fontSize='40' fontSize.landscape='16'/>",
                       "reference='1080x1920' reference.landscape='1920x1080'"),
                SE, SELandscape);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("fontSize.landscape 16 renders at 5.6dp on 'SE-L'", issues[0].Message);
        }

        // ---- §8-5: the scale chain --------------------------------------------------------------

        [Test]
        public void An_ancestor_scale_multiplies_in_and_is_named()
        {
            var issues = Lint(Screen("<Frame scale='0.5'><Text fontSize='40'/></Frame>"), SE);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("renders at 6.9dp", issues[0].Message);
            StringAssert.Contains("scale ×0.5", issues[0].Message);
            StringAssert.Contains("Needs fontSize ≥ 64", issues[0].Message);
        }

        [Test]
        public void A_device_density_scale_cancels_the_canvas_factor()
        {
            // scale="2x": exactly 2 physical px per unit whatever the reference → 10 × 2 / 2 = 10dp.
            var body = Screen("<Frame scale='2x'><Text fontSize='10'/></Frame>")
                       + "<Screen name='T' reference='2160x3840'><Frame scale='2x'><Text fontSize='10'/></Frame></Screen>";
            var issues = Lint(body, SE);

            Assert.AreEqual(2, issues.Count);
            foreach (var i in issues) StringAssert.Contains("renders at 10.0dp", i.Message);
        }

        [Test]
        public void A_relative_scale_snaps_like_the_runtime()
        {
            // f = 0.694, 0.5r → round(0.347) = 0 → clamped to 1 px per unit → 20 / 2 = 10dp.
            var issues = Lint(Screen("<Frame scale='0.5r'><Text fontSize='20'/></Frame>"), SE);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("renders at 10.0dp", issues[0].Message);
            Assert.AreEqual(1f / (750f / 1080f), ScaleSyntax.LocalScale("0.5r", 750f / 1080f), 1e-5f);
        }

        // ---- §8-6, 7, 8: scale-mode, no reference, world canvas -----------------------------------

        [Test]
        public void Pixel_mode_uses_the_integer_ladder()
        {
            var body = Screen("<Text fontSize='2'/>", "scale-mode='pixel' reference='320x180'");

            CollectionAssert.IsEmpty(Lint(body, "FHD=1920x1080@1x"), "factor 6 → 12dp");
            var issues = Lint(body, "Laptop=1366x768@1x");
            Assert.AreEqual(1, issues.Count, "factor floor(4.27) = 4 → 8dp");
            StringAssert.Contains("renders at 8.0dp", issues[0].Message);
            StringAssert.Contains("pixel canvas ×4 from reference 320x180", issues[0].Message);
        }

        [Test]
        public void Pixel_mode_below_one_snaps_to_a_half()
        {
            var issues = Lint(Screen("<Text fontSize='20'/>", "scale-mode='pixel' reference='1920x1080'"),
                              "Laptop=1366x768@1x");

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("renders at 10.0dp", issues[0].Message);
        }

        [Test]
        public void No_reference_means_one_pixel_per_unit()
        {
            var issues = Lint(Screen("<Text fontSize='14'/>", ""), "TV=1920x1080@2x");

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("renders at 7.0dp", issues[0].Message);
            StringAssert.Contains("canvas ×1, no reference", issues[0].Message);
        }

        [Test]
        public void A_world_canvas_is_skipped()
        {
            CollectionAssert.IsEmpty(
                Lint(Screen("<Text fontSize='8'/>", "canvas='world' reference='1080x1920'"), SE));
        }

        // ---- §8-9: which nodes carry text, and their default sizes --------------------------------

        [TestCase("<Btn text='OK'/>", "default fontSize 24")]
        [TestCase("<Btn>OK</Btn>", "default fontSize 24")]
        [TestCase("<Toggle text='Sound'/>", "default fontSize 14")]
        [TestCase("<InputField/>", "default fontSize 14")]
        [TestCase("<Collapsible text='More'/>", "default fontSize 14")]
        [TestCase("<TabMenu/>", "default fontSize 24")]
        [TestCase("<Markdown/>", "default fontSize 16")]
        [TestCase("<Text/>", "default fontSize 36")]
        [TestCase("<TabBar><Tab text='A'/></TabBar>", "default fontSize 24")]
        public void A_tag_without_fontSize_is_checked_at_its_default(string node, string expected)
        {
            // A 20dp floor so even <Text>'s 36 (12.5dp on the SE) falls below it.
            var issues = Lint(Screen(node), Policy(20f, SE));

            Assert.AreEqual(1, issues.Count, node);
            StringAssert.Contains(expected, issues[0].Message);
        }

        [TestCase("<Btn/>")]
        [TestCase("<Btn sprite='ui:x'/>")]
        [TestCase("<Toggle/>")]
        [TestCase("<Collapsible/>")]
        [TestCase("<TabBar><Tab/></TabBar>")]
        [TestCase("<Frame/>")]
        [TestCase("<Image/>")]
        public void A_tag_that_shows_no_text_is_not_checked(string node)
        {
            CollectionAssert.IsEmpty(Lint(Screen(node), SE), node);
        }

        [Test]
        public void The_text_default_comes_from_the_policy()
        {
            var policy = Policy(11f, SE);
            policy.TextDefaultSize = 48f;   // 48 × 0.347 = 16.7dp

            CollectionAssert.IsEmpty(Lint(Screen("<Text/>"), policy));
        }

        [Test]
        public void A_dropdown_has_no_fontSize_so_the_fix_is_a_scale()
        {
            var issues = Lint(Screen("<Dropdown/>"), SE);

            Assert.AreEqual(1, issues.Count);
            var m = issues[0].Message;
            StringAssert.Contains("fixed fontSize 14 renders at 4.9dp", m);
            StringAssert.Contains("has no fontSize attribute", m);
            StringAssert.Contains("Needs scale ≥ 2.27 on this node", m);
            StringAssert.DoesNotContain("Needs fontSize", m);
        }

        // ---- §8-10, 11: templates and class= ----------------------------------------------------

        [Test]
        public void A_template_finding_names_the_template_line_and_folds_identical_instances()
        {
            var issues = Lint(
                "<Template name='Label'><Param name='s' default='28'/><Text id='t' fontSize='{{s}}'/></Template>"
                + Screen("<Label/><Label/><Frame scale='0.5'><Label/></Frame>"), SE);

            Assert.AreEqual(2, issues.Count, "two instances at 9.7dp fold into one; the scaled one is its own");
            Assert.IsTrue(issues.All(i => i.Origin == "main.ui.xml" && i.Via != null));
            Assert.IsTrue(issues.Any(i => i.Message.Contains("renders at 9.7dp")));
            Assert.IsTrue(issues.Any(i => i.Message.Contains("renders at 4.9dp")));
        }

        [Test]
        public void A_fontSize_from_a_class_names_the_class()
        {
            var issues = Lint("<Style name='caption' fontSize='20'/>"
                              + Screen("<Text class='caption'/>"), SE);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("fontSize 20 (class=\"caption\") renders at 6.9dp", issues[0].Message);
        }

        // ---- §8-12: themes ----------------------------------------------------------------------

        [Test]
        public void A_theme_that_shrinks_a_class_is_reported_under_that_theme()
        {
            var issues = Lint("<Style name='caption' fontSize='40'/>"
                              + "<Theme name='dark'><Style name='caption' fontSize='20'/></Theme>"
                              + Screen("<Text class='caption'/>"), SE);

            Assert.AreEqual(1, issues.Count, "the base pack (40 → 13.9dp) passes");
            StringAssert.Contains("renders at 6.9dp", issues[0].Message);
            StringAssert.EndsWith("(under <Theme name='dark'>)", issues[0].Message);
        }

        [Test]
        public void A_theme_that_leaves_the_size_alone_adds_nothing()
        {
            var issues = Lint("<Style name='caption' fontSize='20' color='#000'/>"
                              + "<Theme name='dark'><Style name='caption' fontSize='20' color='#fff'/></Theme>"
                              + Screen("<Text class='caption'/>"), SE);

            Assert.AreEqual(1, issues.Count);
            StringAssert.DoesNotContain("under <Theme", issues[0].Message);
        }

        // ---- §8-13: variant <Add> blocks --------------------------------------------------------

        [Test]
        public void An_Add_block_is_checked_only_where_its_variant_is_active_and_inherits_the_target_scale()
        {
            var issues = Lint(
                Screen("<Frame id='panel' scale='0.5'/>"
                       + "<Variant when='mobile'><Add into='#panel'><Text fontSize='40'/></Add></Variant>"),
                SE, "SE-mobile=750x1334@2x|mobile");

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("renders at 6.9dp on 'SE-mobile'", issues[0].Message);
            StringAssert.DoesNotContain("Also below", issues[0].Message);
        }

        [Test]
        public void An_Add_path_reaches_into_a_template_instance()
        {
            var issues = Lint(
                "<Template name='Card'><Frame><Frame id='body' scale='0.5'/></Frame></Template>"
                + Screen("<Card id='card'/>"
                         + "<Variant when='mobile'><Add into='#card/body'><Text fontSize='40'/></Add></Variant>"),
                "SE-mobile=750x1334@2x|mobile");

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("renders at 6.9dp", issues[0].Message);
        }

        [Test]
        public void An_Add_into_a_missing_target_is_skipped_quietly()
        {
            Assert.DoesNotThrow(() => Lint(
                Screen("<Variant when='mobile'><Add into='#nope'><Text fontSize='4'/></Add></Variant>"),
                "SE-mobile=750x1334@2x|mobile"));
        }

        // ---- §8-14: itemTemplate rows -----------------------------------------------------------

        [Test]
        public void ItemTemplate_rows_are_checked_under_the_list()
        {
            var issues = Lint(
                "<Template name='Row'><Text id='label' fontSize='20'/></Template>"
                + Screen("<ScrollList id='list' itemTemplate='Row' scale='0.8' width='300' height='400'/>"), SE);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("<Text id='label'>", issues[0].Message);
            StringAssert.Contains("renders at 5.6dp", issues[0].Message);
        }

        [Test]
        public void An_itemTemplate_with_a_required_param_is_skipped()
        {
            CollectionAssert.IsEmpty(Lint(
                "<Template name='Row'><Param name='s'/><Text fontSize='{{s}}'/></Template>"
                + Screen("<ScrollList itemTemplate='Row' width='300' height='400'/>"), SE));
        }

        // ---- §8-15, 16: aggregation and the boundary --------------------------------------------

        [Test]
        public void Several_failing_targets_make_one_finding_led_by_the_worst()
        {
            var issues = Lint(Screen("<Text fontSize='28'/>"), Phone16, SE, ProMax);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("renders at 9.7dp on 'SE'", issues[0].Message);
            StringAssert.Contains("Also below on '16' (10.2dp).", issues[0].Message);
            StringAssert.DoesNotContain("ProMax", issues[0].Message);
        }

        [Test]
        public void Exactly_the_floor_passes()
        {
            // reference 1080x1920 on a 1080x1920@2x screen: 0.5dp per unit.
            CollectionAssert.IsEmpty(Lint(Screen("<Text fontSize='22'/>"), "X=1080x1920@2x"));
            Assert.AreEqual(1, Lint(Screen("<Text fontSize='21.9'/>"), "X=1080x1920@2x").Count);
        }

        // ---- a node-level fontFloor (badges and the like) --------------------------------------

        [Test]
        public void A_node_fontFloor_replaces_the_floor_for_itself()
        {
            CollectionAssert.IsEmpty(Lint(Screen("<Text fontSize='28' fontFloor='9'/>"), SE), "9.7dp ≥ 9");

            var issues = Lint(Screen("<Text fontSize='28' fontFloor='10'/>"), SE);
            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("below the 10dp floor (fontFloor=\"10\" on this node)", issues[0].Message);
            StringAssert.Contains("Needs fontSize ≥ 29", issues[0].Message);
        }

        [Test]
        public void An_ancestor_fontFloor_covers_its_subtree_only()
        {
            var issues = Lint(Screen(
                "<Frame id='badge' fontFloor='7'><Text id='n' fontSize='17'/></Frame><Text id='other' fontSize='22'/>"), SE);

            Assert.AreEqual(2, issues.Count);
            var badge = issues.Single(i => i.Id == "n").Message;
            StringAssert.Contains("renders at 5.9dp", badge);
            StringAssert.Contains("below the 7dp floor (fontFloor=\"7\" on <Frame id='badge'>)", badge);
            StringAssert.Contains("Needs fontSize ≥ 21", badge);
            StringAssert.Contains("below the 11dp floor.", issues.Single(i => i.Id == "other").Message,
                "outside the frame the project floor applies");
        }

        [Test]
        public void The_nearest_fontFloor_wins()
        {
            var issues = Lint(Screen(
                "<Frame fontFloor='7'><Frame id='strict' fontFloor='12'><Text fontSize='28'/></Frame></Frame>"), SE);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("below the 12dp floor (fontFloor=\"12\" on <Frame id='strict'>)", issues[0].Message);
        }

        [Test]
        public void A_zero_fontFloor_exempts_the_subtree()
        {
            CollectionAssert.IsEmpty(Lint(Screen("<Frame fontFloor='0'><Text fontSize='4'/></Frame>"), SE));
        }

        [Test]
        public void A_fontFloor_from_a_class_pack_names_the_class()
        {
            var issues = Lint("<Style name='badge-text' fontSize='9' fontFloor='7'/>"
                              + Screen("<Text class='badge-text'/>"), SE);

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("below the 7dp floor (fontFloor=\"7\" on this node via class=\"badge-text\")",
                issues[0].Message);
            CollectionAssert.IsEmpty(Lint("<Style name='badge-text' fontSize='21' fontFloor='7'/>"
                                          + Screen("<Text class='badge-text'/>"), SE));
        }

        [Test]
        public void A_variant_fontFloor_follows_the_target()
        {
            var issues = Lint(Screen("<Text fontSize='22' fontFloor='7' fontFloor.mobile='9'/>"),
                              SE, "SE-mobile=750x1334@2x|mobile");

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("on 'SE-mobile'", issues[0].Message);
            StringAssert.Contains("below the 9dp floor (fontFloor.mobile=\"9\" on this node)", issues[0].Message);
        }

        [Test]
        public void With_different_floors_the_largest_shortfall_leads()
        {
            // SE: 6.9dp against 7 (fix 21); ProMax-mobile: 8.1dp against 10 (fix 25) — the higher dp,
            // yet the bigger shortfall, and the fix that satisfies both.
            var issues = Lint(Screen("<Text fontSize='20' fontFloor='7' fontFloor.mobile='10'/>"),
                              SE, "ProMax-mobile=1320x2868@3x|mobile");

            Assert.AreEqual(1, issues.Count);
            StringAssert.Contains("on 'ProMax-mobile'", issues[0].Message);
            StringAssert.Contains("Needs fontSize ≥ 25", issues[0].Message);
            StringAssert.Contains("Also below on 'SE' (6.9dp)", issues[0].Message);
        }

        [Test]
        public void A_template_invocation_accepts_fontFloor_for_its_instance()
        {
            const string tpl = "<Template name='Badge'><Frame><Text fontSize='18'/></Frame></Template>";

            var issues = Lint(tpl + Screen("<Badge fontFloor='7'/>"), SE);
            Assert.AreEqual(1, issues.Count, "6.2dp under the instance's 7dp floor");
            StringAssert.Contains("below the 7dp floor", issues[0].Message);
            CollectionAssert.IsEmpty(Lint(tpl + Screen("<Badge fontFloor='6'/>"), SE));
        }

        [TestCase("<Text fontFloor='small'/>")]
        [TestCase("<Text fontFloor='-1'/>")]
        [TestCase("<Text fontFloor='7' fontFloor.mobile='big'/>")]
        public void A_fontFloor_that_is_not_a_size_is_reported(string node)
        {
            var issues = DocumentLinter.Walk(Parse(Screen(node)))
                .Where(i => i.Code == FontFloorRules.ValueCode).ToList();

            Assert.AreEqual(1, issues.Count, node);
            StringAssert.Contains("fontFloor", issues[0].Message);
        }

        [TestCase("<Text fontFloor='0'/>")]
        [TestCase("<Text fontFloor='7.5' fontFloor.mobile='9'/>")]
        public void A_fontFloor_that_is_a_size_is_fine(string node)
        {
            CollectionAssert.IsEmpty(DocumentLinter.Walk(Parse(Screen(node)))
                .Where(i => i.Code == FontFloorRules.ValueCode).ToList());
        }

        [Test]
        public void A_variant_only_fontFloor_needs_no_base()
        {
            // Lint-only: nothing at runtime applies it, so nothing can get stuck.
            var n = new ElementNode("Text");
            n.VariantOverrides["fontFloor"] = new List<(string, string)> { ("mobile", "9") };
            CollectionAssert.IsEmpty(VariantBaseRules.Check(n));
        }

        // ---- §8-17, 18: when the rule stays quiet ----------------------------------------------

        [Test]
        public void An_inactive_policy_reports_nothing()
        {
            var body = Screen("<Text fontSize='4'/>");
            CollectionAssert.IsEmpty(Lint(body, (FontFloorPolicy)null));
            CollectionAssert.IsEmpty(Lint(body, Policy(0f, SE)));
            CollectionAssert.IsEmpty(Lint(body, Policy(11f)));
            CollectionAssert.IsEmpty(Lint(body, Policy(11f, "bad=750*1334")));
        }

        [Test]
        public void Without_an_expanded_pass_the_rule_is_silent()
        {
            // An Import nobody can resolve: the expanded pass is skipped, the raw pass never runs it.
            var issues = DocumentLinter.Walk(
                    Parse("<Import src='lib'/>" + Screen("<Text fontSize='4'/>")), "main.ui.xml",
                    imports: null, fontFloor: Policy(11f, SE))
                .Where(i => i.Code == FontFloorRules.Code).ToList();

            CollectionAssert.IsEmpty(issues);
        }
    }
}
