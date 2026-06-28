using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Navigation
{
    public class FocusCursorPlayTests
    {
        [UnityTest]
        public IEnumerator Cursor_FollowsSelectionChange()
        {
            UI.ResetForTests();
            UI.UseGamepadNavigation();
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <FocusCursor side='left'><Image id='hand' size='16x16'/></FocusCursor>
  <VStack spacing='40'><Btn id='a' size='100x40'>A</Btn><Btn id='b' size='100x40'>B</Btn></VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var overlay = (RectTransform)screen.RootGameObject.transform.Find("__FocusCursor");
            Assert.IsNotNull(overlay, "__FocusCursor overlay must exist");
            EventSystem.current.SetSelectedGameObject(screen.Get<Btn>("a").GameObject);
            yield return null; yield return null;
            var posA = overlay.anchoredPosition;
            EventSystem.current.SetSelectedGameObject(screen.Get<Btn>("b").GameObject);
            yield return null; yield return null; yield return null;
            Assert.AreNotEqual(posA.y, overlay.anchoredPosition.y, "Cursor must move to B");
            UI.ResetForTests();
        }
    }
}
