using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using InputField = PromptUGUI.Controls.InputField;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// <c>InputField.BeginEdit()</c> against a field TMP really edits (spec 2026-10-19-input-begin-edit §4.1):
    /// it enters edit mode where <c>screen.Focus</c> cannot — the field already selected after a submit, a
    /// directional selection the two-level gate would undo — and it may be called from the submit handler.
    /// </summary>
    public class InputFieldBeginEditPlayTests
    {
        private Screen _s;
        private GameObject _ownEs;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            if (EventSystem.current == null)
            {
#if ENABLE_INPUT_SYSTEM
                _ownEs = new GameObject("EventSystem", typeof(EventSystem),
                                        typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
                _ownEs = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
            }
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <InputField id='f' anchor='center' width='240' height='44' lineType='single'/>
  <Btn id='b' anchor='top-left' size='80x40'>B</Btn>
</Screen></PromptUGUI>");
            _s = UI.Open("S");
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_ownEs != null) Object.Destroy(_ownEs);
        }

        private InputField Field => _s.Get<InputField>("f");
        private static EventSystem Es => EventSystem.current;

        private IEnumerator Frames(int n = 2)
        {
            for (var i = 0; i < n; i++) yield return null;
        }

        private IEnumerator EditThenSubmit()
        {
            Es.SetSelectedGameObject(Field.GameObject);
            yield return Frames();
            Assert.IsTrue(Field.IsEditing, "precondition: editing");
            ExecuteEvents.Execute(Field.GameObject, new BaseEventData(Es), ExecuteEvents.submitHandler);
            yield return Frames();
            Assert.IsFalse(Field.IsEditing, "precondition: the submit ended the edit");
            Assert.AreSame(Field.GameObject, Es.currentSelectedGameObject, "precondition: still selected");
        }

        [UnityTest]
        public IEnumerator After_a_submit_screen_Focus_cannot_reenter_the_edit()
        {
            yield return EditThenSubmit();

            _s.Focus("f");
            yield return Frames();

            Assert.IsFalse(Field.IsEditing, "the field is already selected: selecting it again does nothing");
        }

        [UnityTest]
        public IEnumerator After_a_submit_BeginEdit_reenters_the_edit()
        {
            yield return EditThenSubmit();

            Assert.IsTrue(Field.BeginEdit());
            yield return Frames();

            Assert.IsTrue(Field.IsEditing);
        }

        [UnityTest]
        public IEnumerator BeginEdit_from_the_submit_handler_keeps_editing()
        {
            Es.SetSelectedGameObject(Field.GameObject);
            yield return Frames();
            using var _ = Field.OnSubmit.Subscribe(__ => Field.BeginEdit());

            ExecuteEvents.Execute(Field.GameObject, new BaseEventData(Es), ExecuteEvents.submitHandler);
            yield return Frames();

            Assert.IsTrue(Field.IsEditing, "the edit ended with the submit and began again");
        }

        [UnityTest]
        public IEnumerator Directional_navigation_selected_field_enters_edit()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            Es.SetSelectedGameObject(Field.GameObject);
            yield return Frames();
            Assert.IsFalse(Field.IsEditing, "precondition: the two-level gate only selected it");

            Field.BeginEdit();
            yield return Frames();

            Assert.IsTrue(Field.IsEditing);
        }

        [UnityTest]
        public IEnumerator Directional_navigation_unselected_field_enters_edit()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            Es.SetSelectedGameObject(_s.Get("b").GameObject);
            yield return Frames();

            Field.BeginEdit();
            yield return Frames();

            Assert.AreSame(Field.GameObject, Es.currentSelectedGameObject, "TMP's activation selects it");
            Assert.IsTrue(Field.IsEditing, "and the gate does not undo it");
        }

        [UnityTest]
        public IEnumerator The_request_is_consumed_by_the_next_LateUpdate()
        {
            Field.BeginEdit();
            var tmp = Field.GameObject.GetComponent<PromptUGUI.Controls.Internal.PuiInputField>();
            Assert.IsTrue(tmp.EditRequestPending);

            yield return Frames();

            Assert.IsFalse(tmp.EditRequestPending);
            Assert.IsTrue(Field.IsEditing);
        }
    }
}
