using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TabBarBindItemsTests
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
        public void BindItems_With_Default_Template_Instantiates_Tabs()
        {
            var bar = OpenBar("<TabBar id='bar'/>");
            using var sub = bar.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "A", "B", "C" }),
                (Tab tab, string s) => tab.Text = s);

            Assert.AreEqual(3, bar.Count);
            Assert.AreEqual("A", bar.GetAt(0).GameObject.transform.Find("Label").GetComponent<TMPro.TMP_Text>().text);
        }

        [Test]
        public void BindItems_Clears_Existing_Static_Tabs()
        {
            var bar = OpenBar("<TabBar id='bar'><Tab text='static1'/><Tab text='static2'/></TabBar>");
            Assert.AreEqual(2, bar.Count, "starts with 2 static tabs");

            using var sub = bar.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "dyn1" }),
                (Tab tab, string s) => tab.Text = s);

            Assert.AreEqual(1, bar.Count, "BindItems clears static and rebuilds from dynamic");
        }

        [Test]
        public void BindItems_With_Empty_List_Leaves_TabBar_Empty()
        {
            var bar = OpenBar("<TabBar id='bar'><Tab text='static'/></TabBar>");
            using var sub = bar.BindItems(
                Observable.Return<IReadOnlyList<string>>(new string[0]),
                (Tab tab, string s) => tab.Text = s);
            Assert.AreEqual(0, bar.Count);
            Assert.AreEqual(-1, bar.SelectedIndex);
        }
    }
}
