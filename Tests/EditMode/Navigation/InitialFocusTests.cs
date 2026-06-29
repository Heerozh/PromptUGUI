using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class InitialFocusTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // EventSystem.current is null in EditMode; use FindAnyObjectByType to locate the
        // instance created by UI.UseGamepadNavigation() — the same pattern as NavEnableTests.
        private static EventSystem GetEventSystem() =>
            EventSystem.current ?? Object.FindAnyObjectByType<EventSystem>();

        [Test]
        public void Open_SelectsControlMarkedFocus()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack><Btn id='a'>A</Btn><Btn id='b' focus='true'>B</Btn></VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var es = GetEventSystem();
            Assert.IsNotNull(es, "EventSystem must exist after UseGamepadNavigation");
            Assert.AreSame(screen.Get<Btn>("b").GameObject, es.currentSelectedGameObject);
        }

        [Test]
        public void Open_NoMarker_SelectsFirstFocusable()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack><Image id='img'/><Btn id='a'>A</Btn><Btn id='b'>B</Btn></VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var es = GetEventSystem();
            Assert.IsNotNull(es, "EventSystem must exist after UseGamepadNavigation");
            Assert.AreSame(screen.Get<Btn>("a").GameObject, es.currentSelectedGameObject);
        }

        [Test]
        public void Focus_ProgrammaticallySelects()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='a'>A</Btn></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            screen.Focus("a");
            var es = GetEventSystem();
            Assert.IsNotNull(es, "EventSystem must exist after UseGamepadNavigation");
            Assert.AreSame(screen.Get<Btn>("a").GameObject, es.currentSelectedGameObject);
        }
    }
}
