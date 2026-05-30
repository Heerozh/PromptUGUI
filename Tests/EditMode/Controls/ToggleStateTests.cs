using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;
using PuiToggle = PromptUGUI.Controls.Internal.PuiToggle;
using Toggle = PromptUGUI.Controls.Toggle;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ToggleStateTests
    {
        private const int Normal = 0, Pressed = 2;

        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        private static Toggle BuildToggle(string attrs, string body = "")
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='tg' {attrs}>{body}</Toggle>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S").Get<Toggle>("tg");
        }

        [Test]
        public void PressedColor_SwitchesTransitionNone_AndInstallsReactors()
        {
            var tg = BuildToggle("pressedColor='#808080'");
            var pt = tg.GameObject.GetComponent<PuiToggle>();
            Assert.IsNotNull(pt, "Toggle should host a PuiToggle");
            Assert.AreEqual(Selectable.Transition.None, pt.transition);
            // bg lives on the child "Background" GO; the installer walks all descendant graphics.
            var reactors = tg.GameObject.GetComponentsInChildren<StateTintReactor>(true);
            Assert.Greater(reactors.Length, 0);
        }

        [Test]
        public void Selected_ReadsWhenIsOnAtRest()
        {
            var tg = BuildToggle("selectedColor='#808080'");
            var pt = tg.GameObject.GetComponent<PuiToggle>();
            Assert.AreEqual(InteractState.Normal, pt.Current);
            tg.IsOn = true;
            Assert.AreEqual(InteractState.Selected, pt.Current);
            pt.SimulateState(Pressed);
            Assert.AreEqual(InteractState.Pressed, pt.Current);
            pt.SimulateState(Normal);
            Assert.AreEqual(InteractState.Selected, pt.Current);
        }

        [Test]
        public void NoStateColor_KeepsDefaultTransition_NoReactors()
        {
            var tg = BuildToggle("");
            var reactors = tg.GameObject.GetComponentsInChildren<StateTintReactor>(true);
            Assert.AreEqual(0, reactors.Length);
        }
    }
}
