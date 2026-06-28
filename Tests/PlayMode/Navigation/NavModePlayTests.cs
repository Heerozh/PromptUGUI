using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PromptUGUI.Tests.PlayMode.Navigation
{
    public class NavModePlayTests : UnityEngine.InputSystem.InputTestFixture
    {
        [UnityTest]
        public IEnumerator GamepadInput_FlipsToDirectional()
        {
            UI.ResetForTests();
            var pad = InputSystem.AddDevice<Gamepad>();
            UI.UseGamepadNavigation();
            yield return null;
            Set(pad.leftStick, new Vector2(1f, 0f));   // InputTestFixture 注入
            yield return null;
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode);
            UI.ResetForTests();
        }
    }
}
