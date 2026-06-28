using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Navigation
{
    public class ExplicitNavTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Screen Open(string body)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        [Test]
        public void NavNone_SetsModeNone()
        {
            var s = Open("<Btn id='a' nav='none'>A</Btn>");
            var sel = s.Get<Btn>("a").GameObject.GetComponent<Selectable>();
            Assert.AreEqual(UnityEngine.UI.Navigation.Mode.None, sel.navigation.mode);
        }

        [Test]
        public void NavUp_SetsExplicitToTarget()
        {
            var s = Open("<Btn id='a'>A</Btn><Btn id='b' navUp='a'>B</Btn>");
            var b = s.Get<Btn>("b").GameObject.GetComponent<Selectable>();
            var a = s.Get<Btn>("a").GameObject.GetComponent<Selectable>();
            Assert.AreEqual(UnityEngine.UI.Navigation.Mode.Explicit, b.navigation.mode);
            Assert.AreSame(a, b.navigation.selectOnUp);
        }
    }
}
