using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;
using PromptScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// <c>hidden</c> / <c>interactable</c> are runtime-owned once code writes them (spec
    /// 2026-09-17-common-attr-runtime-state-design): the declared value is the initial state, a
    /// ReSolve (resize / Variant / Theme) never snaps a code-written value back, and an untouched
    /// node still follows its <c>.variant</c> override and its theme's style pack. Same contract as
    /// <c>Tab.isOn</c> — value comparison against the last applied baseline, baseline refreshed
    /// only on a pass that actually wrote.
    /// </summary>
    public class CommonAttrRuntimeStateTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptScreen Open(string body, string prelude = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + prelude
                      + "<Screen name='S'>" + body + "</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        // Themes only register through the async load path (the sync overload bypasses it).
        private static void LoadAsync(string body)
        {
            var files = new Dictionary<string, string>
            {
                ["main"] = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + body + "</PromptUGUI>",
            };
            UI.SourceResolver = src => AwaitableHelpers.Completed(files.TryGetValue(src, out var v) ? v : null);
            UI.LoadDocumentAsync("main").GetAwaiter().GetResult();
        }

        private static bool Shown(IScreen s, string id) => s.Get(id).GameObject.activeSelf;

        // ── interactable ──────────────────────────────────────────────────────────────────

        [Test]
        public void Undeclared_interactable_disabled_by_code_survives_resolve()
        {
            // The bug as reported: nothing declared, code disables, any ReSolve re-enabled it.
            var s = Open("<Btn id='b'>x</Btn>");
            var b = s.Get<Btn>("b");
            b.Interactable = false;

            s.ReSolve();

            Assert.IsFalse(b.Interactable, "a code-written interactable is runtime-owned");
            Assert.IsFalse(b.GameObject.GetComponent<Button>().interactable, "…and the Button follows it");
        }

        [Test]
        public void Declared_interactable_false_enabled_by_code_survives_resolve()
        {
            var s = Open("<Btn id='b' interactable='false'>x</Btn>");
            var b = s.Get<Btn>("b");
            Assume.That(b.Interactable, Is.False, "guard: the declared value landed");
            b.Interactable = true;

            UI.Variants.Set("mobile", true);   // any ReSolve

            Assert.IsTrue(b.Interactable);
            Assert.IsTrue(b.GameObject.GetComponent<Button>().interactable);
        }

        [Test]
        public void Untouched_interactable_variant_override_self_heals_without_base()
        {
            // interactable keeps its "undeclared = true" default, so a base-less override still
            // reverts when its variant clears (VariantBaseRules: interactable self-heals).
            var s = Open("<Btn id='b' interactable.portrait='false'>x</Btn>");
            var b = s.Get<Btn>("b");
            Assume.That(b.Interactable, Is.True);

            UI.Variants.Set("portrait", true);
            Assert.IsFalse(b.Interactable, "the override applies to an untouched node");

            UI.Variants.Set("portrait", false);
            Assert.IsTrue(b.Interactable, "…and clears back to the default");
        }

        // ── hidden ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Declared_hidden_true_shown_by_code_survives_resolve()
        {
            var s = Open("<Text id='t' hidden='true'>x</Text>");
            Assume.That(Shown(s, "t"), Is.False, "guard: the declared value landed");
            s.Get("t").Hidden = false;

            s.ReSolve();

            Assert.IsTrue(Shown(s, "t"), "a code-written hidden is runtime-owned");
        }

        [Test]
        public void Declared_hidden_false_hidden_by_code_survives_resolve()
        {
            var s = Open("<Frame id='f' hidden='false'/>");
            s.Get("f").Hidden = true;

            UI.Variants.Set("mobile", true);   // any ReSolve

            Assert.IsFalse(Shown(s, "f"));
        }

        [Test]
        public void Lock_baseline_is_not_refreshed_while_locked()
        {
            // The lock must hold across several passes: a locked (skipped) attribute keeps its old
            // baseline, otherwise the second ReSolve would read "unchanged" and replay the XML.
            var s = Open("<Text id='t' hidden='true'>x</Text>");
            s.Get("t").Hidden = false;

            s.ReSolve();
            s.ReSolve();
            UI.Variants.Set("mobile", true);

            Assert.IsTrue(Shown(s, "t"));
        }

        [Test]
        public void Initial_apply_never_locks()
        {
            var s = Open("<Text id='t' hidden='true'>x</Text>");
            s.Get("t").Hidden = false;
            UI.Close("S");

            var again = UI.Open("S");
            Assert.IsFalse(Shown(again, "t"), "a fresh Open starts from the declared value");
        }

        [Test]
        public void Untouched_hidden_variant_override_still_applies()
        {
            var s = Open("<Frame id='f' hidden='false' hidden.portrait='true'/>");
            Assume.That(Shown(s, "f"), Is.True);

            UI.Variants.Set("portrait", true);
            Assert.IsFalse(Shown(s, "f"), "the override reaches an untouched node");

            UI.Variants.Set("portrait", false);
            Assert.IsTrue(Shown(s, "f"), "…and the base value comes back");
        }

        [Test]
        public void Touched_then_variant_flip_keeps_code_value()
        {
            // Code hid it in landscape. portrait declares hidden too (no visible change), and
            // clearing portrait would replay the base hidden='false' — but the node is code-owned.
            var s = Open("<Frame id='f' hidden='false' hidden.portrait='true'/>");
            s.Get("f").Hidden = true;

            UI.Variants.Set("portrait", true);
            Assert.IsFalse(Shown(s, "f"));
            UI.Variants.Set("portrait", false);
            Assert.IsFalse(Shown(s, "f"), "the lock outranks the base value on the way back");
        }

        // ── Theme <Style> packs (the sample's skin layers) ────────────────────────────────

        private const string SkinLayers = @"
            <Style name='skin-wood'  hidden='false'/>
            <Style name='skin-glass' hidden='true'/>
            <Theme name='wood'><Color name='ink' value='#000'/></Theme>
            <Theme name='glass'>
              <Style name='skin-wood'  hidden='true'/>
              <Style name='skin-glass' hidden='false'/>
            </Theme>
            <Screen name='S'><Frame id='w' class='skin-wood'/><Frame id='g' class='skin-glass'/></Screen>";

        [Test]
        public void Theme_style_hidden_switch_still_flips_untouched_layers()
        {
            LoadAsync(SkinLayers);
            UI.Theme.Set("wood");
            var s = UI.Open("S");
            Assume.That(Shown(s, "w"), Is.True);
            Assume.That(Shown(s, "g"), Is.False);

            UI.Theme.Set("glass");

            Assert.IsFalse(Shown(s, "w"), "an untouched layer follows the theme");
            Assert.IsTrue(Shown(s, "g"));

            UI.Theme.Set("wood");
            Assert.IsTrue(Shown(s, "w"));
            Assert.IsFalse(Shown(s, "g"));
        }

        [Test]
        public void Touched_node_stops_following_theme_hidden()
        {
            LoadAsync(SkinLayers);
            UI.Theme.Set("wood");
            var s = UI.Open("S");
            s.Get("w").Hidden = true;          // code took the wood layer over

            s.ReSolve();
            Assert.IsFalse(Shown(s, "w"), "the theme's hidden='false' no longer reaches it");

            UI.Theme.Set("glass");             // declares hidden='true' — nothing to see
            UI.Theme.Set("wood");              // declares hidden='false' — still code-owned
            Assert.IsFalse(Shown(s, "w"));
            Assert.IsFalse(Shown(s, "g"), "the untouched layer still follows the theme");
        }

        // ── Other writers of activeSelf ───────────────────────────────────────────────────

        [Test]
        public void Tab_bound_page_declaring_hidden_false_stays_hidden_after_resolve()
        {
            // The page's declared hidden='false' used to be replayed on top of Tab.bind's hide
            // (and whether TabBar re-hid it depended on _nodeMap order). The Tab's write is a
            // runtime write like any other: the page stays where the Tab put it.
            var s = Open(
                "<TabBar id='bar'><Tab id='a' bind='fa' isOn='true'/><Tab id='b' bind='fb'/></TabBar>" +
                "<Frame id='fa'/><Frame id='fb' hidden='false'/>");
            Assume.That(Shown(s, "fb"), Is.False, "guard: the unselected page is hidden by the Tab");

            s.ReSolve();

            Assert.IsFalse(Shown(s, "fb"));
            Assert.IsTrue(Shown(s, "fa"));
        }

        [Test]
        public void BindItems_row_hidden_written_by_bind_survives_resolve()
        {
            // The host's card pattern: a template param seeds hidden, the bind callback flips it per
            // item, and a resize used to replay the template default over every row.
            var s = Open(
                "<ScrollList id='list' anchor='center' size='200x100' itemTemplate='Row' sprite='none' color='#0000'/>",
                prelude: "<Template name='Row'><Param name='emptyHidden' default='true'/>" +
                         "<HStack height='20'><Text id='empty' hidden='{{emptyHidden}}'>-</Text><Text id='built'>x</Text></HStack>" +
                         "</Template>");
            IControl empty = null;
            s.Get<ScrollList>("list").BindItems(
                R3.Observable.Return<IReadOnlyList<int>>(new[] { 1 }),
                (IControl row, int _) =>
                {
                    empty = row.Get("empty");
                    empty.Hidden = false;
                    row.Get("built").Hidden = true;
                });
            Assume.That(empty.GameObject.activeSelf, Is.True);

            s.ReSolve();

            Assert.IsTrue(empty.GameObject.activeSelf, "the bind's write is runtime-owned");
        }

        [Test]
        public void Instantiate_root_hidden_pool_still_survives_resolve()
        {
            var s = Open("<Frame id='host'/>",
                prelude: "<Template name='Card'><Frame><Text id='t'>x</Text></Frame></Template>");
            var card = s.Instantiate("Card", s.Get("host"));
            card.Hidden = true;               // parked in a pool

            s.ReSolve();
            UI.Variants.Set("mobile", true);

            Assert.IsFalse(card.GameObject.activeSelf);
        }
    }
}
