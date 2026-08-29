using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    /// <summary>
    /// Directional focus around an open <c>&lt;TabMenu&gt;</c>, and who gets to consume Escape when
    /// a menu and a modal are both listening (spec §7.8 / §7.9).
    /// </summary>
    public class TabMenuTrapTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // EventSystem.current is null in EditMode; mirror the ModalTrapTests pattern.
        private static EventSystem GetEventSystem() =>
            EventSystem.current ?? Object.FindAnyObjectByType<EventSystem>();

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'>{innerXml}</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private const string PageWithMenu = @"
          <Btn id='elsewhere'>O</Btn>
          <TabMenu id='m' transition='0'>
            <Tab id='a' text='World' isOn='true'/>
            <Tab id='b' text='Guild'/>
          </TabMenu>";

        private static GameObject Popup(TabMenu m) => m.RectTransform.Find("Popup").gameObject;

        // ── Focus containment ─────────────────────────────────────────────────────────────

        [Test]
        public void Expanding_cages_the_focus_inside_the_popup()
        {
            UI.UseGamepadNavigation();
            var s = Open(PageWithMenu);
            var m = s.Get<TabMenu>("m");

            m.Expand();

            Assert.AreSame(Popup(m), UI.Navigation.ContainmentRoot,
                           "an open menu owns the focus while it is up");
        }

        [Test]
        public void Expanding_puts_the_focus_on_the_selected_row()
        {
            UI.UseGamepadNavigation();
            var s = Open(PageWithMenu);
            var m = s.Get<TabMenu>("m");

            m.Expand();

            Assert.AreSame(s.Get<Tab>("a").GameObject, GetEventSystem().currentSelectedGameObject,
                           "opening a menu lands on what is currently chosen, not on row 0 blindly");
        }

        [Test]
        public void Focus_cannot_escape_to_the_page_behind()
        {
            UI.UseGamepadNavigation();
            var s = Open(PageWithMenu);
            var m = s.Get<TabMenu>("m");
            m.Expand();

            GetEventSystem().SetSelectedGameObject(s.Get<Btn>("elsewhere").GameObject);
            UI.Navigation.EnforceContainmentForTests();

            Assert.IsTrue(GetEventSystem().currentSelectedGameObject.transform.IsChildOf(Popup(m).transform));
        }

        [Test]
        public void Collapsing_releases_the_cage_and_returns_focus_to_the_handle()
        {
            UI.UseGamepadNavigation();
            var s = Open(PageWithMenu);
            var m = s.Get<TabMenu>("m");
            m.Expand();

            m.Collapse();

            Assert.IsNull(UI.Navigation.ContainmentRoot, "the page was not caged before, so it is not now");
            Assert.AreSame(m.GameObject, GetEventSystem().currentSelectedGameObject,
                           "the user is put back where they opened the menu from");
        }

        [Test]
        public void Collapsing_inside_a_modal_restores_the_modal_cage()
        {
            UI.UseGamepadNavigation();
            var s = Open(PageWithMenu);
            var m = s.Get<TabMenu>("m");

            // Stand in for an enclosing modal: something already owned the focus.
            var modalRoot = s.RootGameObject;
            UI.Navigation.ContainmentRoot = modalRoot;

            m.Expand();
            Assert.AreSame(Popup(m), UI.Navigation.ContainmentRoot);

            m.Collapse();
            Assert.AreSame(modalRoot, UI.Navigation.ContainmentRoot,
                           "a menu restores whatever owned the focus before it, not null");
        }

        [Test]
        public void Nothing_happens_to_focus_when_navigation_is_off()
        {
            var s = Open(PageWithMenu);          // no UseGamepadNavigation
            var m = s.Get<TabMenu>("m");

            m.Expand();
            Assert.IsNull(UI.Navigation.ContainmentRoot);

            m.Collapse();
            Assert.IsNull(UI.Navigation.ContainmentRoot);
        }

        // ── Escape ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Escape_is_not_consumed_when_no_menu_is_open()
        {
            Open(PageWithMenu);
            Assert.IsFalse(TabMenu.TryConsumeEscape(), "a modal behind it must still close");
        }

        [Test]
        public void Escape_closes_an_open_menu_and_is_consumed()
        {
            var m = Open(PageWithMenu).Get<TabMenu>("m");
            m.Expand();

            Assert.IsTrue(TabMenu.TryConsumeEscape(), "…so the modal underneath survives this press");
            Assert.IsFalse(m.IsExpanded);
        }

        // Both listeners answer the same key press and the order between them is not defined. If the
        // menu's own listener wins the race, the modal's handler must still see the press as spent,
        // or one Escape would close the menu AND the modal behind it.
        [Test]
        public void Escape_is_still_consumed_when_the_menu_closed_itself_first()
        {
            var m = Open(PageWithMenu).Get<TabMenu>("m");
            m.Expand();

            m.NotifyEscapeConsumedForTests();      // the menu's own listener got there first
            Assert.IsFalse(m.IsExpanded);

            Assert.IsTrue(TabMenu.TryConsumeEscape(),
                          "the modal's handler, running later in the same frame, must not act on it");
        }

        [Test]
        public void A_stale_escape_from_an_earlier_frame_is_not_consumed()
        {
            var m = Open(PageWithMenu).Get<TabMenu>("m");
            m.Expand();
            m.NotifyEscapeConsumedForTests();
            Assert.IsTrue(TabMenu.TryConsumeEscape());

            TabMenu.ForgetEscapeFrameForTests();   // stand in for "a later frame"
            Assert.IsFalse(TabMenu.TryConsumeEscape(), "only the frame it happened in is swallowed");
        }
    }
}
