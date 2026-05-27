using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
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
    }
}
