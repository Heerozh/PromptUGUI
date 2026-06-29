using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
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
    }
}
