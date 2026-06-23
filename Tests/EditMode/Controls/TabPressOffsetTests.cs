using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TabPressOffsetTests
    {
        private const int Normal = 0, Pressed = 2;

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // Two tabs so we can drive 'a' between Normal (select b) and Selected (select a).
        private static (Tab a, Tab b) TwoTabs(string aAttrs)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a' {aAttrs}/><Tab id='b'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            return (s.Get<Tab>("bar/a"), s.Get<Tab>("bar/b"));
        }

        private static RectTransform Holder(Control c)
            => (RectTransform)c.GameObject.GetComponentInChildren<PressOffsetController>(true).transform;

        [Test]
        public void SelectedOffset_HoldsWhileSelected()
        {
            var (a, b) = TwoTabs("selectedOffset='0,-3'");
            var holder = Holder(a);
            b.IsOn = true;   // a -> Normal
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition, "unselected -> zero");
            a.IsOn = true;   // a -> Selected
            Assert.AreEqual(new Vector2(0, -3), holder.anchoredPosition, "selected -> held offset");
        }

        [Test]
        public void Pressed_OverridesSelected_RevertsToSelected()
        {
            var (a, b) = TwoTabs("pressedOffset='0,-1' selectedOffset='0,-3'");
            var pt = a.GameObject.GetComponent<PuiToggle>();
            var holder = Holder(a);
            a.IsOn = true;                          // Selected -> -3
            Assert.AreEqual(new Vector2(0, -3), holder.anchoredPosition);
            pt.SimulateState(Pressed);              // Pressed -> -1
            Assert.AreEqual(new Vector2(0, -1), holder.anchoredPosition);
            pt.SimulateState(Normal);               // transient Normal + isOn -> Selected -> -3
            Assert.AreEqual(new Vector2(0, -3), holder.anchoredPosition);
        }

        [Test]
        public void PressedOffset_ShiftsOnPress()
        {
            var (a, b) = TwoTabs("pressedOffset='0,-2'");
            var pt = a.GameObject.GetComponent<PuiToggle>();
            var holder = Holder(a);
            pt.SimulateState(Pressed);
            Assert.AreEqual(new Vector2(0, -2), holder.anchoredPosition);
            pt.SimulateState(Normal);
            Assert.AreEqual(Vector2.zero, holder.anchoredPosition);
        }
    }
}
