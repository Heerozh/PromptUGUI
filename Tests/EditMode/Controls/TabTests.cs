using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;
using UnityToggle = UnityEngine.UI.Toggle;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TabTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Tab OpenTab(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Tab>("t");
        }

        [Test]
        public void Tab_Has_Bg_Toggle_And_Label_Children()
        {
            // Suppress the no-ancestor warning fired by OnAttached.
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t'/>");
            Assert.IsNotNull(t.GameObject.GetComponent<UnityImage>(), "bg UnityImage on self");
            Assert.IsNotNull(t.GameObject.GetComponent<UnityToggle>(), "UnityToggle on self");
            var label = t.GameObject.transform.Find("Label") as RectTransform;
            Assert.IsNotNull(label, "Label RT child");
            Assert.IsNotNull(label.GetComponent<TMP_Text>(), "TMP on Label");
        }

        [Test]
        public void Tab_Inside_TabBar_Has_ToggleGroup_Wired()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='t'/></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var tab = screen.Get<Tab>("t");
            var bar = screen.Get<TabBar>("bar");
            var toggle = tab.GameObject.GetComponent<UnityToggle>();
            var group = bar.GameObject.GetComponent<ToggleGroup>();
            Assert.AreSame(group, toggle.group, "Tab's UnityToggle.group is the TabBar's ToggleGroup");
        }
    }
}
