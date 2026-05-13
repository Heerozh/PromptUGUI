using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ControlApplyCommonFractionalTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // ───── fractional → anchor sub-range, free-positioning parent ─────

        [Test]
        public void Width_50pct_with_anchor_center_centers_anchor_in_parent()
        {
            // anchor=center + width=50% → child occupies [0.25, 0.75] horizontally, centered.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='row' anchor='top-stretch' height='60'>
    <Btn id='b' anchor='center' width='50%' height='46'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var rt = btn.RectTransform;
            Assert.AreEqual(0.25f, rt.anchorMin.x, 0.0001f, "center + 50% → anchorMin.x = 0.25");
            Assert.AreEqual(0.75f, rt.anchorMax.x, 0.0001f, "center + 50% → anchorMax.x = 0.75");
            Assert.AreEqual(0.5f, rt.pivot.x, 0.0001f, "fractional axis forces pivot=0.5 so margin math is symmetric");
            Assert.AreEqual(0f, rt.sizeDelta.x, 0.0001f, "no margin → sizeDelta.x = 0 (anchor-driven)");
        }

        [Test]
        public void Width_50pct_with_anchor_left_pins_to_left_half()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='row' anchor='top-stretch' height='60'>
    <Btn id='b' anchor='center-left' width='50%' height='46'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var rt = btn.RectTransform;
            Assert.AreEqual(0f, rt.anchorMin.x, 0.0001f, "left + 50% → anchorMin.x = 0");
            Assert.AreEqual(0.5f, rt.anchorMax.x, 0.0001f, "left + 50% → anchorMax.x = 0.5");
        }

        [Test]
        public void Width_30pct_with_anchor_right_pins_to_right_30()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='row' anchor='top-stretch' height='60'>
    <Btn id='b' anchor='center-right' width='30%' height='46'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var rt = btn.RectTransform;
            Assert.AreEqual(0.7f, rt.anchorMin.x, 0.0001f, "right + 30% → anchorMin.x = 0.7");
            Assert.AreEqual(1f, rt.anchorMax.x, 0.0001f, "right + 30% → anchorMax.x = 1");
        }

        [Test]
        public void Height_50pct_with_anchor_top_pins_to_upper_half()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='col' anchor='stretch'>
    <Btn id='b' anchor='top-center' width='100' height='50%'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var rt = btn.RectTransform;
            Assert.AreEqual(0.5f, rt.anchorMin.y, 0.0001f, "top + 50% → anchorMin.y = 0.5");
            Assert.AreEqual(1f, rt.anchorMax.y, 0.0001f, "top + 50% → anchorMax.y = 1");
            Assert.AreEqual(0.5f, rt.pivot.y, 0.0001f);
        }

        [Test]
        public void Height_50pct_with_anchor_bottom_pins_to_lower_half()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='col' anchor='stretch'>
    <Btn id='b' anchor='bottom-center' width='100' height='50%'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var rt = btn.RectTransform;
            Assert.AreEqual(0f, rt.anchorMin.y, 0.0001f);
            Assert.AreEqual(0.5f, rt.anchorMax.y, 0.0001f);
        }

        [Test]
        public void Margin_insets_within_fractional_range()
        {
            // width=50% at anchor=center gives anchorMin/Max = 0.25/0.75. Margin 16 each side
            // further insets: sizeDelta.x = -32 (margin sum, negative because it shrinks).
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='row' anchor='top-stretch' height='60'>
    <Btn id='b' anchor='center' width='50%' height='46' margin='_,16,_,16'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var rt = btn.RectTransform;
            Assert.AreEqual(0.25f, rt.anchorMin.x, 0.0001f);
            Assert.AreEqual(0.75f, rt.anchorMax.x, 0.0001f);
            Assert.AreEqual(-32f, rt.sizeDelta.x, 0.0001f, "margin 16 each side → sizeDelta.x = -32");
            Assert.AreEqual(0f, rt.anchoredPosition.x, 0.0001f, "symmetric margin → centered");
        }

        // ───── invalid combinations ─────

        [Test]
        public void Fractional_under_VStack_throws_with_actionable_guidance()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b' width='50%' height='46'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var ex = Assert.Throws<System.ArgumentException>(() => UI.Open("S"));
            // Error must point the author at the right alternatives.
            StringAssert.Contains("stretch", ex.Message);
            StringAssert.Contains("spacer", ex.Message);
        }

        [Test]
        public void Fractional_under_HStack_throws()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='200' height='200'>
    <Btn id='b' width='100' height='50%'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            Assert.Throws<System.ArgumentException>(() => UI.Open("S"));
        }

        [Test]
        public void Fractional_under_Grid_throws()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Grid id='grid' columns='2' cellSize='40x40' width='200' height='200'>
    <Btn id='b' width='50%'/>
  </Grid>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            Assert.Throws<System.ArgumentException>(() => UI.Open("S"));
        }

        [Test]
        public void Fractional_on_anchor_stretched_axis_throws()
        {
            // anchor=top-stretch already fills horizontal axis — combining with width=50% is incoherent.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='row' anchor='stretch'>
    <Btn id='b' anchor='top-stretch' width='50%' height='46'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            Assert.Throws<System.ArgumentException>(() => UI.Open("S"));
        }

        [Test]
        public void Size_with_percent_throws()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='row' anchor='stretch'>
    <Btn id='b' anchor='center' size='50%'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            Assert.Throws<System.ArgumentException>(() => UI.Open("S"));
        }
    }
}
