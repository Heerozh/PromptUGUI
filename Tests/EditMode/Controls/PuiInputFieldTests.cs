using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using InputField = PromptUGUI.Controls.InputField;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The field's TMP component while a suggestion list is open (spec 2026-10-18 §6.2 / §6.6): the
    /// gamepad's move / submit / cancel and the key handler drive the list; with the list closed every
    /// one of them is TMP's as before. The edit state comes from the test hook — EditMode never
    /// focuses a TMP field.
    /// </summary>
    public class PuiInputFieldTests
    {
        private const string Row = "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        private Screen _screen;
        private InputField _field;
        private Suggestions _list;
        private readonly List<SuggestionSubmit> _submits = new();
        private readonly List<string> _fieldSubmits = new();
        private GameObject _ownEventSystem;

        // uGUI's own navigation (the paths that fall through to TMP) selects through the event's EventSystem.
        private static EventSystem Es => EventSystem.current ?? Object.FindAnyObjectByType<EventSystem>();

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            if (Es == null) _ownEventSystem = new GameObject("EventSystem", typeof(EventSystem));
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + Row
                                 + "<Screen name='S'><InputField id='f' width='240' height='44'>"
                                 + "<Suggestions id='s' itemTemplate='Row' transition='0'/></InputField>"
                                 + "<InputField id='plain' width='240' height='44'/>"
                                 + "</Screen></PromptUGUI>");
            _screen = UI.Open("S");
            _field = _screen.Get<InputField>("f");
            _list = _screen.Get<Suggestions>("s");
            _field.TextValue = "ap";
            _submits.Clear();
            _fieldSubmits.Clear();
            _list.OnSubmit.Subscribe(_submits.Add);
            _field.OnSubmit.Subscribe(_fieldSubmits.Add);
        }

        [TearDown]
        public void TearDown()
        {
            EscapeChain.ForgetPopupConsumedFrameForTests();
            UI.ResetForTests();
            if (_ownEventSystem != null) Object.DestroyImmediate(_ownEventSystem);
            _ownEventSystem = null;
        }

        private void OpenList(int rows = 3)
        {
            _field.SetEditingForTests(true);
            _list.NotifyUserInput();
            var items = new string[rows];
            for (var i = 0; i < rows; i++) items[i] = "r" + i;
            _list.BindItems(Observable.Return<IReadOnlyList<string>>(items), (IControl row, string v) => { });
            Assert.IsTrue(_list.IsOpen, "precondition");
        }

        private static PuiInputField Pui(InputField f) => f.GameObject.GetComponent<PuiInputField>();

        private void Move(MoveDirection dir) =>
            ExecuteEvents.Execute(_field.GameObject, new AxisEventData(Es) { moveDir = dir },
                                  ExecuteEvents.moveHandler);

        private void Submit() =>
            ExecuteEvents.Execute(_field.GameObject, new BaseEventData(Es), ExecuteEvents.submitHandler);

        private void Cancel() =>
            ExecuteEvents.Execute(_field.GameObject, new BaseEventData(Es), ExecuteEvents.cancelHandler);

        [Test]
        public void The_field_runs_on_PuiInputField()
        {
            Assert.IsNotNull(Pui(_field));
            Assert.IsNotNull(Pui(_screen.Get<InputField>("plain")), "with or without a list");
        }

        // ── gamepad ────────────────────────────────────────────────────────────────────────

        [Test]
        public void Move_up_and_down_drive_the_highlight_while_the_list_is_open()
        {
            OpenList();
            Move(MoveDirection.Down);
            Assert.AreEqual(0, _list.HighlightedIndex);
            Move(MoveDirection.Down);
            Assert.AreEqual(1, _list.HighlightedIndex);
            Move(MoveDirection.Up);
            Assert.AreEqual(0, _list.HighlightedIndex);
        }

        [Test]
        public void Submit_with_a_highlight_chooses_it()
        {
            OpenList();
            _list.HighlightedIndex = 1;

            Submit();

            Assert.AreEqual(1, _submits.Count);
            Assert.AreEqual(1, _submits[0].ChosenIndex);
            CollectionAssert.IsEmpty(_fieldSubmits);
            Assert.IsFalse(_list.IsOpen);
        }

        [Test]
        public void Cancel_in_the_frame_the_chain_spent_the_escape_is_swallowed()
        {
            OpenList();
            Assert.IsTrue(EscapeChain.Dispatch(EscapeKey.CancelButton, EscapeChain.NextTestPress()));
            Assert.IsFalse(_list.IsOpen, "the chain closed the list");

            Cancel();

            Assert.IsFalse(Pui(_field).wasCanceled, "TMP never saw it: no rollback, still editing");
            Assert.IsTrue(_field.IsEditing);
        }

        [Test]
        public void With_the_list_closed_submit_and_cancel_are_TMPs()
        {
            _field.SetEditingForTests(true);

            Submit();
            Assert.AreEqual(1, _fieldSubmits.Count, "TMP's submit");

            Cancel();
            Assert.IsTrue(Pui(_field).wasCanceled, "TMP's cancel");
        }

        [Test]
        public void A_move_right_after_an_arrow_key_is_ignored()
        {
            // Holding ↓ produces both IMGUI key repeats and the input module's Navigate repeats; only
            // the first may step the highlight, or it skips rows.
            OpenList();
            Pui(_field).HandleKeyDown(Event.KeyboardEvent("down"));
            Assert.AreEqual(0, _list.HighlightedIndex);

            Move(MoveDirection.Down);

            Assert.AreEqual(0, _list.HighlightedIndex);
        }

        // ── the key handler ────────────────────────────────────────────────────────────────

        [Test]
        public void Arrow_keys_move_the_highlight_and_enter_chooses_it()
        {
            OpenList();
            var pui = Pui(_field);
            pui.HandleKeyDown(Event.KeyboardEvent("down"));
            pui.HandleKeyDown(Event.KeyboardEvent("down"));
            Assert.AreEqual(1, _list.HighlightedIndex);

            pui.HandleKeyDown(Event.KeyboardEvent("return"));

            Assert.AreEqual(1, _submits.Count);
            Assert.AreEqual(1, _submits[0].ChosenIndex);
            Assert.IsFalse(_field.IsEditing);
        }

        // ── lifecycle ──────────────────────────────────────────────────────────────────────

        [Test]
        public void Hiding_the_field_from_code_closes_the_list()
        {
            OpenList();
            _field.Hidden = true;
            Assert.IsFalse(_list.IsOpen);
        }

        [Test]
        public void A_field_without_a_list_handles_gamepad_events_as_before()
        {
            var plain = _screen.Get<InputField>("plain");
            Assert.DoesNotThrow(() =>
            {
                ExecuteEvents.Execute(plain.GameObject, new AxisEventData(Es) { moveDir = MoveDirection.Down },
                                      ExecuteEvents.moveHandler);
                ExecuteEvents.Execute(plain.GameObject, new BaseEventData(Es), ExecuteEvents.cancelHandler);
                plain.Hidden = true;
            });
        }
    }
}
