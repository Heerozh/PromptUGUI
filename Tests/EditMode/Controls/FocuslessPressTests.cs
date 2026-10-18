using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The press behind a suggestion row and the submit icon (spec 2026-10-18 §5.4): pointer
    /// enter / exit / down / up / click and a state source for <c>&lt;Show on="state-*"&gt;</c>,
    /// without being a <see cref="Selectable"/> — pressing it must not take the selection away from
    /// the input field that is being edited.
    /// </summary>
    public class FocuslessPressTests
    {
        private GameObject _go;
        private FocuslessPress _press;
        private InteractState _last;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _go = new GameObject("Press", typeof(RectTransform));
            _press = _go.AddComponent<FocuslessPress>();
            _press.OnState.Subscribe(s => _last = s);
        }

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            UI.ResetForTests();
        }

        private static PointerEventData Pointer(PointerEventData.InputButton button = PointerEventData.InputButton.Left) =>
            new(EventSystem.current) { button = button, eligibleForClick = true };

        private void Enter() => ExecuteEvents.Execute(_go, Pointer(), ExecuteEvents.pointerEnterHandler);
        private void Exit() => ExecuteEvents.Execute(_go, Pointer(), ExecuteEvents.pointerExitHandler);
        private void Down() => ExecuteEvents.Execute(_go, Pointer(), ExecuteEvents.pointerDownHandler);
        private void Up() => ExecuteEvents.Execute(_go, Pointer(), ExecuteEvents.pointerUpHandler);

        private void Click(PointerEventData.InputButton button = PointerEventData.InputButton.Left) =>
            ExecuteEvents.Execute(_go, Pointer(button), ExecuteEvents.pointerClickHandler);

        [Test]
        public void Is_not_a_Selectable()
        {
            Assert.IsNull(_go.GetComponent<Selectable>());
            Assert.IsNull(ExecuteEvents.GetEventHandler<ISelectHandler>(_go),
                "nothing on it answers select, so a press here never moves the selection");
        }

        [Test]
        public void Pointer_enter_and_exit_drive_hover()
        {
            Assert.AreEqual(InteractState.Normal, _last);
            Enter();
            Assert.AreEqual(InteractState.Hover, _last);
            Exit();
            Assert.AreEqual(InteractState.Normal, _last);
        }

        [Test]
        public void Press_and_release_drive_pressed()
        {
            Enter();
            Down();
            Assert.AreEqual(InteractState.Pressed, _last);
            Up();
            Assert.AreEqual(InteractState.Hover, _last);
        }

        [Test]
        public void SetOn_reads_selected_and_hover_overrides_it_while_inside()
        {
            _press.SetOn(true);
            Assert.AreEqual(InteractState.Selected, _last);

            Enter();
            Assert.AreEqual(InteractState.Hover, _last, "hover is transient and wins, as on a Toggle");
            Exit();
            Assert.AreEqual(InteractState.Selected, _last);

            _press.SetOn(false);
            Assert.AreEqual(InteractState.Normal, _last);
        }

        [Test]
        public void Click_fires_for_a_left_click_only()
        {
            var clicks = 0;
            _press.OnClicked.Subscribe(_ => clicks++);

            Click(PointerEventData.InputButton.Right);
            Assert.AreEqual(0, clicks);
            Click();
            Assert.AreEqual(1, clicks);
        }

        [Test]
        public void Not_interactable_reads_disabled_and_never_clicks()
        {
            var clicks = 0;
            _press.OnClicked.Subscribe(_ => clicks++);

            _press.Interactable = false;
            Assert.AreEqual(InteractState.Disabled, _last);
            Enter();
            Down();
            Assert.AreEqual(InteractState.Disabled, _last);
            Assert.IsFalse(_press.IsPressed);
            Click();
            Assert.AreEqual(0, clicks);

            _press.Interactable = true;
            Assert.AreEqual(InteractState.Hover, _last, "still inside");
        }

        [Test]
        public void IsPressed_spans_down_to_up()
        {
            Assert.IsFalse(_press.IsPressed);
            Down();
            Assert.IsTrue(_press.IsPressed);
            Exit();
            Assert.IsTrue(_press.IsPressed, "dragging off keeps the press until it is released");
            Up();
            Assert.IsFalse(_press.IsPressed);
        }

        [Test]
        public void A_Show_instantiated_under_it_resolves_to_it()
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <Frame>
      <Show id='sel' on='state-selected'><Frame id='hl'/></Show>
    </Frame>
  </Template>
  <Screen name='S'/>
</PromptUGUI>");
            var screen = UI.Open("S");
            _go.transform.SetParent(screen.RootGameObject.transform, false);
            var row = UI.GetInstantiator().InstantiateNode(
                UI.GetScreenDef("S").Templates["Row"].Body, (RectTransform)_go.transform, screen);
            var sel = row.Get<Show>("sel");
            Assert.IsFalse(sel.GameObject.activeSelf);

            _press.SetOn(true);

            Assert.IsTrue(sel.GameObject.activeSelf, "the row shell is the template's state source");
        }
    }
}
