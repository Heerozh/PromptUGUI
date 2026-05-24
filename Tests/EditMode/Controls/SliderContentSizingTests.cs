using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;
using Slider = PromptUGUI.Controls.Slider;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class SliderContentSizingTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Slider_horizontal_GetNativeSize_returns_horizontal_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            var native = slider.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(160f, native.Value.x, "horizontal Slider: long axis x = 160");
            Assert.AreEqual(44f, native.Value.y, "horizontal Slider: short axis y = 44");
        }

        [Test]
        public void Slider_vertical_GetNativeSize_returns_vertical_defaults()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Slider id='s' direction='vertical'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            var native = slider.GetNativeSize();
            Assert.IsTrue(native.HasValue);
            Assert.AreEqual(44f, native.Value.x, "vertical Slider: short axis x = 44");
            Assert.AreEqual(160f, native.Value.y, "vertical Slider: long axis y = 160");
        }

        [Test]
        public void Slider_in_Frame_no_size_sizeDelta_matches_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Slider id='s'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            Assert.AreEqual(160f, slider.RectTransform.sizeDelta.x, 0.5f);
            Assert.AreEqual(44f, slider.RectTransform.sizeDelta.y, 0.5f);
        }

        [Test]
        public void Slider_in_VStack_no_size_gets_LayoutElement_with_native_preferred()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='200'>
    <Slider id='s'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            var le = slider.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le,
                "BCS-D6 / DSS-D3: Slider under LayoutGroup with no size should auto-attach LE reporting GetNativeSize");
            Assert.AreEqual(160f, le.preferredWidth, 0.5f);
            Assert.AreEqual(44f, le.preferredHeight, 0.5f);
        }

        [Test]
        public void Slider_in_Frame_explicit_size_overrides_native()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' size='400x200'>
    <Slider id='s' size='200x40'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            Assert.AreEqual(new Vector2(200f, 40f), slider.RectTransform.sizeDelta);
        }

        [Test]
        public void Slider_direction_change_via_variant_updates_native()
        {
            // DSS-D6: ApplyCommon re-runs on Variant switch → GetNativeSize re-reads _slider.direction
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='400'>
    <Slider id='s' direction='horizontal' direction.tall='vertical'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            UI.Variants.Set("tall", false);
            var screen = UI.Open("S");
            var slider = screen.Get<Slider>("s");
            var le = slider.GameObject.GetComponent<LayoutElement>();
            Assert.AreEqual(160f, le.preferredWidth, 0.5f, "base: horizontal → preferredWidth=160");
            Assert.AreEqual(44f, le.preferredHeight, 0.5f, "base: horizontal → preferredHeight=44");

            UI.Variants.Set("tall", true);
            Assert.AreEqual(44f, le.preferredWidth, 0.5f, "tall variant: vertical → preferredWidth=44");
            Assert.AreEqual(160f, le.preferredHeight, 0.5f, "tall variant: vertical → preferredHeight=160");
        }
    }
}
