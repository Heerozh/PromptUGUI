using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class FocusCursorPositionTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // EventSystem.current is null in EditMode; mirror InitialFocusTests pattern.
        private static EventSystem GetEventSystem() =>
            EventSystem.current ?? Object.FindAnyObjectByType<EventSystem>();

        [Test]
        public void Cursor_HiddenInPointerMode_VisibleAndTracksInDirectional()
        {
            UI.UseGamepadNavigation();
            Assert.IsTrue(UI.Navigation.IsEnabled, "Navigation must be enabled");
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <FocusCursor side='left'><Image id='hand' size='16x16'/></FocusCursor>
  <Btn id='a' anchor='center' size='100x40'>A</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var es = GetEventSystem();
            Assert.IsNotNull(es, "EventSystem must exist after UseGamepadNavigation");
            es.SetSelectedGameObject(screen.Get<Btn>("a").GameObject);

            var overlayTf = screen.RootGameObject.transform.Find("__FocusCursor");
            Assert.IsNotNull(overlayTf, "__FocusCursor overlay must exist (Navigation.IsEnabled=" + UI.Navigation.IsEnabled + ")");
            var overlay = overlayTf.GetComponent<CanvasGroup>();
            Assert.IsNotNull(overlay, "CanvasGroup on __FocusCursor must exist");
            var view = overlay.GetComponent<PromptUGUI.Application.Navigation.FocusCursorView>();
            Assert.IsNotNull(view, "FocusCursorView on __FocusCursor must exist");

            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            view.TickForTests();
            Assert.AreEqual(0f, overlay.alpha);                 // pointer: hidden

            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            view.TickForTests();
            Assert.AreEqual(1f, overlay.alpha);                 // directional: visible
        }
    }
}
