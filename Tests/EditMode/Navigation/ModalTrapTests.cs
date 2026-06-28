using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class ModalTrapTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // EventSystem.current is null in EditMode; mirror the InitialFocusTests pattern.
        private static EventSystem GetEventSystem() =>
            EventSystem.current ?? Object.FindAnyObjectByType<EventSystem>();

        [Test]
        public void Containment_SnapsBack_WhenSelectionEscapes()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='outside'>O</Btn>
  <Frame id='trap'><Btn id='inside'>I</Btn></Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var es = GetEventSystem();
            Assert.IsNotNull(es, "EventSystem must exist after UseGamepadNavigation");
            UI.Navigation.ContainmentRoot = screen.Get<Frame>("trap").GameObject;
            es.SetSelectedGameObject(screen.Get<Btn>("outside").GameObject);  // 越界
            UI.Navigation.EnforceContainmentForTests();
            Assert.AreSame(screen.Get<Btn>("inside").GameObject, es.currentSelectedGameObject);
        }

        [Test]
        public void Containment_NoOp_WhenSelectionIsInside()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='outside'>O</Btn>
  <Frame id='trap'><Btn id='inside'>I</Btn></Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var es = GetEventSystem();
            Assert.IsNotNull(es, "EventSystem must exist after UseGamepadNavigation");
            var insideGo = screen.Get<Btn>("inside").GameObject;
            UI.Navigation.ContainmentRoot = screen.Get<Frame>("trap").GameObject;
            es.SetSelectedGameObject(insideGo);
            UI.Navigation.EnforceContainmentForTests();
            Assert.AreSame(insideGo, es.currentSelectedGameObject);
        }

        [Test]
        public void Containment_NoOp_WhenRootIsNull()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='outside'>O</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var es = GetEventSystem();
            Assert.IsNotNull(es, "EventSystem must exist after UseGamepadNavigation");
            var outsideGo = screen.Get<Btn>("outside").GameObject;
            UI.Navigation.ContainmentRoot = null;
            es.SetSelectedGameObject(outsideGo);
            UI.Navigation.EnforceContainmentForTests();
            Assert.AreSame(outsideGo, es.currentSelectedGameObject);
        }

        [Test]
        public void ContainmentRoot_ClearedByResetForTests()
        {
            UI.UseGamepadNavigation();
            UI.Navigation.ContainmentRoot = new GameObject("fake-root");
            UI.ResetForTests();
            Assert.IsNull(UI.Navigation.ContainmentRoot);
        }

        [Test]
        public void FirstFocusableUnder_ReturnsFirstSelectableInSubtree()
        {
            UI.UseGamepadNavigation();
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='trap'><Btn id='a'>A</Btn><Btn id='b'>B</Btn></Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var root = screen.Get<Frame>("trap").GameObject;
            var first = UI.Navigation.FirstFocusableUnder(root);
            Assert.IsNotNull(first);
            Assert.AreSame(screen.Get<Btn>("a").GameObject, first);
        }
    }
}
