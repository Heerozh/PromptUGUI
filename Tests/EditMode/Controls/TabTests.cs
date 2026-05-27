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

        [Test]
        public void Tab_Text_Sets_Label()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' text='Hello'/>");
            var label = t.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            Assert.AreEqual("Hello", label.text);
        }

        [Test]
        public void Tab_With_Empty_Text_Has_Empty_Label()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t'/>");
            var label = t.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            Assert.AreEqual("", label.text);
        }

        [Test]
        public void Tab_FontSize_Sets_TMP_FontSize()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' text='X' fontSize='18'/>");
            var label = t.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            Assert.AreEqual(18f, label.fontSize);
        }

        [Test]
        public void Tab_Default_FontSize_Is_24()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' text='X'/>");
            var label = t.GameObject.transform.Find("Label").GetComponent<TMP_Text>();
            Assert.AreEqual(24f, label.fontSize);
        }

        [Test]
        public void Tab_With_Icon_Creates_Icon_Child()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            LogAssert.Expect(LogType.Error,
                new System.Text.RegularExpressions.Regex("UI.SpriteResolver is not registered"));
            var t = OpenTab("<Tab id='t' text='X' icon='ui:nope'/>");
            var icon = t.GameObject.transform.Find("Icon") as RectTransform;
            Assert.IsNotNull(icon, "Icon RT child created");
            Assert.IsNotNull(icon.GetComponent<UnityImage>(), "Icon UnityImage");
            Assert.IsFalse(icon.GetComponent<UnityImage>().raycastTarget, "Icon does not block raycasts");
        }

        [Test]
        public void Tab_Without_Icon_Attr_Has_No_Icon_Child()
        {
            LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex("Tab.*has no.*TabBar.*ancestor"));
            var t = OpenTab("<Tab id='t' text='X'/>");
            Assert.IsNull(t.GameObject.transform.Find("Icon"), "no Icon RT when icon attr absent");
        }
    }
}
