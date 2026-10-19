using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using InputField = PromptUGUI.Controls.InputField;
using Screen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// <c>&lt;Popover closeOnEditEnd&gt;</c> against a field TMP really edits (spec 2026-10-19-input-begin-edit
    /// §1.2 / §4.2): a failed submit that goes back to the field keeps the panel; the edit ending for good closes it.
    /// </summary>
    public class PopoverCloseOnEditEndPlayTests
    {
        private Screen _s;
        private GameObject _ownEs;
        private readonly List<PopoverCloseReason> _closes = new();

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _closes.Clear();
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
  <Btn id='a' anchor='center' size='60x40'>A</Btn>
  <Popover id='pop' on='click@a' placement='over-end' closeOnEditEnd='true' transition='0.15'>
    <InputField id='f' width='240' height='44' lineType='single'/>
  </Popover>
</Screen></PromptUGUI>");
            _s = UI.Open("S");
            Pop.OnClosed.Subscribe(_closes.Add);
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_ownEs != null) Object.Destroy(_ownEs);
        }

        private Popover Pop => _s.Get<Popover>("pop");
        private InputField Field => _s.Get<InputField>("f");

        private static IEnumerator Frames(int n)
        {
            for (var i = 0; i < n; i++) yield return null;
        }

        private IEnumerator OpenAndEdit()
        {
            Pop.OnOpened.Subscribe(_ => Field.BeginEdit());
            Pop.Open();
            yield return Frames(2);
            Assert.IsTrue(Field.IsEditing, "precondition: BeginEdit from OnOpened");
        }

        private void Submit() =>
            ExecuteEvents.Execute(Field.GameObject, new BaseEventData(EventSystem.current), ExecuteEvents.submitHandler);

        [UnityTest]
        public IEnumerator A_failed_submit_that_goes_back_to_the_field_keeps_it_open()
        {
            yield return OpenAndEdit();
            using var _ = Field.OnSubmit.Subscribe(__ => Field.BeginEdit());

            Submit();
            yield return Frames(4);

            Assert.IsTrue(Pop.IsOpen, "the edit ended with the submit and began again in that frame");
            Assert.IsTrue(Field.IsEditing);
            CollectionAssert.IsEmpty(_closes);
        }

        [UnityTest]
        public IEnumerator A_submit_nobody_answers_closes_it()
        {
            yield return OpenAndEdit();

            Submit();
            yield return Frames(4);

            Assert.IsFalse(Pop.IsOpen);
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.EditEnded }, _closes);
            Assert.AreSame(_s.Get("a").GameObject, EventSystem.current.currentSelectedGameObject,
                "the selection the field held goes back to the trigger");
        }

        [UnityTest]
        public IEnumerator Losing_the_selection_closes_it()
        {
            yield return OpenAndEdit();

            EventSystem.current.SetSelectedGameObject(null);   // a press on the panel's padding, deselectOnBackgroundClick
            yield return Frames(4);

            Assert.IsFalse(Pop.IsOpen);
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.EditEnded }, _closes);
        }
    }
}
