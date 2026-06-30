using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    // Full-screen page (no modal ContainmentRoot). Clicking the page background / a non-Selectable
    // child clears the EventSystem selection in uGUI. Directional input must then RE-ACQUIRE focus
    // instead of going dead. Mirrors ModalTrapTests' EnforceContainmentForTests drive pattern.
    public class PageFocusRecoveryTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // EventSystem.current is null in EditMode; mirror the ModalTrapTests pattern.
        private static EventSystem GetEventSystem() =>
            EventSystem.current ?? Object.FindAnyObjectByType<EventSystem>();

        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack><Btn id='a'>A</Btn><Btn id='b'>B</Btn></VStack>
</Screen></PromptUGUI>";

        [Test]
        public void Directional_RestoresLastFocus_AfterSelectionLost()
        {
            UI.UseGamepadNavigation();
            UI.LoadDocument("t", Xml);
            var screen = UI.Open("S");
            var es = GetEventSystem();
            Assert.IsNotNull(es, "EventSystem must exist after UseGamepadNavigation");
            var a = screen.Get<Btn>("a").GameObject;

            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            es.SetSelectedGameObject(a);
            UI.Navigation.EnforceContainmentForTests();   // remembers 'a' as last good focus

            es.SetSelectedGameObject(null);               // click on background clears selection
            UI.Navigation.EnforceContainmentForTests();   // directional re-acquire

            Assert.AreSame(a, es.currentSelectedGameObject,
                "directional input after selection-loss must restore the last focused control");
        }

        [Test]
        public void Pointer_DoesNotRestore_AfterSelectionLost()
        {
            UI.UseGamepadNavigation();
            UI.LoadDocument("t", Xml);
            var screen = UI.Open("S");
            var es = GetEventSystem();
            Assert.IsNotNull(es, "EventSystem must exist after UseGamepadNavigation");
            var a = screen.Get<Btn>("a").GameObject;

            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            es.SetSelectedGameObject(a);
            UI.Navigation.EnforceContainmentForTests();   // remembers 'a'

            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            es.SetSelectedGameObject(null);               // pointer click clears selection
            UI.Navigation.EnforceContainmentForTests();

            Assert.IsNull(es.currentSelectedGameObject,
                "pointer-mode click-away must stay deselected (cursor is hidden)");
        }
    }
}
