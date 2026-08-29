using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using PuguiScreen = PromptUGUI.Application.Screen;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Structure, caption mirroring and lifecycle for <c>&lt;TabMenu&gt;</c> — the popup-shaped tab
    /// group (spec <c>2026-08-29-tabmenu-design.md</c>). Placement lives in
    /// <see cref="TabMenuPlacementTests"/>, dynamic items in <c>TabMenuBindItemsTests</c>.
    /// </summary>
    public class TabMenuTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        internal const string TwoTabs = @"
  <TabMenu id='m' fontSize='22'>
    <Tab id='a' text='World' bind='pw' isOn='true'/>
    <Tab id='b' text='Guild' bind='pg'/>
  </TabMenu>
  <Frame id='pw'/>
  <Frame id='pg'/>";

        internal static PuguiScreen Open(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        internal static RectTransform Popup(TabMenu m) => (RectTransform)m.RectTransform.Find("Popup");
        internal static RectTransform Content(TabMenu m) => (RectTransform)Popup(m).Find("Content");
        private static TMP_Text Label(TabMenu m) => m.RectTransform.Find("Label").GetComponent<TMP_Text>();
        private static UnityImage Arrow(TabMenu m) => m.RectTransform.Find("Arrow").GetComponent<UnityImage>();
        private static UnityImage IconOf(TabMenu m) => m.RectTransform.Find("Icon").GetComponent<UnityImage>();

        // ── Registration & structure ───────────────────────────────────────────────────────

        [Test]
        public void Registered_as_builtin()
        {
            UI.ResetForTests();
            Assert.IsTrue(UI.Registry.Has("TabMenu"));
        }

        [Test]
        public void Tabs_land_in_popup_content_not_on_self()
        {
            var s = Open(TwoTabs);
            var m = s.Get<TabMenu>("m");
            var tab = s.Get<Tab>("a");

            Assert.AreSame(Content(m), tab.RectTransform.parent, "children host is Popup/Content");
            Assert.AreSame(Popup(m), Content(m).parent);
            Assert.AreSame(m.RectTransform, Popup(m).parent, "popup stays inside the TabMenu subtree");
        }

        [Test]
        public void Self_carries_toggle_group_and_button()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            var group = m.GameObject.GetComponent<ToggleGroup>();
            Assert.IsNotNull(group, "mutual exclusion lives on the TabMenu, like TabBar");
            Assert.IsFalse(group.allowSwitchOff);
            Assert.IsNotNull(m.GameObject.GetComponent<Button>(), "the caption is the click target");
        }

        [Test]
        public void Trigger_area_is_transparent_but_clickable()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            var img = m.GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(0f, img.color.a, "TM-D3: no chrome of its own — wrap it in a Frame for that");
            Assert.IsTrue(img.raycastTarget, "…but still the click target for expanding");
        }

        [Test]
        public void Popup_content_is_a_vertical_layout_that_fills_item_width()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            var vlg = Content(m).GetComponent<VerticalLayoutGroup>();
            Assert.IsNotNull(vlg);
            Assert.IsTrue(vlg.childControlWidth);
            Assert.IsTrue(vlg.childControlHeight);
            Assert.IsTrue(vlg.childForceExpandWidth, "TM-D7: menu rows span the panel");
            Assert.IsFalse(vlg.childForceExpandHeight, "…but keep their own height");
        }

        [Test]
        public void Popup_is_inactive_after_open()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            Assert.IsFalse(Popup(m).gameObject.activeSelf, "collapsed is the only initial state (TM-D11)");
        }

        // ── Caption mirroring (TM-D5) ──────────────────────────────────────────────────────

        [Test]
        public void Caption_mirrors_selected_tab_text()
        {
            var s = Open(TwoTabs);
            var m = s.Get<TabMenu>("m");
            Assert.AreEqual("World", Label(m).text);

            s.Get<Tab>("b").IsOn = true;
            Assert.AreEqual("Guild", Label(m).text);
        }

        [Test]
        public void Caption_follows_a_runtime_text_change_on_the_selected_tab()
        {
            var s = Open(TwoTabs);
            s.Get<Tab>("a").Text = "Renamed";
            Assert.AreEqual("Renamed", Label(s.Get<TabMenu>("m")).text);
        }

        [Test]
        public void Caption_ignores_text_changes_on_unselected_tabs()
        {
            var s = Open(TwoTabs);
            s.Get<Tab>("b").Text = "Elsewhere";
            Assert.AreEqual("World", Label(s.Get<TabMenu>("m")).text);
        }

        [Test]
        public void Caption_icon_hidden_when_selected_tab_has_none()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            Assert.IsFalse(IconOf(m).enabled, "no icon= on the tab means no caption icon slot");
        }

        [Test]
        public void FontSize_applies_to_caption()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            Assert.AreEqual(22f, Label(m).fontSize);
        }

        [Test]
        public void Arrow_uses_the_default_caret()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            Assert.IsTrue(Arrow(m).enabled);
            Assert.IsNotNull(Arrow(m).sprite);
        }

        [Test]
        public void Arrow_empty_hides_the_caret()
        {
            var m = Open(@"<TabMenu id='m' arrow=''><Tab id='a' text='X'/></TabMenu>").Get<TabMenu>("m");
            Assert.IsFalse(Arrow(m).enabled, "arrow='' hides it — a sprite-less Image would draw a block");
        }

        // ── Selection semantics reused from TabBar ─────────────────────────────────────────

        [Test]
        public void Bind_shows_only_the_selected_page()
        {
            var s = Open(TwoTabs);
            Assert.IsTrue(s.Get<Frame>("pw").GameObject.activeSelf);
            Assert.IsFalse(s.Get<Frame>("pg").GameObject.activeSelf);

            s.Get<Tab>("b").IsOn = true;
            Assert.IsFalse(s.Get<Frame>("pw").GameObject.activeSelf);
            Assert.IsTrue(s.Get<Frame>("pg").GameObject.activeSelf);
        }

        // Regression: the rows sit inside a collapsed (inactive) popup, where uGUI's ToggleGroup
        // does nothing — Toggle.Set gates NotifyToggleOn on IsActive(), and a disabled toggle has
        // already unregistered itself. Without TabGroupCore enforcing it, both tabs stay on.
        [Test]
        public void Selection_is_exclusive_even_while_collapsed()
        {
            var s = Open(TwoTabs);
            Assert.IsFalse(Popup(s.Get<TabMenu>("m")).gameObject.activeSelf, "precondition: collapsed");

            s.Get<Tab>("b").IsOn = true;
            Assert.IsFalse(s.Get<Tab>("a").IsOn, "the previously selected tab must switch off");
            Assert.AreEqual(1, s.Get<TabMenu>("m").SelectedIndex);
        }

        [Test]
        public void Auto_selects_first_tab_when_none_declared()
        {
            var s = Open(@"<TabMenu id='m'><Tab id='a' text='A'/><Tab id='b' text='B'/></TabMenu>");
            Assert.AreEqual(0, s.Get<TabMenu>("m").SelectedIndex);
            Assert.AreEqual("A", Label(s.Get<TabMenu>("m")).text);
        }

        [Test]
        public void Count_and_GetAt_see_static_tabs()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            Assert.AreEqual(2, m.Count);
            Assert.AreEqual("Guild", m.GetAt(1).CaptionText);
        }

        [Test]
        public void OnSelectionChanged_fires_on_switch()
        {
            var s = Open(TwoTabs);
            Tab seen = null;
            using var sub = s.Get<TabMenu>("m").OnSelectionChanged.Subscribe(t => seen = t);
            s.Get<Tab>("b").IsOn = true;
            Assert.AreSame(s.Get<Tab>("b"), seen);
        }

        // ── Native size ────────────────────────────────────────────────────────────────────

        [Test]
        public void Native_size_hugs_the_caption()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            var n = m.GetNativeSize();
            Assert.IsTrue(n.HasValue);
            Assert.Greater(n.Value.x, 0f, "width follows the caption text (TM-D6, unlike <Dropdown>)");
            Assert.GreaterOrEqual(n.Value.y, 44f, "min tap target");
        }

        [Test]
        public void Native_width_grows_with_a_longer_caption()
        {
            var s = Open(TwoTabs);
            var m = s.Get<TabMenu>("m");
            var before = m.GetNativeSize().Value.x;
            s.Get<Tab>("a").Text = "A much, much longer channel name";
            Assert.Greater(m.GetNativeSize().Value.x, before);
        }
    }
}
