using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using static PromptUGUI.Tests.EditMode.Controls.SfxTestKit;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Toggle&gt;</c> and <c>&lt;Tab&gt;</c> cues (spec 2026-11-23-ui-sfx-cues §7.3, §7.4): only a real flip the user made, only
    /// on the control the user touched, before its own C# events.
    /// </summary>
    public class SfxToggleTabTests
    {
        private List<SfxCue> _cues;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _cues = Record();
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        // ───── Toggle ─────

        [Test]
        public void Clicking_a_Toggle_cues_checked_then_unchecked_before_OnValueChanged()
        {
            var s = OpenScreen("<Toggle id='t'>Music</Toggle>");
            var t = s.Get<Toggle>("t");
            var order = new List<string>();
            UI.Sfx.OnCue.Subscribe(c => order.Add(c.Key));
            t.OnValueChanged.Subscribe(v => order.Add("changed:" + v)).AddTo(s);

            Click(t.GameObject);
            Click(t.GameObject);

            CollectionAssert.AreEqual(
                new[] { "toggle.checked", "changed:True", "toggle.unchecked", "changed:False" }, order);
            Assert.AreSame(t, _cues[0].Source);
        }

        [Test]
        public void Code_XML_and_ReSolve_flips_cue_nothing()
        {
            var s = OpenScreen("<Toggle id='a' isOn='true'>A</Toggle><Toggle id='b' isOn='false' isOn.mobile='true'>B</Toggle>");
            s.Get<Toggle>("a").IsOn = false;
            try { UI.Variants.Set("mobile", true); }
            finally { UI.Variants.Set("mobile", false); }
            Assert.IsEmpty(_cues, Describe(_cues));
        }

        [Test]
        public void In_a_radio_group_only_the_clicked_toggle_cues()
        {
            var s = OpenScreen("<Toggle id='a' group='g' isOn='true'>A</Toggle><Toggle id='b' group='g'>B</Toggle>");

            Click(s.Get<Toggle>("b").GameObject);
            Click(s.Get<Toggle>("b").GameObject);

            Assert.AreEqual("[toggle.checked→toggle.checked]", Describe(_cues),
                "a's switch-off is the group's doing; clicking b again changes nothing");
            Assert.AreSame(s.Get<Toggle>("b"), _cues[0].Source);
        }

        [Test]
        public void A_handler_that_flips_it_back_in_the_same_click_does_not_cue_twice()
        {
            var s = OpenScreen("<Toggle id='t'>T</Toggle>");
            var t = s.Get<Toggle>("t");
            t.OnValueChanged.Subscribe(v => { if (v) t.IsOn = false; }).AddTo(s);

            Click(t.GameObject);

            Assert.AreEqual("[toggle.checked→toggle.checked]", Describe(_cues));
        }

        [Test]
        public void A_disabled_Toggle_cues_deny_and_the_keys_follow_sfx()
        {
            var s = OpenScreen("<Toggle id='off' interactable='false'>X</Toggle>" +
                               "<Toggle id='t' sfx='checked:on_key unchecked:off_key'>T</Toggle>");
            Click(s.Get<Toggle>("off").GameObject);
            Click(s.Get<Toggle>("t").GameObject);
            Click(s.Get<Toggle>("t").GameObject);
            Assert.AreEqual("[toggle.deny→toggle.deny, toggle.checked→on_key, toggle.unchecked→off_key]", Describe(_cues));
        }

        // ───── Tab ─────

        private const string Bar =
            "<TabBar id='bar' {0}><Tab id='a' isOn='true'>A</Tab><Tab id='b' {1}>B</Tab><Tab id='c' interactable='false'>C</Tab></TabBar>";

        private static string TabBar(string barAttrs = "", string bAttrs = "") => string.Format(Bar, barAttrs, bAttrs);

        [Test]
        public void Clicking_another_tab_cues_one_select_before_OnSelectionChanged()
        {
            var s = OpenScreen(TabBar());
            var order = new List<string>();
            UI.Sfx.OnCue.Subscribe(c => order.Add(c.Key));
            s.Get<TabBar>("bar").OnSelectionChanged.Subscribe(t => order.Add("selected:" + t?.Id)).AddTo(s);

            Click(s.Get<Tab>("b").GameObject);

            CollectionAssert.AreEqual(new[] { "tab.select", "selected:b" }, order);
            Assert.AreSame(s.Get<Tab>("b"), _cues.Single().Source);
        }

        [Test]
        public void Clicking_the_selected_tab_cues_nothing()
        {
            var s = OpenScreen(TabBar());
            Click(s.Get<Tab>("a").GameObject);
            Assert.IsEmpty(_cues, Describe(_cues));
        }

        [Test]
        public void With_allowSwitchOff_clicking_the_selected_tab_cues_deselect()
        {
            var s = OpenScreen(TabBar("allowSwitchOff='true'"));
            Click(s.Get<Tab>("a").GameObject);
            Assert.AreEqual("[tab.deselect→tab.deselect]", Describe(_cues));
        }

        [Test]
        public void Code_selection_cues_nothing()
        {
            var s = OpenScreen(TabBar());
            s.Get<Tab>("b").IsOn = true;
            s.Get<TabBar>("bar").ClearSelection();
            Assert.IsEmpty(_cues, Describe(_cues));
        }

        [Test]
        public void A_disabled_tab_cues_deny()
        {
            var s = OpenScreen(TabBar());
            Click(s.Get<Tab>("c").GameObject);
            Assert.AreEqual("[tab.deny→tab.deny]", Describe(_cues));
        }

        [Test]
        public void The_tab_bar_is_looked_up_after_the_tab()
        {
            var s = OpenScreen(TabBar("sfx='select:page_turn'", "sfx='special'"));

            Click(s.Get<Tab>("b").GameObject);   // its own bare key first
            Click(s.Get<Tab>("a").GameObject);   // the bar's select:
            Click(s.Get<Tab>("c").GameObject);   // neither decides deny

            Assert.AreEqual("[tab.select→special, tab.select→page_turn, tab.deny→tab.deny]", Describe(_cues));
        }

        [Test]
        public void A_tab_inside_a_TabMenu_looks_up_the_menu()
        {
            var s = OpenScreen("<TabMenu id='m' sfx='select:menu_pick'><Tab id='a' isOn='true'>A</Tab><Tab id='b'>B</Tab></TabMenu>");
            s.Get<TabMenu>("m").Expand();
            _cues.Clear();

            Click(s.Get<Tab>("b").GameObject);

            Assert.AreEqual("tab.select", _cues.First().DefaultKey);
            Assert.AreEqual("menu_pick", _cues.First().Key, Describe(_cues));
        }
    }
}
