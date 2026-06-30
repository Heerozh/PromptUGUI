using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TogglePressOffsetTests
    {
        private const int Normal = 0, Pressed = 2;

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Toggle BuildToggle(string attrs)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t' {attrs}>Opt</Toggle>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Toggle>("t");
        }

        private static RectTransform Holder(Control c)
            => (RectTransform)c.GameObject.GetComponentInChildren<PressOffsetController>(true).transform;

        [Test]
        public void SelectedOffset_HoldsWhenOn()
        {
            var tog = BuildToggle("selectedOffset='0,-3'");
            var holder = Holder(tog);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "off -> zero");
            tog.IsOn = true;
            Assert.AreEqual(new Vector2(0, -3), holder.anchoredPosition, "on -> held offset");
            tog.IsOn = false;
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "off again -> zero");
        }

        [Test]
        public void AuthoredIsOn_ShowsSelectedOffsetOnFrameOne()
        {
            var tog = BuildToggle("isOn='true' selectedOffset='0,-3'");
            var holder = Holder(tog);
            Assert.AreEqual(new Vector2(0, -3), holder.anchoredPosition,
                "isOn at open -> selected offset established instantly (first-frame)");
        }

        [Test]
        public void PressedOffset_ShiftsOnPress()
        {
            var tog = BuildToggle("pressedOffset='0,-2'");
            var pt = tog.GameObject.GetComponent<PuiToggle>();
            var holder = Holder(tog);
            pt.SimulateState(Pressed);
            Assert.AreEqual(new Vector2(0, -2), holder.anchoredPosition);
            pt.SimulateState(Normal);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition);
        }
    }
}
