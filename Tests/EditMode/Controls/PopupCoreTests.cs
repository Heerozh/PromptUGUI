using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using PuguiScreen = PromptUGUI.Application.Screen;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The popup machinery pulled out of <c>&lt;TabMenu&gt;</c> (spec 2026-10-18 §4.1 / §4.3): a
    /// panel lifted above its host's root canvas, the optional catcher, the Escape listener, the
    /// focus cage and what is put back on close. Driven with a bare <c>&lt;Frame&gt;</c> host so the
    /// core is pinned on its own; TabMenu's suites guard the same behaviour through the control.
    /// </summary>
    public class PopupCoreTests
    {
        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            PopupChain.ResetForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PopupChain.ResetForTests();
            UI.ResetForTests();
        }

        private PuguiScreen _screen;
        private Frame _host;
        private RectTransform _panel;
        private Button _row;

        private PopupCore Make(PopupDismiss dismiss = PopupDismiss.Blocker, bool cage = true, bool cancelButton = true)
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='elsewhere'>O</Btn>
  <Frame id='h' size='120x44'/>
</Screen></PromptUGUI>");
            _screen = UI.Open("S");
            _host = _screen.Get<Frame>("h");
            _panel = ProceduralBuilders.AddChild(_host.RectTransform, "Panel");
            _panel.gameObject.AddComponent<UnityImage>();

            // A focusable row, so the cage has something to land on.
            var row = ProceduralBuilders.AddChild(_panel, "Row");
            row.gameObject.AddComponent<UnityImage>();
            _row = row.gameObject.AddComponent<Button>();

            return new PopupCore(_host, _panel, dismiss, cage, cancelButton)
            {
                Measure = () => new PopupLayout(new Vector2(200f, 100f), 4f),
                TransitionSeconds = 0f,
            };
        }

        private GameObject Blocker()
        {
            var t = _screen.RootGameObject.transform.Find(PopupCore.BlockerName);
            return t != null ? t.gameObject : null;
        }

        private static EventSystem Es() => EventSystem.current ?? Object.FindAnyObjectByType<EventSystem>();

        private int RootOrder => _screen.RootGameObject.GetComponent<Canvas>().sortingOrder;

        // ── Open / close ──────────────────────────────────────────────────────────────────

        [Test]
        public void Open_lifts_the_panel_above_the_host_root_canvas()
        {
            var core = Make();

            core.Open();

            Assert.IsTrue(core.IsOpen);
            Assert.IsTrue(_panel.gameObject.activeSelf);
            var canvas = _panel.GetComponent<Canvas>();
            Assert.IsTrue(canvas.overrideSorting);
            Assert.AreEqual(RootOrder + PopupChain.SortingOffset, canvas.sortingOrder);
            Assert.IsNotNull(_panel.GetComponent<GraphicRaycaster>());
            Assert.AreSame(core, PopupChain.Top);
        }

        [Test]
        public void Open_sizes_the_panel_from_Measure()
        {
            var core = Make();
            core.Open();
            Assert.AreEqual(new Vector2(200f, 100f), _panel.sizeDelta);
        }

        [Test]
        public void Blocker_mode_puts_a_catcher_under_the_root_canvas()
        {
            var core = Make();

            core.Open();

            var blocker = Blocker();
            Assert.IsNotNull(blocker, "the catcher lives on the root canvas, not under the host");
            Assert.IsTrue(blocker.activeSelf);
            Assert.AreEqual(RootOrder + PopupChain.SortingOffset - 1, blocker.GetComponent<Canvas>().sortingOrder);
            Assert.AreEqual(0f, blocker.GetComponent<UnityImage>().color.a);
            Assert.IsTrue(blocker.GetComponent<UnityImage>().raycastTarget);
            Assert.AreEqual(UnityEngine.UI.Navigation.Mode.None, blocker.GetComponent<Button>().navigation.mode);
        }

        [Test]
        public void Clicking_the_catcher_closes_with_OutsidePress()
        {
            var core = Make();
            var reasons = new List<PopupCloseReason>();
            core.Closed += reasons.Add;
            core.Open();

            Blocker().GetComponent<Button>().onClick.Invoke();

            Assert.IsFalse(core.IsOpen);
            CollectionAssert.AreEqual(new[] { PopupCloseReason.OutsidePress }, reasons);
            Assert.IsFalse(Blocker().activeSelf);
        }

        [Test]
        public void Owner_mode_has_no_catcher()
        {
            var core = Make(PopupDismiss.Owner);
            core.Open();
            Assert.IsNull(Blocker());
        }

        [Test]
        public void Close_in_EditMode_deactivates_the_panel_and_drops_overrideSorting()
        {
            var core = Make();
            core.Open();

            core.Close();

            Assert.IsFalse(core.IsOpen);
            Assert.IsFalse(_panel.gameObject.activeSelf);
            Assert.IsFalse(_panel.GetComponent<Canvas>().overrideSorting);
            Assert.IsTrue(PopupChain.IsEmpty);
        }

        [Test]
        public void Opened_and_Closed_fire_with_the_reason()
        {
            var core = Make();
            var opened = 0;
            var reasons = new List<PopupCloseReason>();
            core.Opened += () => opened++;
            core.Closed += reasons.Add;

            core.Open();
            core.Open();                                   // already open: nothing
            core.Close(PopupCloseReason.Escape);
            core.Close(PopupCloseReason.Escape);           // already closed: nothing

            Assert.AreEqual(1, opened);
            CollectionAssert.AreEqual(new[] { PopupCloseReason.Escape }, reasons);
        }

        [Test]
        public void Opening_another_popup_closes_this_one()
        {
            var first = Make();
            var otherPanel = ProceduralBuilders.AddChild(_screen.Get<Btn>("elsewhere").RectTransform, "Panel2");
            var second = new PopupCore(_screen.Get<Btn>("elsewhere"), otherPanel, PopupDismiss.Owner, false, false)
            {
                Measure = () => new PopupLayout(new Vector2(50f, 50f), 0f),
            };
            var reasons = new List<PopupCloseReason>();
            first.Closed += reasons.Add;
            first.Open();

            second.Open();

            Assert.IsFalse(first.IsOpen);
            CollectionAssert.AreEqual(new[] { PopupCloseReason.Replaced }, reasons);
            Assert.AreSame(second, PopupChain.Top);
        }

        // ── Escape listener ───────────────────────────────────────────────────────────────

        [Test]
        public void Escape_listener_is_enabled_only_while_open()
        {
            var core = Make();
            core.Open();
            var listener = _panel.GetComponent<ModalEscapeListener>();
            Assert.IsNotNull(listener);
            Assert.IsTrue(listener.enabled);

            core.Close();
            Assert.IsFalse(listener.enabled);
        }

        [Test]
        public void Cancel_button_binding_follows_the_constructor_flag()
        {
            var with = Make(cancelButton: true);
            with.Open();
            Assert.IsTrue(_panel.GetComponent<ModalEscapeListener>().AlsoCancelButton);
            with.Close();
            UI.ResetForTests();

            var without = Make(cancelButton: false);
            without.Open();
            Assert.IsFalse(_panel.GetComponent<ModalEscapeListener>().AlsoCancelButton);
        }

        [Test]
        public void Its_escape_listener_closes_it_with_Escape()
        {
            var core = Make();
            var reasons = new List<PopupCloseReason>();
            core.Closed += reasons.Add;
            core.Open();

            _panel.GetComponent<ModalEscapeListener>().FireForTests();

            Assert.IsFalse(core.IsOpen);
            CollectionAssert.AreEqual(new[] { PopupCloseReason.Escape }, reasons);
        }

        // ── Focus cage (spec §4.3) ────────────────────────────────────────────────────────

        [Test]
        public void Cage_sets_ContainmentRoot_and_lands_on_FocusPick()
        {
            UI.UseGamepadNavigation();
            var core = Make();
            core.FocusPick = () => _row.gameObject;

            core.Open();

            Assert.AreSame(_panel.gameObject, UI.Navigation.ContainmentRoot);
            Assert.AreSame(_panel.gameObject, _screen.NavConfineRoot);
            Assert.AreSame(_row.gameObject, Es().currentSelectedGameObject);
        }

        [Test]
        public void Without_a_cage_focus_is_left_alone()
        {
            UI.UseGamepadNavigation();
            var core = Make(cage: false);
            Es().SetSelectedGameObject(_screen.Get<Btn>("elsewhere").GameObject);

            core.Open();

            Assert.IsNull(UI.Navigation.ContainmentRoot);
            Assert.AreSame(_screen.Get<Btn>("elsewhere").GameObject, Es().currentSelectedGameObject);
        }

        [Test]
        public void Close_restores_ContainmentRoot_only_if_it_is_still_the_panel()
        {
            UI.UseGamepadNavigation();
            var core = Make();
            var modal = new GameObject("ModalStandIn");
            try
            {
                UI.Navigation.ContainmentRoot = modal;
                core.Open();
                core.Close();
                Assert.AreSame(modal, UI.Navigation.ContainmentRoot, "put back what it replaced");

                core.Open();
                var newer = new GameObject("NewerLayer");
                UI.Navigation.ContainmentRoot = newer;      // a layer that appeared meanwhile caged the focus
                core.Close(PopupCloseReason.Covered);
                Assert.AreSame(newer, UI.Navigation.ContainmentRoot, "not ours any more: left alone");
                Object.DestroyImmediate(newer);
            }
            finally { Object.DestroyImmediate(modal); }
        }

        [Test]
        public void Close_restores_the_screens_NavConfineRoot_and_the_global_ContainmentRoot_separately()
        {
            UI.UseGamepadNavigation();
            var core = Make();
            var modal = new GameObject("ModalStandIn");
            try
            {
                UI.Navigation.ContainmentRoot = modal;     // the global cage …
                Assert.IsNull(_screen.NavConfineRoot);      // … while this Screen itself was not confined

                core.Open();
                core.Close();

                Assert.AreSame(modal, UI.Navigation.ContainmentRoot);
                Assert.IsNull(_screen.NavConfineRoot, "the Screen gets its own previous cage back, not the global one");
            }
            finally { Object.DestroyImmediate(modal); }
        }

        [Test]
        public void Close_moves_selection_to_the_host_only_if_it_was_inside_the_panel()
        {
            UI.UseGamepadNavigation();
            var core = Make();
            core.FocusPick = () => _row.gameObject;

            core.Open();
            core.Close();
            Assert.AreSame(_host.GameObject, Es().currentSelectedGameObject, "inside the panel: back to the host");

            core.Open();
            var elsewhere = _screen.Get<Btn>("elsewhere").GameObject;
            Es().SetSelectedGameObject(elsewhere);          // a new layer already took the focus
            core.Close(PopupCloseReason.Covered);
            Assert.AreSame(elsewhere, Es().currentSelectedGameObject, "outside the panel: not stolen back");
        }

        [Test]
        public void HostGone_clears_a_selection_inside_the_panel()
        {
            UI.UseGamepadNavigation();
            var core = Make();
            core.FocusPick = () => _row.gameObject;
            core.Open();

            core.Close(PopupCloseReason.HostGone);

            Assert.IsNull(Es().currentSelectedGameObject);
        }

        // ── Measuring and the host's apply pass ───────────────────────────────────────────

        [Test]
        public void BeginRebuild_activates_a_closed_panel_for_the_scope_only()
        {
            var core = Make();
            _panel.gameObject.SetActive(false);

            using (core.BeginRebuild())
                Assert.IsTrue(_panel.gameObject.activeSelf, "rows built now must measure");

            Assert.IsFalse(_panel.gameObject.activeSelf, "…and the popup is left closed");
        }

        [Test]
        public void OnHostAfterApply_closes_when_the_host_is_disabled_and_repositions_otherwise()
        {
            var core = Make();
            var reasons = new List<PopupCloseReason>();
            core.Closed += reasons.Add;
            core.Open();

            _panel.sizeDelta = Vector2.zero;
            core.OnHostAfterApply();
            Assert.IsTrue(core.IsOpen);
            Assert.AreEqual(new Vector2(200f, 100f), _panel.sizeDelta, "re-measured and re-placed");

            _host.Interactable = false;
            core.OnHostAfterApply();
            Assert.IsFalse(core.IsOpen);
            CollectionAssert.AreEqual(new[] { PopupCloseReason.Owner }, reasons, "a disabled host closes it like the host would");
        }

        [Test]
        public void OnHostAfterApply_hides_a_closed_panel()
        {
            var core = Make();
            Assert.IsTrue(_panel.gameObject.activeSelf, "precondition: built active, as a host does");

            core.OnHostAfterApply();

            Assert.IsFalse(_panel.gameObject.activeSelf);
        }

        [Test]
        public void Dispose_destroys_the_catcher_and_leaves_the_chain()
        {
            var core = Make();
            core.Open();
            Assert.IsNotNull(Blocker());

            core.Dispose();

            Assert.IsNull(Blocker());
            Assert.IsTrue(PopupChain.IsEmpty);
            Assert.IsFalse(core.IsOpen);
        }
    }
}
