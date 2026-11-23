using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using static PromptUGUI.Tests.EditMode.Controls.SfxTestKit;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A Tab / Toggle announces a value only when it differs from the value right before the operation (spec
    /// 2026-11-23-input-correctness §3): re-clicking the selected tab or the on radio raises nothing — uGUI's Toggle.Set forces
    /// the group's member back on and still calls onValueChanged(true). Clicks go through the real InternalToggle
    /// (<see cref="SfxTestKit.Click"/>). Plus the TabMenu's two guards (§4).
    /// </summary>
    public class RepickTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private sealed class Counts
        {
            public int Trigger, Value, Selected, Selection;
        }

        private static PromptUGUI.Application.Screen OpenBar(string barAttrs = "") =>
            OpenScreen($"<TabBar id='bar' anchor='center' size='300x60' {barAttrs}>" +
                       "<Tab id='a' text='A' isOn='true'><Trigger id='ta' on='checked'/></Tab><Tab id='b' text='B'/></TabBar>");

        private static Counts Watch(PromptUGUI.Application.Screen s)
        {
            var c = new Counts();
            s.Get<Trigger>("ta").OnFire.Subscribe(_ => c.Trigger++);
            s.Get<Tab>("a").OnValueChanged.Subscribe(_ => c.Value++);
            s.Get<Tab>("a").OnSelected.Subscribe(_ => c.Selected++);
            s.Get<TabBar>("bar").OnSelectionChanged.Subscribe(_ => c.Selection++);
            return c;
        }

        [Test]
        public void Re_clicking_the_selected_tab_raises_no_event()
        {
            var s = OpenBar();
            var c = Watch(s);

            Click(s.Get<Tab>("a").GameObject);

            Assert.IsTrue(s.Get<Tab>("a").IsOn);
            Assert.AreEqual(0, c.Value, "Tab.OnValueChanged");
            Assert.AreEqual(0, c.Selected, "Tab.OnSelected");
            Assert.AreEqual(0, c.Selection, "TabBar.OnSelectionChanged");
            Assert.AreEqual(0, c.Trigger, "<Trigger on='checked'>");
        }

        [Test]
        public void Writing_false_to_the_only_selected_tab_raises_no_event()
        {
            var s = OpenBar();
            var c = Watch(s);

            s.Get<Tab>("a").IsOn = false;   // the group puts it straight back on

            Assert.IsTrue(s.Get<Tab>("a").IsOn);
            Assert.AreEqual(0, c.Value + c.Selected + c.Selection + c.Trigger);
        }

        [Test]
        public void Picking_another_tab_still_announces()
        {
            var s = OpenBar();
            var c = Watch(s);
            var bSelected = 0;
            s.Get<Tab>("b").OnSelected.Subscribe(_ => bSelected++);

            Click(s.Get<Tab>("b").GameObject);

            Assert.AreEqual(1, c.Value, "a turned off");
            Assert.AreEqual(1, c.Selection);
            Assert.AreEqual(1, bSelected);
        }

        [Test]
        public void With_allowSwitchOff_clicking_the_selected_tab_off_still_announces()
        {
            var s = OpenBar("allowSwitchOff='true'");
            var c = Watch(s);

            Click(s.Get<Tab>("a").GameObject);

            Assert.IsFalse(s.Get<Tab>("a").IsOn);
            Assert.AreEqual(1, c.Value);
            Assert.AreEqual(1, c.Selection, "the selection became none");
        }

        [Test]
        public void Re_clicking_the_on_radio_raises_no_event()
        {
            var s = OpenScreen("<Toggle id='r1' anchor='top-left' size='140x44' group='g' isOn='true'>R1</Toggle>" +
                               "<Toggle id='r2' anchor='top-left' size='140x44' margin='_,_,_,200' group='g'>R2</Toggle>");
            var changed = 0;
            s.Get<Toggle>("r1").OnValueChanged.Subscribe(_ => changed++);

            Click(s.Get<Toggle>("r1").GameObject);

            Assert.IsTrue(s.Get<Toggle>("r1").IsOn);
            Assert.AreEqual(0, changed);
        }

        // ───── TabMenu ─────

        private const string Menu =
            "<TabMenu id='m' anchor='top-left' transition='0'>" +
            "<Tab id='a' text='A' isOn='true'/><Tab id='b' text='B'/><Tab id='c' text='C' interactable='false'/>" +
            "</TabMenu>";

        [Test]
        public void Re_picking_the_current_row_still_closes_the_menu_with_its_cue()
        {
            var cues = Record();
            var s = OpenScreen(Menu);
            var m = s.Get<TabMenu>("m");
            var selection = 0;
            m.OnSelectionChanged.Subscribe(_ => selection++);
            m.Expand();
            cues.Clear();

            Click(s.Get<Tab>("a").GameObject);

            Assert.IsFalse(m.IsExpanded, "picking a row closes the menu (TM-D10), the current one included");
            Assert.AreEqual(0, selection, "the selection did not change");
            Assert.AreEqual("[tabmenu.collapse→tabmenu.collapse]", Describe(cues));
        }

        [Test]
        public void Clicking_a_disabled_row_keeps_the_menu_open()
        {
            var cues = Record();
            var s = OpenScreen(Menu);
            var m = s.Get<TabMenu>("m");
            m.Expand();
            cues.Clear();

            Click(s.Get<Tab>("c").GameObject);

            Assert.IsTrue(m.IsExpanded);
            Assert.AreEqual("[tab.deny→tab.deny]", Describe(cues), "the refusal, and nothing else");
        }

        [Test]
        public void An_open_the_chain_refuses_raises_no_OnExpanded()
        {
            var s = OpenScreen(Menu + "<TabMenu id='n' anchor='top-left' margin='_,_,_,300' transition='0'><Tab id='x' text='X' isOn='true'/></TabMenu>");
            var m = s.Get<TabMenu>("m");
            var n = s.Get<TabMenu>("n");
            n.Expand();
            // Opening m closes n first; n's close handler closes m again before m is up — the chain refuses m's open.
            n.OnCollapsed.Subscribe(_ => m.Collapse());
            var expanded = 0;
            m.OnExpanded.Subscribe(_ => expanded++);

            m.Expand();

            Assume.That(m.IsExpanded, Is.False, "precondition: the open was refused");
            Assert.AreEqual(0, expanded);
        }
    }
}
