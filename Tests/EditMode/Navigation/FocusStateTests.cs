using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class FocusStateTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PuiButton BuildBtn()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='b'>Hi</Btn></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            return screen.Get<Btn>("b").GameObject.GetComponent<PuiButton>();
        }

        [Test]
        public void MapTransient_NavigationSelected_IsFocused()
            => Assert.AreEqual(InteractState.Focused, StateBroadcaster.MapTransient(3));

        [Test]
        public void Focus_FoldsToNormal_InPointerMode()
        {
            var pui = BuildBtn();
            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            pui.SimulateState(3);                       // uGUI navigation-Selected
            Assert.AreEqual(InteractState.Normal, pui.Current);
        }

        [Test]
        public void Focus_IsVisible_InDirectionalMode()
        {
            var pui = BuildBtn();
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            pui.SimulateState(3);
            Assert.AreEqual(InteractState.Focused, pui.Current);
        }

        [Test]
        public void RefreshState_RepaintsOnModeFlip()
        {
            var pui = BuildBtn();
            UI.Navigation.Mode = UI.Navigation.NavMode.Pointer;
            pui.SimulateState(3);
            Assert.AreEqual(InteractState.Normal, pui.Current);   // pointer: invisible
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            pui.RefreshState();                                    // re-poke, no uGUI state change
            Assert.AreEqual(InteractState.Focused, pui.Current);
        }
    }
}
