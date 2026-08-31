using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
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
        private static TMP_Text LabelOf(TabMenu m) => m.RectTransform.Find("Label").GetComponent<TMP_Text>();
        private static UnityImage Arrow(TabMenu m) => m.RectTransform.Find("Arrow").GetComponent<UnityImage>();
        private static UnityImage IconOf(TabMenu m) => m.RectTransform.Find("Icon").GetComponent<UnityImage>();

        private static System.Collections.Generic.List<LintIssue> Walk(string body)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

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
            Assert.AreEqual("World", LabelOf(m).text);

            s.Get<Tab>("b").IsOn = true;
            Assert.AreEqual("Guild", LabelOf(m).text);
        }

        [Test]
        public void Caption_follows_a_runtime_text_change_on_the_selected_tab()
        {
            var s = Open(TwoTabs);
            s.Get<Tab>("a").Text = "Renamed";
            Assert.AreEqual("Renamed", LabelOf(s.Get<TabMenu>("m")).text);
        }

        [Test]
        public void Caption_ignores_text_changes_on_unselected_tabs()
        {
            var s = Open(TwoTabs);
            s.Get<Tab>("b").Text = "Elsewhere";
            Assert.AreEqual("World", LabelOf(s.Get<TabMenu>("m")).text);
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
            Assert.AreEqual(22f, LabelOf(m).fontSize);
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
            Assert.AreEqual("A", LabelOf(s.Get<TabMenu>("m")).text);
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

        // ── Expand / collapse ─────────────────────────────────────────────────────────────

        private static GameObject Blocker(PuguiScreen s)
        {
            var t = s.RootGameObject.transform.Find(TabMenu.BlockerName);
            return t != null ? t.gameObject : null;
        }

        [Test]
        public void Expand_activates_the_popup_and_lifts_it_above_the_page()
        {
            var s = Open(TwoTabs);
            var m = s.Get<TabMenu>("m");
            var rootOrder = s.RootGameObject.GetComponent<Canvas>().sortingOrder;

            m.Expand();

            Assert.IsTrue(m.IsExpanded);
            Assert.IsTrue(Popup(m).gameObject.activeSelf);
            var canvas = Popup(m).GetComponent<Canvas>();
            Assert.IsTrue(canvas.overrideSorting, "also what frees the panel from an ancestor mask");
            Assert.AreEqual(rootOrder + TabMenu.PopupSortingOffset, canvas.sortingOrder);
        }

        [Test]
        public void Expand_puts_a_click_catcher_under_the_root_canvas()
        {
            var s = Open(TwoTabs);
            var m = s.Get<TabMenu>("m");
            var rootOrder = s.RootGameObject.GetComponent<Canvas>().sortingOrder;

            m.Expand();

            var blocker = Blocker(s);
            Assert.IsNotNull(blocker, "the catcher lives on the root canvas, not inside the menu");
            Assert.IsTrue(blocker.activeSelf);
            Assert.AreEqual(rootOrder + TabMenu.PopupSortingOffset - 1,
                            blocker.GetComponent<Canvas>().sortingOrder, "just below the panel");
            Assert.AreEqual(0f, blocker.GetComponent<UnityImage>().color.a, "invisible…");
            Assert.IsTrue(blocker.GetComponent<UnityImage>().raycastTarget, "…but catches the click");
            Assert.AreEqual(UnityEngine.UI.Navigation.Mode.None,
                            blocker.GetComponent<Button>().navigation.mode,
                            "never a directional-navigation neighbour");
        }

        [Test]
        public void Clicking_the_catcher_collapses()
        {
            var s = Open(TwoTabs);
            var m = s.Get<TabMenu>("m");
            m.Expand();

            Blocker(s).GetComponent<Button>().onClick.Invoke();

            Assert.IsFalse(m.IsExpanded);
            Assert.IsFalse(Blocker(s).activeSelf);
        }

        [Test]
        public void Collapse_hides_the_popup()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            m.Expand();
            m.Collapse();
            Assert.IsFalse(m.IsExpanded);
            Assert.IsFalse(Popup(m).gameObject.activeSelf);
        }

        [Test]
        public void Toggle_flips_the_state()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            m.Toggle();
            Assert.IsTrue(m.IsExpanded);
            m.Toggle();
            Assert.IsFalse(m.IsExpanded);
        }

        [Test]
        public void Clicking_the_handle_toggles()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            m.GameObject.GetComponent<Button>().onClick.Invoke();
            Assert.IsTrue(m.IsExpanded);
        }

        [Test]
        public void Picking_a_tab_collapses()
        {
            var s = Open(TwoTabs);
            var m = s.Get<TabMenu>("m");
            m.Expand();

            s.Get<Tab>("b").IsOn = true;

            Assert.IsFalse(m.IsExpanded, "choosing is the whole point — the menu closes behind it");
        }

        [Test]
        public void Re_picking_the_selected_tab_also_collapses()
        {
            var s = Open(TwoTabs);
            var m = s.Get<TabMenu>("m");
            m.Expand();

            s.Get<Tab>("a").SimulateClickForTests();   // already isOn: no onValueChanged at all

            Assert.IsFalse(m.IsExpanded);
        }

        [Test]
        public void Only_one_menu_is_open_at_a_time()
        {
            var s = Open(@"
              <TabMenu id='m1'><Tab id='a' text='A'/></TabMenu>
              <TabMenu id='m2'><Tab id='b' text='B'/></TabMenu>");
            var m1 = s.Get<TabMenu>("m1");
            var m2 = s.Get<TabMenu>("m2");

            m1.Expand();
            m2.Expand();

            Assert.IsFalse(m1.IsExpanded, "the blocker covers the screen — a second open menu is unreachable");
            Assert.IsTrue(m2.IsExpanded);
        }

        [Test]
        public void OnExpanded_and_OnCollapsed_fire()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            int expanded = 0, collapsed = 0;
            using var e = m.OnExpanded.Subscribe(_ => expanded++);
            using var c = m.OnCollapsed.Subscribe(_ => collapsed++);

            m.Expand();
            m.Expand();       // already open: no second event
            m.Collapse();
            m.Collapse();     // already closed: no second event

            Assert.AreEqual(1, expanded);
            Assert.AreEqual(1, collapsed);
        }

        [Test]
        public void A_disabled_menu_does_not_open()
        {
            var m = Open(@"<TabMenu id='m' interactable='false'><Tab id='a' text='A'/></TabMenu>")
                .Get<TabMenu>("m");
            m.Expand();
            Assert.IsFalse(m.IsExpanded);
        }

        [Test]
        public void Becoming_disabled_closes_an_open_menu()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            m.Expand();
            m.Interactable = false;
            Assert.IsFalse(m.IsExpanded);
        }

        [Test]
        public void Closing_the_screen_takes_the_catcher_with_it()
        {
            var s = Open(TwoTabs);
            s.Get<TabMenu>("m").Expand();
            Assert.IsNotNull(Blocker(s));

            s.Close();
            Assert.IsFalse(TabMenu.HasExpandedMenu, "no dangling global reference to a dead menu");
        }

        // ── Transition (TM-D13) ───────────────────────────────────────────────────────────
        //
        // EditMode has no player loop, so LitMotion never ticks here — the control writes the end
        // state directly outside play mode. What these pin down is that state; the interpolation
        // itself is covered in TabMenuPlayTests.

        [Test]
        public void Transition_zero_lands_on_the_end_state_immediately()
        {
            var m = Open(@"<TabMenu id='m' transition='0'><Tab id='a' text='A'/></TabMenu>").Get<TabMenu>("m");
            m.Expand();

            Assert.AreEqual(1f, Popup(m).GetComponent<CanvasGroup>().alpha);
            Assert.AreEqual(180f, ArrowFlip(m), 0.01f, "the caret points up while open");
        }

        [Test]
        public void Collapse_restores_the_caret()
        {
            var m = Open(@"<TabMenu id='m' transition='0'><Tab id='a' text='A'/></TabMenu>").Get<TabMenu>("m");
            m.Expand();
            m.Collapse();
            Assert.AreEqual(0f, ArrowFlip(m), 0.01f);
        }

        // Regression: the caret must never turn about its TRANSFORM. Its pivot is its LEFT edge
        // (that is what places it by its left side after the label), so a transform turn swung the
        // whole glyph to the left of where it was placed — the caret jumped sideways on every open.
        // The mesh-level turn is about the rect's centre and leaves the transform alone.
        [Test]
        public void Flipping_the_caret_leaves_it_exactly_where_it_was()
        {
            var m = Open(@"<TabMenu id='m' transition='0'><Tab id='a' text='World'/></TabMenu>")
                .Get<TabMenu>("m");
            var arrow = (RectTransform)m.RectTransform.Find("Arrow");
            var before = arrow.anchoredPosition;
            var leftEdgeBefore = LeftEdgeOf(arrow);

            m.Expand();

            Assert.AreEqual(before, arrow.anchoredPosition);
            Assert.AreEqual(leftEdgeBefore, LeftEdgeOf(arrow), 0.01f,
                            "a vertical flip mirrors about the middle — it must not move in x");
            Assert.AreEqual(0f, arrow.localEulerAngles.z, 0.01f, "…and it is a flip, not a turn");
        }

        // World-space left edge, so a pivot-relative move would show up even if anchoredPosition did not.
        private static float LeftEdgeOf(RectTransform rt)
        {
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            return Mathf.Min(corners[0].x, corners[2].x);
        }

        [Test]
        public void An_unparseable_transition_falls_back_to_the_default()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("transition"));
            var m = Open(@"<TabMenu id='m' transition='soon'><Tab id='a' text='A'/></TabMenu>")
                .Get<TabMenu>("m");
            Assert.AreEqual(TabMenu.DefaultTransition, m.TransitionSeconds, 0.0001f);
        }

        [Test]
        public void Transition_accepts_ms_and_bare_seconds()
        {
            Assert.AreEqual(0.25f, Open(@"<TabMenu id='m' transition='250ms'><Tab id='a' text='A'/></TabMenu>")
                .Get<TabMenu>("m").TransitionSeconds, 0.0001f);
            UI.ResetForTests();
            Assert.AreEqual(0.4f, Open(@"<TabMenu id='m' transition='0.4'><Tab id='a' text='A'/></TabMenu>")
                .Get<TabMenu>("m").TransitionSeconds, 0.0001f);
        }

        // Degrees of the caret's mesh-level turn: 180 = pointing up (menu open), 0 = at rest.
        private static float ArrowFlip(TabMenu m)
            => m.RectTransform.Find("Arrow").GetComponent<PromptUGUI.Controls.Internal.RotateFlipEffect>().Rotation;

        // ── Popup skin & procedural surface (TM-D3) ───────────────────────────────────────

        [Test]
        public void Color_fills_the_popup_panel_not_the_handle()
        {
            var m = Open(@"<TabMenu id='m' color='#FF0000'><Tab id='a' text='X'/></TabMenu>").Get<TabMenu>("m");
            var panelBg = Popup(m).GetComponent<UnityImage>();
            Assert.AreEqual(1f, panelBg.color.r);
            Assert.AreEqual(0f, panelBg.color.g);
            Assert.AreEqual(0f, m.GameObject.GetComponent<UnityImage>().color.a,
                            "the handle stays transparent — color= describes the menu");
        }

        [Test]
        public void Popup_has_a_default_rounded_skin()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            Assert.AreEqual("pugui_9slice_round", Popup(m).GetComponent<UnityImage>().sprite.name);
        }

        [Test]
        public void Sprite_empty_clears_the_popup_skin()
        {
            var m = Open(@"<TabMenu id='m' sprite=''><Tab id='a' text='X'/></TabMenu>").Get<TabMenu>("m");
            var bg = Popup(m).GetComponent<UnityImage>();
            Assert.IsNull(bg.sprite);
            Assert.AreEqual(UnityImage.Type.Simple, bg.type);
        }

        [Test]
        public void Radius_draws_a_procedural_surface_under_the_popup()
        {
            var m = Open(@"<TabMenu id='m' radius='12'><Tab id='a' text='X'/></TabMenu>").Get<TabMenu>("m");
            Assert.IsNotNull(Popup(m).Find(ProceduralSurface.NodeName), "the panel is the surface host");
            Assert.IsNull(m.RectTransform.Find(ProceduralSurface.NodeName), "…and the handle draws nothing");
        }

        [Test]
        public void No_procedural_attributes_means_no_surface_node()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            Assert.IsNull(Popup(m).Find(ProceduralSurface.NodeName));
        }

        [Test]
        public void Padding_and_spacing_land_on_the_popup_layout()
        {
            var m = Open(@"<TabMenu id='m' padding='8,12' spacing='4'><Tab id='a' text='X'/></TabMenu>")
                .Get<TabMenu>("m");
            var vlg = Content(m).GetComponent<VerticalLayoutGroup>();
            Assert.AreEqual(4f, vlg.spacing);
            Assert.AreEqual(8, vlg.padding.top);
            Assert.AreEqual(8, vlg.padding.bottom);
            Assert.AreEqual(12, vlg.padding.left);
            Assert.AreEqual(12, vlg.padding.right);
        }

        [Test]
        public void TextColor_paints_the_caption_not_the_panel()
        {
            var m = Open(@"<TabMenu id='m' textColor='#00FF00' color='#FF0000'><Tab id='a' text='X'/></TabMenu>")
                .Get<TabMenu>("m");
            Assert.AreEqual(1f, LabelOf(m).color.g);
            Assert.AreEqual(0f, LabelOf(m).color.r);
        }

        [Test]
        public void Lint_accepts_procedural_attributes_on_TabMenu()
        {
            var issues = Walk("<TabMenu id='m' radius='12' glass='true'><Tab id='a' text='X'/></TabMenu>");
            Assert.IsFalse(issues.Any(i => i.Code == PureContainerVisualAttrRules.VisualAttrCode),
                           "TabMenu is a drawing tag: its surface is the popup panel");
        }

        [Test]
        public void Lint_accepts_nav_attributes_on_TabMenu()
        {
            var issues = Walk(@"<TabMenu id='m' focus='true' navUp='other'><Tab id='a' text='X'/></TabMenu>
                                <Btn id='other'>x</Btn>");
            Assert.IsFalse(issues.Any(i => i.Code == NavTargetRules.NonSelectableCode),
                           "the collapsed handle is a Button — it belongs in the navigation graph");
        }

        [Test]
        public void Native_size_hugs_the_caption()
        {
            var m = Open(TwoTabs).Get<TabMenu>("m");
            var n = m.GetNativeSize();
            Assert.IsTrue(n.HasValue);
            Assert.Greater(n.Value.x, 0f, "width follows the caption text (TM-D6, unlike <Dropdown>)");
            Assert.GreaterOrEqual(n.Value.y, 44f, "min tap target");
        }

        // Regression: ApplyCommon measures a control BEFORE OnAfterApply fills the caption from the
        // selected tab, so measuring the (still empty) label handed the layout a handle just wide
        // enough for the caret — 30px, whatever the channel was called.
        [Test]
        public void Handle_is_laid_out_wide_enough_for_its_caption_on_the_first_pass()
        {
            var s = Open(@"<HStack anchor='top-stretch' height='64'>
                             <TabMenu id='m' fontSize='22'>
                               <Tab id='a' text='A reasonably long channel' isOn='true'/>
                             </TabMenu>
                             <Frame width='stretch'/>
                           </HStack>");
            Canvas.ForceUpdateCanvases();
            var m = s.Get<TabMenu>("m");

            Assert.AreEqual(m.GetNativeSize().Value.x, m.RectTransform.rect.width, 1f,
                "the laid-out handle matches what it asks for — no measuring of an empty caption");
        }

        [Test]
        public void Native_size_sees_the_selected_tab_before_the_caption_is_filled()
        {
            // Same thing one level down: the measurement peeks at the tabs, and picks the isOn one
            // rather than blindly the first.
            var s = Open(@"<TabMenu id='m'>
                             <Tab id='a' text='x'/>
                             <Tab id='b' text='A much, much longer name' isOn='true'/>
                           </TabMenu>");
            var wide = s.Get<TabMenu>("m").GetNativeSize().Value.x;

            UI.ResetForTests();
            var s2 = Open(@"<TabMenu id='m'>
                             <Tab id='a' text='x' isOn='true'/>
                             <Tab id='b' text='A much, much longer name'/>
                           </TabMenu>");
            var narrow = s2.Get<TabMenu>("m").GetNativeSize().Value.x;

            Assert.Greater(wide, narrow, "the selected tab is what the handle has to fit");
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
