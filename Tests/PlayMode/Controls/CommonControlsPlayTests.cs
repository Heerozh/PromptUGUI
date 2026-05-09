using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class CommonControlsPlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator Toggle_group_runtime_switching()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack>
    <Toggle id='a' group='g' isOn='true'/>
    <Toggle id='b' group='g'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var a = screen.Get<Toggle>("a");
            var b = screen.Get<Toggle>("b");

            yield return null;  // give Unity one frame to wire up ToggleGroup

            b.IsOn = true;
            yield return null;
            Assert.IsFalse(a.IsOn);
            Assert.IsTrue(b.IsOn);
        }

        [UnityTest]
        public IEnumerator ScrollList_renders_via_real_layout()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack height='32'><Text id='label'/></HStack></Template>
  <Screen name='S'>
    <ScrollList id='list' anchor='center' size='400x300' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "alpha", "beta", "gamma" }),
                (IControl slot, string s) => slot.Get<Text>("label").TextValue = s);

            yield return null;
            yield return null;
            Assert.AreEqual(3, list.SlotCount);
        }
    }
}
