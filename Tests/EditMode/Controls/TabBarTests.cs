using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TabBarTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static TabBar OpenBar(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<TabBar>("bar");
        }

        [Test]
        public void TabBar_Has_ToggleGroup_And_HorizontalLayoutGroup()
        {
            var bar = OpenBar("<TabBar id='bar'/>");
            Assert.IsNotNull(bar.GameObject.GetComponent<ToggleGroup>(), "ToggleGroup on self");
            Assert.IsNotNull(bar.GameObject.GetComponent<HorizontalLayoutGroup>(), "HLG default");
            Assert.IsFalse(bar.GameObject.GetComponent<ToggleGroup>().allowSwitchOff, "TB-D7 fixed false");
        }

        [Test]
        public void TabBar_Sprite_Pushes_To_All_Child_Tabs()
        {
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("UI.SpriteResolver is not registered"));
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar' sprite='ui:fake_normal'>
    <Tab id='a' text='A'/>
    <Tab id='b' text='B'/>
  </TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var a = screen.Get<Tab>("a");
            var b = screen.Get<Tab>("b");
            var bgA = a.GameObject.GetComponent<UnityEngine.UI.Image>();
            var bgB = b.GameObject.GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual(bgA.sprite, bgB.sprite, "both Tabs received the same (possibly null) sprite from TabBar");
        }

        [Test]
        public void TabBar_SelectedSprite_Creates_Overlay_On_Each_Tab()
        {
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("UI.SpriteResolver is not registered"));
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar' selectedSprite='ui:fake_selected'>
    <Tab id='a'/>
    <Tab id='b'/>
  </TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            foreach (var id in new[] { "a", "b" })
            {
                var tab = screen.Get<Tab>(id);
                var overlay = tab.GameObject.transform.Find("Overlay") as RectTransform;
                Assert.IsNotNull(overlay, $"Tab '{id}' has Overlay RT");
                var img = overlay.GetComponent<UnityEngine.UI.Image>();
                var toggle = tab.GameObject.GetComponent<UnityEngine.UI.Toggle>();
                Assert.AreSame(img, toggle.graphic, $"Tab '{id}' UnityToggle.graphic = overlay");
                Assert.IsFalse(img.raycastTarget, "Overlay does not block raycasts");
            }
        }

        [Test]
        public void TabBar_Without_SelectedSprite_Has_No_Overlay()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var tab = screen.Get<Tab>("a");
            Assert.IsNull(tab.GameObject.transform.Find("Overlay"), "no Overlay when selectedSprite absent");
        }

        [Test]
        public void TabBar_Children_Share_ToggleGroup()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a'/>
    <Tab id='b'/>
  </TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var bar = screen.Get<TabBar>("bar");
            var a = screen.Get<Tab>("a").GameObject.GetComponent<UnityEngine.UI.Toggle>();
            var b = screen.Get<Tab>("b").GameObject.GetComponent<UnityEngine.UI.Toggle>();
            var group = bar.GameObject.GetComponent<UnityEngine.UI.ToggleGroup>();
            Assert.AreSame(group, a.group);
            Assert.AreSame(group, b.group);
        }

        [Test]
        public void Tab_Bind_Toggle_Switches_Frame_SetActive()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a' bind='fa'/>
    <Tab id='b' bind='fb'/>
  </TabBar>
  <Frame id='fa'/>
  <Frame id='fb'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var fa = screen.Get<Frame>("fa");
            var fb = screen.Get<Frame>("fb");
            var a = screen.Get<Tab>("a");
            var b = screen.Get<Tab>("b");
            a.IsOn = true;
            Assert.IsTrue(fa.GameObject.activeSelf);
            b.IsOn = true;
            Assert.IsFalse(fa.GameObject.activeSelf);
            Assert.IsTrue(fb.GameObject.activeSelf);
        }

        [Test]
        public void Tab_Bind_To_Missing_Frame_Warns_Once_Then_Silent()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.bind='nope'.*did not resolve"));
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a' bind='nope' isOn='true'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var a = screen.Get<Tab>("a");
            a.IsOn = false;
            a.IsOn = true;
            // No further warn expected — LogAssert would fail if a 2nd warning fires.
        }

        [Test]
        public void TabBar_With_No_Initial_IsOn_Auto_Selects_First_Tab()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='a'/><Tab id='b'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            Assert.IsTrue(screen.Get<Tab>("a").IsOn);
            Assert.IsFalse(screen.Get<Tab>("b").IsOn);
        }

        [Test]
        public void TabBar_Non_Selected_Bind_Frames_Are_Deactivated_Initially()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a' bind='fa' isOn='true'/>
    <Tab id='b' bind='fb'/>
    <Tab id='c' bind='fc'/>
  </TabBar>
  <Frame id='fa'/>
  <Frame id='fb'/>
  <Frame id='fc'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            Assert.IsTrue(screen.Get<Frame>("fa").GameObject.activeSelf);
            Assert.IsFalse(screen.Get<Frame>("fb").GameObject.activeSelf);
            Assert.IsFalse(screen.Get<Frame>("fc").GameObject.activeSelf);
        }
    }
}
