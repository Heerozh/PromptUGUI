using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine.EventSystems;
using Object = UnityEngine.Object;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class NavEnableTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Enable_IsIdempotent_AndCreatesEventSystem()
        {
            Assert.IsFalse(UI.Navigation.IsEnabled);
            UI.UseGamepadNavigation();
            UI.UseGamepadNavigation();                 // 第二次 no-op
            Assert.IsTrue(UI.Navigation.IsEnabled);
            Assert.IsNotNull(Object.FindAnyObjectByType<EventSystem>());
        }

        [Test]
        public void NoteInput_FlipsMode()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.NoteDirectionalInput();
            Assert.AreEqual(UI.Navigation.NavMode.Directional, UI.Navigation.Mode);
            UI.Navigation.NotePointerInput();
            Assert.AreEqual(UI.Navigation.NavMode.Pointer, UI.Navigation.Mode);
        }
    }
}
