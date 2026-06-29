using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Navigation
{
    public class InputFieldNavPlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string body)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        [UnityTest]
        public IEnumerator DirectionalSelect_DoesNotEnterEditMode()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();              // Mode = Directional
            var s = Open("<InputField id='f'/><Btn id='b'>B</Btn>");
            var field = s.Get<InputField>("f");
            var es = EventSystem.current;

            es.SetSelectedGameObject(field.GameObject);        // 模拟导航选中
            yield return null;                                  // 让 TMP 处理 activate-then-suppress
            yield return null;

            Assert.IsFalse(field.IsEditing,
                "directional-select must NOT activate edit mode (two-level nav)");
        }

        [UnityTest]
        public IEnumerator Submit_OnFocusedNotEditingField_EntersEditMode()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            var s = Open("<InputField id='f'/>");
            var field = s.Get<InputField>("f");
            var es = EventSystem.current;
            es.SetSelectedGameObject(field.GameObject);
            yield return null; yield return null;
            Assert.IsFalse(field.IsEditing, "precondition: selected-not-editing");

            ExecuteEvents.Execute(field.GameObject, new BaseEventData(es), ExecuteEvents.submitHandler);
            yield return null; yield return null;

            // Validates the gate does NOT interfere with TMP's native submit-to-activate path (not the gate's positive contribution).
            Assert.IsTrue(field.IsEditing, "Submit must enter edit mode");
        }

        [UnityTest]
        public IEnumerator Submit_ToEnterEdit_DoesNotFireSubmitEvent()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            var s = Open("<InputField id='f'/>");
            var field = s.Get<InputField>("f");
            bool fired = false;
            field.OnSubmit.Subscribe(_ => fired = true).AddTo(s);
            var es = EventSystem.current;
            es.SetSelectedGameObject(field.GameObject);
            yield return null; yield return null;

            ExecuteEvents.Execute(field.GameObject, new BaseEventData(es), ExecuteEvents.submitHandler);
            yield return null;

            Assert.IsFalse(fired, "entering edit via Submit must not fire the OnSubmit business event");
        }

        [UnityTest]
        public IEnumerator Submit_WhileEditingField_StillFiresSubmitEvent()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            var s = Open("<InputField id='f'/>");
            var field = s.Get<InputField>("f");
            // Enter edit mode first (nav-Submit on the focused-not-editing field).
            ExecuteEvents.Execute(field.GameObject, new BaseEventData(EventSystem.current),
                ExecuteEvents.submitHandler);
            yield return null; yield return null;
            Assert.IsTrue(field.IsEditing, "precondition: must be editing");

            bool fired = false;
            field.OnSubmit.Subscribe(_ => fired = true).AddTo(s);
            // Confirm-submit while editing — the business event must fire.
            ExecuteEvents.Execute(field.GameObject, new BaseEventData(EventSystem.current),
                ExecuteEvents.submitHandler);
            yield return null;

            Assert.IsTrue(fired, "confirm-Submit on an editing field must fire the business event");
        }

        [UnityTest]
        public IEnumerator PointerSelect_StillEntersEditImmediately()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NotePointerInput();                  // Mode = Pointer
            var s = Open("<InputField id='f'/>");
            var field = s.Get<InputField>("f");
            var es = EventSystem.current;
            // 指针点击路径：直接走 TMP 的 OnPointerClick → 默认激活，gate 不抑制
            ExecuteEvents.Execute(field.GameObject, new PointerEventData(es), ExecuteEvents.pointerClickHandler);
            yield return null; yield return null;
            Assert.IsTrue(field.IsEditing, "pointer click must enter edit immediately (mouse UX unchanged)");
        }

        [UnityTest]
        public IEnumerator NavDisabled_DefaultBehaviorUnchanged()
        {
            // 不调 UseGamepadNavigation：gate 挂着但 IsEnabled=false → OnSelect 不抑制
            var s = Open("<InputField id='f'/>");
            var field = s.Get<InputField>("f");
            // SetSelectedGameObject 同步触发 OnSelect（即使无 InputModule），bare EventSystem 即可
            if (EventSystem.current == null)
                new GameObject("ES_NavDisabled", typeof(EventSystem));
            EventSystem.current.SetSelectedGameObject(field.GameObject);
            yield return null; yield return null;
            // 桌面默认：选中即编辑；gate 因 IsEnabled=false 不撤销
            Assert.IsTrue(field.IsEditing, "with nav disabled, default TMP behavior must be untouched");
        }

        [UnityTest]
        public IEnumerator Cancel_ExitsEditMode()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            var s = Open("<InputField id='f'/>");
            var field = s.Get<InputField>("f");
            var es = EventSystem.current;
            es.SetSelectedGameObject(field.GameObject);
            yield return null; yield return null;
            ExecuteEvents.Execute(field.GameObject, new BaseEventData(es), ExecuteEvents.submitHandler);  // 进编辑
            yield return null; yield return null;
            Assert.IsTrue(field.IsEditing, "precondition: editing");

            ExecuteEvents.Execute(field.GameObject, new BaseEventData(es), ExecuteEvents.cancelHandler);  // Esc/B
            yield return null; yield return null;
            Assert.IsFalse(field.IsEditing, "Cancel/Esc must exit edit mode back to navigation");
        }
    }
}
