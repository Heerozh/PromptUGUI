using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif
using InputField = PromptUGUI.Controls.InputField;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// A field with a suggestion list in a running player loop (spec 2026-10-18 §6.2 / §6.6): TMP really
    /// focuses, edit edges come from <c>isFocused</c>, and gamepad events reach the field through the
    /// EventSystem. Real device keys are the fixture below.
    /// </summary>
    public class InputFieldSuggestionsPlayTests
    {
        internal const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>
  <Screen name='S'>
    <InputField id='f' anchor='center' width='240' height='44'>
      <Suggestions id='s' itemTemplate='Row' transition='0'/>
    </InputField>
    <Btn id='other' anchor='top-left' size='80x40'>X</Btn>
  </Screen>
</PromptUGUI>";

        private Screen _screen;
        private InputField _field;
        private Suggestions _list;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.UseGamepadNavigation();
            UI.Navigation.NotePointerInput();   // pointer mode: selecting the field enters edit at once
            UI.LoadDocument("t", Xml);
            _screen = UI.Open("S");
            _field = _screen.Get<InputField>("f");
            _list = _screen.Get<Suggestions>("s");
        }

        [TearDown] public void TearDown() => UI.ResetForTests();

        private static EventSystem Es => EventSystem.current;
        private PuiInputField Pui => _field.GameObject.GetComponent<PuiInputField>();

        private IEnumerator Edit()
        {
            Es.SetSelectedGameObject(_field.GameObject);
            yield return null;
            yield return null;
            Assert.IsTrue(_field.IsEditing, "precondition: editing");
        }

        private void Push(params string[] items) =>
            _list.BindItems(Observable.Return<IReadOnlyList<string>>(items), (IControl row, string v) => { });

        private void Move(MoveDirection dir) =>
            ExecuteEvents.Execute(_field.GameObject, new AxisEventData(Es) { moveDir = dir }, ExecuteEvents.moveHandler);

        [UnityTest]
        public IEnumerator Selecting_the_field_enters_edit_and_a_push_opens_the_list()
        {
            yield return Edit();
            _list.Open();
            Push("apple", "apricot");
            yield return null;

            Assert.IsTrue(_list.IsOpen);
            Assert.IsTrue(_list.GameObject.activeInHierarchy);
        }

        [UnityTest]
        public IEnumerator Selecting_something_else_ends_the_edit_and_closes_the_list()
        {
            yield return Edit();
            _list.Open();
            Push("apple");
            Assert.IsTrue(_list.IsOpen, "precondition");

            Es.SetSelectedGameObject(_screen.Get<Btn>("other").GameObject);
            yield return null;
            yield return null;

            Assert.IsFalse(_field.IsEditing);
            Assert.IsFalse(_list.IsOpen);
        }

        [UnityTest]
        public IEnumerator Gamepad_move_and_submit_choose_a_suggestion()
        {
            yield return Edit();
            _list.Open();
            Push("apple", "apricot", "avocado");
            var submits = new List<SuggestionSubmit>();
            _list.OnSubmit.Subscribe(submits.Add);

            Move(MoveDirection.Down);
            Move(MoveDirection.Down);
            Assert.AreEqual(1, _list.HighlightedIndex);
            ExecuteEvents.Execute(_field.GameObject, new BaseEventData(Es), ExecuteEvents.submitHandler);
            yield return null;
            yield return null;

            Assert.AreEqual(1, submits.Count);
            Assert.AreEqual(1, submits[0].ChosenIndex);
            Assert.IsFalse(_list.IsOpen);
            Assert.IsFalse(_field.IsEditing);
        }

        [UnityTest]
        public IEnumerator East_closes_the_list_without_rolling_the_text_back()
        {
            _field.TextValue = "ap";
            yield return Edit();
            _field.TextValue = "apple";
            _list.Open();
            Push("apple");

            Assert.IsTrue(EscapeChain.Dispatch(EscapeKey.CancelButton, Time.frameCount));
            ExecuteEvents.Execute(_field.GameObject, new BaseEventData(Es), ExecuteEvents.cancelHandler);
            yield return null;
            yield return null;

            Assert.IsFalse(_list.IsOpen);
            Assert.IsTrue(_field.IsEditing, "only the list closed");
            Assert.AreEqual("apple", _field.TextValue);
        }

        // ── the key handler, on a field TMP really focused ─────────────────────────────────

        [UnityTest]
        public IEnumerator A_character_key_goes_into_the_text()
        {
            yield return Edit();
            _list.Open();
            Push("apple");
            var changes = new List<string>();
            _field.OnValueChanged.Subscribe(changes.Add);

            Pui.HandleKeyDown(Event.KeyboardEvent("a"));

            Assert.AreEqual("a", _field.TextValue);
            CollectionAssert.AreEqual(new[] { "a" }, changes);
            Assert.IsTrue(_list.IsOpen);
        }

        [UnityTest]
        public IEnumerator Down_moves_the_highlight_and_not_the_caret()
        {
            _field.TextValue = "ap";
            yield return Edit();
            _list.Open();
            Push("apple", "apricot");
            var caret = Pui.caretPosition;

            Pui.HandleKeyDown(Event.KeyboardEvent("down"));

            Assert.AreEqual(0, _list.HighlightedIndex);
            Assert.AreEqual(caret, Pui.caretPosition);
        }

        [UnityTest]
        public IEnumerator Return_with_a_highlight_chooses_it()
        {
            yield return Edit();
            _list.Open();
            Push("apple", "apricot");
            var submits = new List<SuggestionSubmit>();
            var fieldSubmits = new List<string>();
            _list.OnSubmit.Subscribe(submits.Add);
            _field.OnSubmit.Subscribe(fieldSubmits.Add);
            _list.HighlightedIndex = 1;

            Assert.IsTrue(Pui.HandleKeyDown(Event.KeyboardEvent("return")), "the edit ends");
            yield return null;

            Assert.AreEqual(1, submits.Count);
            Assert.AreEqual(1, submits[0].ChosenIndex);
            CollectionAssert.IsEmpty(fieldSubmits);
            Assert.IsFalse(_field.IsEditing);
        }

        [UnityTest]
        public IEnumerator Return_without_a_highlight_submits_the_text_and_ends_the_edit()
        {
            _field.TextValue = "ap";
            yield return Edit();
            _list.Open();
            Push("apple");
            var log = new List<string>();
            _field.OnSubmit.Subscribe(t => log.Add("field:" + t));
            _list.OnSubmit.Subscribe(s => log.Add($"list:{s.Text}:{s.ChosenIndex}"));

            Assert.IsTrue(Pui.HandleKeyDown(Event.KeyboardEvent("return")));
            yield return null;

            CollectionAssert.AreEqual(new[] { "field:ap", "list:ap:-1" }, log, "both, the field's first");
            Assert.IsFalse(_list.IsOpen);
            Assert.IsFalse(_field.IsEditing);
        }

        // ── OnInput (spec §6.7) ────────────────────────────────────────────────────────────

        [UnityTest]
        public IEnumerator Typing_raises_OnInput()
        {
            yield return Edit();
            var inputs = new List<string>();
            _field.OnInput.Subscribe(inputs.Add);

            Pui.ProcessEvent(Event.KeyboardEvent("a"));
            Pui.ProcessEvent(Event.KeyboardEvent("p"));

            CollectionAssert.AreEqual(new[] { "a", "ap" }, inputs);
        }

        [UnityTest]
        public IEnumerator Writing_TextValue_while_editing_does_not()
        {
            yield return Edit();
            var inputs = new List<string>();
            var changes = new List<string>();
            _field.OnInput.Subscribe(inputs.Add);
            _field.OnValueChanged.Subscribe(changes.Add);

            _field.TextValue = "apple";

            CollectionAssert.AreEqual(new[] { "apple" }, changes);
            CollectionAssert.IsEmpty(inputs, "a host writing the chosen suggestion back must not query again");
        }

        [UnityTest]
        public IEnumerator The_escape_rollback_does_not()
        {
            _field.TextValue = "ap";
            yield return Edit();
            var inputs = new List<string>();
            var changes = new List<string>();
            _field.OnInput.Subscribe(inputs.Add);
            _field.OnValueChanged.Subscribe(changes.Add);
            Pui.ProcessEvent(Event.KeyboardEvent("x"));

            Assert.AreEqual(1, inputs.Count, "precondition: the key was typed");
            var typed = _field.TextValue;

            ExecuteEvents.Execute(_field.GameObject, new BaseEventData(Es), ExecuteEvents.cancelHandler);
            yield return null;

            Assert.AreNotEqual(typed, _field.TextValue, "precondition: TMP rolled the edit back");
            Assert.AreEqual(2, changes.Count, "the rollback is a value change…");
            Assert.AreEqual(1, inputs.Count, "…but not user input");
        }

        [UnityTest]
        public IEnumerator A_variant_replaying_text_does_not()
        {
            UI.ResetForTests();
            UI.UseGamepadNavigation();
            UI.Navigation.NotePointerInput();
            UI.LoadDocument("t", Xml.Replace("<InputField id='f'", "<InputField id='f' text='' text.mobile='hi'"));
            _screen = UI.Open("S");
            _field = _screen.Get<InputField>("f");
            _list = _screen.Get<Suggestions>("s");
            yield return Edit();
            var inputs = new List<string>();
            _field.OnInput.Subscribe(inputs.Add);

            UI.Variants.Set("mobile", true);

            Assert.AreEqual("hi", _field.TextValue);
            CollectionAssert.IsEmpty(inputs);
        }

        [UnityTest]
        public IEnumerator Typing_arms_the_list()
        {
            yield return Edit();
            _field.OnInput.Subscribe(_ => Push("apple", "apricot"));

            Pui.ProcessEvent(Event.KeyboardEvent("a"));

            Assert.IsTrue(_list.IsOpen);
        }

        // ── the other submit entries (spec §6.4) ───────────────────────────────────────────

        [UnityTest]
        public IEnumerator The_soft_keyboard_done_key_is_a_typed_submit()
        {
            _field.TextValue = "ap";
            yield return Edit();
            var log = new List<string>();
            _field.OnSubmit.Subscribe(t => log.Add("field:" + t));
            _list.OnSubmit.Subscribe(s => log.Add($"list:{s.Text}:{s.ChosenIndex}"));

            Pui.onSubmit.Invoke(Pui.text);   // what TMP does when the soft keyboard reports Done

            CollectionAssert.AreEqual(new[] { "field:ap", "list:ap:-1" }, log);
        }

        [UnityTest]
        public IEnumerator Entering_edit_with_Submit_in_two_level_navigation_is_not_a_submit()
        {
            UI.ResetForTests();
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();   // before Open, as InputFieldNavPlayTests does
            UI.LoadDocument("t", Xml);
            _screen = UI.Open("S");
            _field = _screen.Get<InputField>("f");
            _list = _screen.Get<Suggestions>("s");
            Es.SetSelectedGameObject(_field.GameObject);
            yield return null;
            yield return null;
            Assert.IsFalse(_field.IsEditing, "precondition: selected, not editing");
            var fired = 0;
            _field.OnSubmit.Subscribe(_ => fired++);
            _list.OnSubmit.Subscribe(_ => fired++);

            ExecuteEvents.Execute(_field.GameObject, new BaseEventData(Es), ExecuteEvents.submitHandler);
            yield return null;
            yield return null;

            Assert.IsTrue(_field.IsEditing);
            Assert.AreEqual(0, fired);
        }
    }

#if ENABLE_INPUT_SYSTEM
    /// <summary>
    /// Real device input through <c>InputSystemUIInputModule</c>. Device keys never reach the IMGUI
    /// queue, so these exercise the gamepad path and the Escape chain; TMP's own key loop is the
    /// fixture above.
    /// </summary>
    public class InputFieldSuggestionsDevicePlayTests : InputTestFixture
    {
        private Screen _screen;
        private InputField _field;
        private Suggestions _list;

        public override void Setup()
        {
            base.Setup();
            UI.ResetForTests();
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
            new GameObject("EventSystem", typeof(EventSystem),
                           typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            UI.LoadDocument("t", InputFieldSuggestionsPlayTests.Xml);
            _screen = UI.Open("S");
            _field = _screen.Get<InputField>("f");
            _list = _screen.Get<Suggestions>("s");
        }

        public override void TearDown()
        {
            UI.ResetForTests();
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
            base.TearDown();
        }

        private IEnumerator EditWithList(params string[] items)
        {
            EventSystem.current.SetSelectedGameObject(_field.GameObject);
            yield return null;
            yield return null;
            Assert.IsTrue(_field.IsEditing, "precondition: editing");
            _list.Open();
            _list.BindItems(Observable.Return<IReadOnlyList<string>>(items), (IControl row, string v) => { });
            Assert.IsTrue(_list.IsOpen, "precondition: open");
        }

        // P17: a takeover that Use()d update-selected every frame would starve the module's move events.
        [UnityTest]
        public IEnumerator Dpad_down_moves_the_highlight_while_editing()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();
            yield return EditWithList("apple", "apricot");

            Press(gamepad.dpad.down);
            yield return null;
            Release(gamepad.dpad.down);
            yield return null;

            Assert.AreEqual(0, _list.HighlightedIndex);
            Assert.IsTrue(_field.IsEditing);
        }

        [UnityTest]
        public IEnumerator Escape_closes_the_list_and_the_field_keeps_editing()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            _field.TextValue = "ap";
            yield return EditWithList("apple");
            _field.TextValue = "apple";

            Press(keyboard.escapeKey);
            yield return null;
            Release(keyboard.escapeKey);
            yield return null;

            Assert.IsFalse(_list.IsOpen);
            Assert.IsTrue(_field.IsEditing, "the press closed the list, not the edit");
            Assert.AreEqual("apple", _field.TextValue, "no rollback");

            Press(keyboard.escapeKey);
            yield return null;
            Release(keyboard.escapeKey);
            yield return null;

            Assert.IsFalse(_field.IsEditing, "the next press is TMP's again");
        }
    }
#endif
}
