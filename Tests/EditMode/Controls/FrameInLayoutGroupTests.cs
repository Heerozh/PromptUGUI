using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine.UI;
using Btn = PromptUGUI.Controls.Btn;
using Frame = PromptUGUI.Controls.Frame;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class FrameInLayoutGroupTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Frame_in_VStack_height_only_gets_cross_axis_flex_width()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='400'>
    <Frame id='f' height='180'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var le = screen.Get<Frame>("f").GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "Frame in VStack must have LayoutElement so cross axis can fill");
            Assert.AreEqual(0f, le.preferredWidth, 0.5f, "DSS-D16: cross axis preferred=0");
            Assert.AreEqual(1f, le.flexibleWidth, 0.5f, "DSS-D16: cross axis flexible=1 → fills VStack width");
            Assert.AreEqual(180f, le.preferredHeight, 0.5f, "explicit height preserved");
            Assert.AreEqual(0f, le.flexibleHeight, 0.5f, "explicit height → flex=0 main axis");
        }

        [Test]
        public void Frame_in_HStack_width_only_gets_cross_axis_flex_height()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='400' height='200'>
    <Frame id='f' width='180'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var le = screen.Get<Frame>("f").GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            Assert.AreEqual(180f, le.preferredWidth, 0.5f);
            Assert.AreEqual(0f, le.flexibleWidth, 0.5f);
            Assert.AreEqual(0f, le.preferredHeight, 0.5f, "DSS-D16: cross axis preferred=0");
            Assert.AreEqual(1f, le.flexibleHeight, 0.5f, "DSS-D16: cross axis flexible=1 → fills HStack height");
        }

        [Test]
        public void Frame_in_VStack_no_size_gets_cross_axis_flex_only()
        {
            // <Frame/> in VStack: cross axis fills, main axis no signal → height collapses to 0
            // (matches CSS empty <div> in flex column: width 100%, height 0)
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='400'>
    <Frame id='f'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var le = screen.Get<Frame>("f").GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "Frame no-size in VStack must still have LE for cross fill");
            Assert.AreEqual(0f, le.preferredWidth, 0.5f);
            Assert.AreEqual(1f, le.flexibleWidth, 0.5f);
            Assert.AreEqual(-1f, le.preferredHeight, 0.5f, "no height signal → main axis preferred=-1");
            Assert.AreEqual(-1f, le.flexibleHeight, 0.5f, "no height signal → main axis flexible=-1");
        }

        [Test]
        public void Frame_in_VStack_explicit_top_left_anchor_skips_cross_fill()
        {
            // 显式写 anchor='top-left' 等于作者明说"不要 stretch" → 不应自动 cross fill
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='400'>
    <Frame id='f' anchor='top-left' height='180'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var le = screen.Get<Frame>("f").GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            Assert.AreEqual(-1f, le.preferredWidth, 0.5f, "explicit anchor=top-left → no cross fill");
            Assert.AreEqual(-1f, le.flexibleWidth, 0.5f, "explicit anchor=top-left → no cross fill");
            Assert.AreEqual(180f, le.preferredHeight, 0.5f);
        }

        [Test]
        public void Btn_in_VStack_no_size_keeps_native_no_cross_fill()
        {
            // DSS-D17: Btn.GetDefaultAnchor 没覆写 → preset (Top, Left) → 不触发 cross fill；走 native 路径
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='400' height='200'>
    <Btn id='b'>OK</Btn>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var le = screen.Get<Btn>("b").GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            Assert.Greater(le.preferredWidth, 0f, "Btn native preferred (label + padding) > 0, NOT 0+flex=1");
            Assert.AreEqual(-1f, le.flexibleWidth, 0.5f, "Btn must not be force-stretched in VStack");
            Assert.AreEqual(44f, le.preferredHeight, 0.5f);
        }
    }
}
