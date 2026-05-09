using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ToggleTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Parses_isOn_initial_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t' isOn='true'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            Assert.IsTrue(screen.Get<Toggle>("t").IsOn);
        }

        [Test]
        public void Setter_triggers_OnValueChanged()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var t = screen.Get<Toggle>("t");

            bool? last = null;
            t.OnValueChanged.Subscribe(v => last = v);
            t.IsOn = true;
            Assert.AreEqual(true, last);
        }

        [Test]
        public void Same_group_is_mutually_exclusive()
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

            b.IsOn = true;
            Assert.IsTrue(b.IsOn);
            Assert.IsFalse(a.IsOn, "selecting b in same group should clear a");
        }

        [Test]
        public void Default_text_attr_routes_text_content()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='t'>静音</Toggle>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var t = screen.Get<Toggle>("t");
            // Toggle constructs a TMP_Text label child; verify text reached it.
            var labels = t.GameObject.GetComponentsInChildren<TMPro.TMP_Text>();
            Assert.That(labels, Has.Some.Property("text").EqualTo("静音"));
        }
    }
}
