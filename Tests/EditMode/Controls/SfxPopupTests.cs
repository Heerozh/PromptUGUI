using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using static PromptUGUI.Tests.EditMode.Controls.SfxTestKit;
using PromptScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Expand / collapse cues of <c>&lt;TabMenu&gt;</c>, <c>&lt;Popover&gt;</c> and <c>&lt;Collapsible&gt;</c> (spec 2026-11-23-ui-sfx-cues
    /// §7.4, §7.6, §7.7): the user's own open and close only — one cue per gesture, for the popup it was aimed at; a pick's
    /// close, code, attribute passes, a cascade, a replaced or covered popup and accordion siblings raise nothing.
    /// </summary>
    public class SfxPopupTests
    {
        private List<SfxCue> _cues;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            _cues = Record();
        }

        [TearDown]
        public void TearDown()
        {
            PopupBounds.BoundsOverride = null;
            UI.ResetForTests();
        }

        private static GameObject Blocker(PromptScreen s)
        {
            foreach (Transform t in s.RootGameObject.GetComponentsInChildren<Transform>(true))
                if (t.name == PopupCore.BlockerName && t.gameObject.activeInHierarchy) return t.gameObject;
            Assert.Fail("an open popup's catcher");
            return null;
        }

        private static void Escape() => EscapeChain.Dispatch(EscapeKey.Escape, EscapeChain.NextTestPress());

        // ───── TabMenu ─────

        private const string Menu =
            "<TabMenu id='m' anchor='top-left' transition='0' {0}>" +
            "<Tab id='a' text='A' isOn='true'/><Tab id='b' text='B'/><Tab id='c' text='C' interactable='false'/>" +
            "</TabMenu>";

        private static PromptScreen OpenMenu(string attrs = "") => OpenScreen(string.Format(Menu, attrs));

        [Test]
        public void Clicking_the_handle_expands_with_a_cue_before_OnExpanded()
        {
            var s = OpenMenu();
            var m = s.Get<TabMenu>("m");
            var order = new List<string>();
            UI.Sfx.OnCue.Subscribe(c => order.Add(c.Key));
            m.OnExpanded.Subscribe(_ => order.Add("expanded")).AddTo(s);

            Click(m.GameObject);

            Assert.IsTrue(m.IsExpanded);
            CollectionAssert.AreEqual(new[] { "tabmenu.expand", "expanded" }, order);
        }

        [Test]
        public void Picking_another_row_cues_only_its_select()
        {
            var s = OpenMenu();
            s.Get<TabMenu>("m").Expand();

            Click(s.Get<Tab>("b").GameObject);

            Assert.IsFalse(s.Get<TabMenu>("m").IsExpanded, "a pick closes the menu");
            Assert.AreEqual("[tab.select→tab.select]", Describe(_cues), "the close is the pick's consequence");
        }

        [Test]
        public void Re_picking_the_current_row_cues_collapse()
        {
            var s = OpenMenu();
            s.Get<TabMenu>("m").Expand();

            Click(s.Get<Tab>("a").GameObject);

            Assert.IsFalse(s.Get<TabMenu>("m").IsExpanded);
            Assert.AreEqual("[tabmenu.collapse→tabmenu.collapse]", Describe(_cues));
        }

        [Test]
        public void A_disabled_row_cues_only_its_deny()
        {
            var s = OpenMenu();
            s.Get<TabMenu>("m").Expand();

            Click(s.Get<Tab>("c").GameObject);

            Assert.AreEqual("[tab.deny→tab.deny]", Describe(_cues));
        }

        [Test]
        public void Escape_and_the_catcher_collapse_with_a_cue()
        {
            var s = OpenMenu();
            var m = s.Get<TabMenu>("m");

            m.Expand();
            Escape();
            Assert.IsFalse(m.IsExpanded);

            m.Expand();
            Click(Blocker(s));
            Assert.IsFalse(m.IsExpanded);

            Assert.AreEqual("[tabmenu.collapse→tabmenu.collapse, tabmenu.collapse→tabmenu.collapse]", Describe(_cues));
        }

        [Test]
        public void Code_opening_and_closing_the_menu_cues_nothing()
        {
            var s = OpenMenu();
            var m = s.Get<TabMenu>("m");
            m.Expand();
            m.Collapse();
            m.Expand();
            m.Interactable = false;
            Assert.IsEmpty(_cues, Describe(_cues));
        }

        [Test]
        public void A_disabled_handle_cues_deny_and_the_menu_looks_up_its_own_sfx()
        {
            var s = OpenScreen(string.Format(Menu, "interactable='false'") +
                               "<TabMenu id='n' anchor='top-right' transition='0' sfx='expand:open_menu'><Tab id='x' text='X' isOn='true'/></TabMenu>");

            Click(s.Get<TabMenu>("m").GameObject);
            Click(s.Get<TabMenu>("n").GameObject);

            Assert.AreEqual("[tabmenu.deny→tabmenu.deny, tabmenu.expand→open_menu]", Describe(_cues));
        }

        // ───── Popover ─────

        private const string Pop =
            "<Btn id='b' anchor='center' size='40x40'>B</Btn><Btn id='o' anchor='top-left' size='40x40'>O</Btn>" +
            "<Popover id='p' on='click@b'><Btn id='c' width='80' height='30'>C</Btn></Popover>" +
            "<Popover id='q' on='click@c'><Frame width='60' height='20'/></Popover>" +
            "<Popover id='r' on='click@o'><Frame width='60' height='20'/></Popover>";

        [Test]
        public void The_trigger_opens_and_closes_with_cues_after_its_click()
        {
            var s = OpenScreen(Pop);
            var p = s.Get<Popover>("p");
            var order = new List<string>();
            UI.Sfx.OnCue.Subscribe(c => order.Add(c.Key));
            p.OnOpened.Subscribe(_ => order.Add("opened")).AddTo(s);

            Click(s.Get<Btn>("b").GameObject);
            Assert.IsTrue(p.IsOpen);
            Click(s.Get<Btn>("b").GameObject);
            Assert.IsFalse(p.IsOpen);

            CollectionAssert.AreEqual(
                new[] { "btn.click", "popover.expand", "opened", "btn.click", "popover.collapse" }, order);
        }

        [Test]
        public void Code_and_a_coded_trigger_click_cue_nothing()
        {
            var s = OpenScreen(Pop);
            var p = s.Get<Popover>("p");
            p.Open(s.Get<Btn>("b"));
            p.Close();
            s.Get<Btn>("b").GameObject.GetComponent<PuiButton>().onClick.Invoke();
            Assert.IsTrue(p.IsOpen, "the coded click still opens it");
            Assert.IsEmpty(_cues, Describe(_cues));
        }

        [Test]
        public void Escape_closes_the_top_popover_with_a_cue()
        {
            var s = OpenScreen(Pop);
            Click(s.Get<Btn>("b").GameObject);
            _cues.Clear();

            Escape();

            Assert.AreEqual("[popover.collapse→popover.collapse]", Describe(_cues));
            Assert.AreSame(s.Get<Popover>("p"), _cues[0].Source);
        }

        [Test]
        public void An_outside_press_on_nested_popovers_cues_once_for_the_one_it_was_aimed_at()
        {
            var s = OpenScreen(Pop);
            Click(s.Get<Btn>("b").GameObject);   // p
            Click(s.Get<Btn>("c").GameObject);   // q, inside p's panel: its child
            Assume.That(s.Get<Popover>("q").IsOpen, "q opened as p's child");
            _cues.Clear();

            PopupChain.DismissOutside(null, Time.frameCount + 1);

            Assert.IsFalse(s.Get<Popover>("p").IsOpen);
            Assert.AreEqual("[popover.collapse→popover.collapse]", Describe(_cues), "q closed along with p");
            Assert.AreSame(s.Get<Popover>("p"), _cues[0].Source);
        }

        [Test]
        public void A_replaced_popover_closes_silently()
        {
            var s = OpenScreen(Pop);
            Click(s.Get<Btn>("b").GameObject);
            _cues.Clear();

            Click(s.Get<Btn>("o").GameObject);

            Assert.IsFalse(s.Get<Popover>("p").IsOpen, "an unrelated popup opening replaces it");
            Assert.AreEqual("[btn.click→btn.click, popover.expand→popover.expand]", Describe(_cues));
            Assert.AreSame(s.Get<Popover>("r"), _cues[1].Source);
        }

        // ───── Collapsible ─────

        private static GameObject Header(Collapsible c) => c.GameObject.transform.Find("Header").gameObject;

        [Test]
        public void Clicking_the_header_cues_expand_and_collapse_before_its_events()
        {
            var s = OpenScreen("<Collapsible id='c' text='T' expanded='false' transition='0'><Btn id='r' height='30'/></Collapsible>");
            var c = s.Get<Collapsible>("c");
            var order = new List<string>();
            UI.Sfx.OnCue.Subscribe(x => order.Add(x.Key));
            c.OnToggled.Subscribe(v => order.Add("toggled:" + v)).AddTo(s);

            Click(Header(c));
            Click(Header(c));

            CollectionAssert.AreEqual(
                new[] { "collapsible.expand", "toggled:True", "collapsible.collapse", "toggled:False" }, order);
        }

        [Test]
        public void Code_and_a_variant_fold_cue_nothing()
        {
            var s = OpenScreen("<Collapsible id='c' text='T' expanded='false' expanded.mobile='true' transition='0'><Btn height='30'/></Collapsible>");
            var c = s.Get<Collapsible>("c");
            c.Expand();
            c.Collapse();
            c.Toggle();
            try { UI.Variants.Set("mobile", true); }
            finally { UI.Variants.Set("mobile", false); }
            Assert.IsEmpty(_cues, Describe(_cues));
        }

        [Test]
        public void An_accordion_sibling_folds_silently()
        {
            var s = OpenScreen(
                "<Collapsible id='a' group='g' text='A' transition='0'><Btn height='30'/></Collapsible>" +
                "<Collapsible id='b' group='g' text='B' expanded='false' transition='0'><Btn height='30'/></Collapsible>");

            Click(Header(s.Get<Collapsible>("b")));

            Assert.IsFalse(s.Get<Collapsible>("a").IsExpanded, "the group closed a");
            Assert.AreEqual("[collapsible.expand→collapsible.expand]", Describe(_cues));
            Assert.AreSame(s.Get<Collapsible>("b"), _cues[0].Source);
        }

        [Test]
        public void A_disabled_header_cues_deny()
        {
            var s = OpenScreen("<Collapsible id='c' text='T' interactable='false' transition='0'><Btn height='30'/></Collapsible>");
            Click(Header(s.Get<Collapsible>("c")));
            Assert.AreEqual("[collapsible.deny→collapsible.deny]", Describe(_cues));
        }
    }
}
