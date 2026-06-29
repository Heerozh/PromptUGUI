using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class FocusTintTests
    {
        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { StateTintReactor.TestForceInstant = false; UI.ResetForTests(); }

        private static (Btn btn, UnityEngine.UI.Image bg) Build(string attrs)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='b' {attrs}>Hi</Btn></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            return (btn, btn.GameObject.GetComponent<UnityEngine.UI.Image>());
        }

        [Test]
        public void DirectionalFocus_ReusesHoverColor()
        {
            var (btn, bg) = Build("hoverColor='#ff0000'");
            var pui = btn.GameObject.GetComponent<PuiButton>();
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            pui.SimulateState(3);                       // focus
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), bg.color);   // == hover
        }

        [Test]
        public void Focus_NoHoverSet_LeavesBaseColor()
        {
            var (btn, bg) = Build("");
            var baseColor = bg.color;
            var pui = btn.GameObject.GetComponent<PuiButton>();
            UI.Navigation.Mode = UI.Navigation.NavMode.Directional;
            pui.SimulateState(3);
            Assert.AreEqual(baseColor, bg.color);        // 手指兜底，控件不变色
        }
    }
}
