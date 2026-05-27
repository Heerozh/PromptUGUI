using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class TabBarPlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator TabBar_Runtime_Switching_Mutex_And_Bind()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a' bind='fa' isOn='true'/>
    <Tab id='b' bind='fb'/>
  </TabBar>
  <Frame id='fa'/>
  <Frame id='fb'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            yield return null;

            var a = screen.Get<Tab>("a");
            var b = screen.Get<Tab>("b");
            var fa = screen.Get<Frame>("fa");
            var fb = screen.Get<Frame>("fb");

            Assert.IsTrue(a.IsOn);
            Assert.IsFalse(b.IsOn);
            Assert.IsTrue(fa.GameObject.activeSelf);
            Assert.IsFalse(fb.GameObject.activeSelf);

            b.IsOn = true;
            yield return null;

            Assert.IsFalse(a.IsOn, "mutex demoted a");
            Assert.IsTrue(b.IsOn);
            Assert.IsFalse(fa.GameObject.activeSelf);
            Assert.IsTrue(fb.GameObject.activeSelf);
        }
    }
}
