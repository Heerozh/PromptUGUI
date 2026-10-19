#if ENABLE_INPUT_SYSTEM
using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using InputField = PromptUGUI.Controls.InputField;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// Real presses on a page with no dialog and no popup (spec 2026-10-19-input-begin-edit §1.3 / §4.4): the
    /// Escape or B that cancels a field's edit is reported spent in the frame game code reads its own back key;
    /// Start is not the field's.
    /// </summary>
    public class InputFieldEscapePlayTests : InputTestFixture
    {
        private Screen _s;

        public override void Setup()
        {
            base.Setup();
            UI.ResetForTests();
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
            new GameObject("EventSystem", typeof(EventSystem),
                           typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' anchor='center' width='240' height='44'/>
</Screen></PromptUGUI>");
            _s = UI.Open("S");
        }

        public override void TearDown()
        {
            UI.ResetForTests();
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.DestroyImmediate(es.gameObject);
            base.TearDown();
        }

        private InputField Field => _s.Get<InputField>("f");

        private IEnumerator Edit()
        {
            Field.TextValue = "abc";
            EventSystem.current.SetSelectedGameObject(Field.GameObject);
            yield return null;
            yield return null;
            Assert.IsTrue(Field.IsEditing, "precondition: editing");
        }

        [UnityTest]
        public IEnumerator Escape_that_cancels_an_edit_is_reported_consumed()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return Edit();

            Press(keyboard.escapeKey);
            yield return null;   // the frame whose input update takes the press: the game's Update sees it now

            Assert.IsTrue(UI.EscapeConsumedThisFrame);
            Release(keyboard.escapeKey);
            yield return null;
            Assert.IsFalse(Field.IsEditing, "TMP still cancels the edit");
            Assert.IsFalse(UI.EscapeConsumedThisFrame, "only that frame");
        }

        [UnityTest]
        public IEnumerator B_that_cancels_an_edit_is_reported_consumed()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();
            yield return Edit();

            Press(gamepad.buttonEast);
            yield return null;

            Assert.IsTrue(UI.EscapeConsumedThisFrame);
            Release(gamepad.buttonEast);
            yield return null;
            Assert.IsFalse(Field.IsEditing);
        }

        [UnityTest]
        public IEnumerator Start_is_not_the_fields()
        {
            var gamepad = InputSystem.AddDevice<Gamepad>();
            yield return Edit();

            Press(gamepad.startButton);
            yield return null;

            Assert.IsFalse(UI.EscapeConsumedThisFrame);
            Release(gamepad.startButton);
            yield return null;
            Assert.IsTrue(Field.IsEditing);
        }

        [UnityTest]
        public IEnumerator Escape_on_a_field_that_is_only_selected_is_not_consumed()
        {
            var keyboard = InputSystem.AddDevice<Keyboard>();
            yield return Edit();
            Press(keyboard.escapeKey);
            yield return null;
            Release(keyboard.escapeKey);
            yield return null;
            Assume.That(!Field.IsEditing && EventSystem.current.currentSelectedGameObject == Field.GameObject);

            Press(keyboard.escapeKey);
            yield return null;

            Assert.IsFalse(UI.EscapeConsumedThisFrame, "the second press is the game's");
            Release(keyboard.escapeKey);
            yield return null;
        }
    }
}
#endif
