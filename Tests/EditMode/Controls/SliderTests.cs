using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class SliderTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Parses_min_max_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' min='0' max='10' value='3'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var s = screen.Get<Slider>("s");
            Assert.AreEqual(3f, s.Value);
        }

        [Test]
        public void Setter_triggers_OnValueChanged()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' min='0' max='1'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var s = screen.Get<Slider>("s");
            float? last = null;
            s.OnValueChanged.Subscribe(v => last = v);
            s.Value = 0.5f;
            Assert.AreEqual(0.5f, last);
        }

        [Test]
        public void Direction_parses_to_unity_enum()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' direction='vertical'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var s = screen.Get<Slider>("s");
            var u = s.GameObject.GetComponent<UnityEngine.UI.Slider>();
            Assert.AreEqual(UnityEngine.UI.Slider.Direction.BottomToTop, u.direction);
        }
    }
}
