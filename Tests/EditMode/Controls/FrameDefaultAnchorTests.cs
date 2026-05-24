using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using Frame = PromptUGUI.Controls.Frame;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class FrameDefaultAnchorTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Frame_no_anchor_no_size_fills_parent()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame size='400x200'>
    <Frame id='inner'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var inner = screen.Get<Frame>("inner").RectTransform;
            Assert.AreEqual(Vector2.zero, inner.anchorMin, "stretch X+Y → anchorMin=(0,0)");
            Assert.AreEqual(Vector2.one, inner.anchorMax, "stretch X+Y → anchorMax=(1,1)");
            Assert.AreEqual(Vector2.zero, inner.sizeDelta, "no margin → sizeDelta=0 = match parent");
        }

        [Test]
        public void Frame_width_only_stretches_height()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame size='400x200'>
    <Frame id='inner' width='100'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var inner = screen.Get<Frame>("inner").RectTransform;
            Assert.AreEqual(0f, inner.anchorMin.x, 0.001f, "X axis Left → anchorMin.x=0");
            Assert.AreEqual(0f, inner.anchorMax.x, 0.001f, "X axis Left → anchorMax.x=0");
            Assert.AreEqual(0f, inner.anchorMin.y, 0.001f, "Y axis Stretch → anchorMin.y=0");
            Assert.AreEqual(1f, inner.anchorMax.y, 0.001f, "Y axis Stretch → anchorMax.y=1");
            Assert.AreEqual(100f, inner.sizeDelta.x, 0.5f, "X explicit 100");
            Assert.AreEqual(0f, inner.sizeDelta.y, 0.5f, "Y stretch + no margin → sizeDelta.y=0 (match parent)");
        }

        [Test]
        public void Frame_height_only_stretches_width()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame size='400x200'>
    <Frame id='inner' height='50'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var inner = screen.Get<Frame>("inner").RectTransform;
            Assert.AreEqual(0f, inner.anchorMin.x, 0.001f);
            Assert.AreEqual(1f, inner.anchorMax.x, 0.001f);
            Assert.AreEqual(1f, inner.anchorMin.y, 0.001f, "Y axis Top → anchorMin.y=1");
            Assert.AreEqual(1f, inner.anchorMax.y, 0.001f, "Y axis Top → anchorMax.y=1");
            Assert.AreEqual(0f, inner.sizeDelta.x, 0.5f, "X stretch → 0");
            Assert.AreEqual(50f, inner.sizeDelta.y, 0.5f, "Y explicit 50");
        }

        [Test]
        public void Frame_explicit_size_both_axes_uses_top_left_default()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame size='400x200'>
    <Frame id='inner' size='100x50'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var inner = screen.Get<Frame>("inner").RectTransform;
            Assert.AreEqual(new Vector2(0f, 1f), inner.anchorMin, "both fixed → top-left preset");
            Assert.AreEqual(new Vector2(0f, 1f), inner.anchorMax);
            Assert.AreEqual(new Vector2(100f, 50f), inner.sizeDelta);
        }

        [Test]
        public void Frame_explicit_anchor_skips_fill_or_fit_default()
        {
            // DSS-D15: 显式写 anchor 时按原规则，不走"按轴 fill-or-fit"
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame size='400x200'>
    <Frame id='inner' anchor='center' width='100'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var inner = screen.Get<Frame>("inner").RectTransform;
            Assert.AreEqual(new Vector2(0.5f, 0.5f), inner.anchorMin, "anchor=center 明文");
            Assert.AreEqual(new Vector2(0.5f, 0.5f), inner.anchorMax);
            Assert.AreEqual(100f, inner.sizeDelta.x, 0.5f);
            Assert.AreEqual(0f, inner.sizeDelta.y, 0.5f, "no height + center 不走 stretch default");
        }
    }
}
