using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ControlApplyCommonLayoutGroupTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Btn_in_VStack_with_size_writes_LayoutElement_preferred_with_flexible_zero()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b' size='64x64'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "Btn inside VStack with size= must get a LayoutElement");
            Assert.AreEqual(64f, le.preferredWidth);
            Assert.AreEqual(64f, le.preferredHeight);
            Assert.AreEqual(0f, le.flexibleWidth);
            Assert.AreEqual(0f, le.flexibleHeight);
        }

        [Test]
        public void Btn_in_VStack_with_only_width_leaves_height_axis_unconstrained()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b' width='100'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            Assert.AreEqual(100f, le.preferredWidth);
            Assert.AreEqual(0f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.preferredHeight,
                "Unspecified height axis must be -1 (LayoutElement 'ignored' sentinel)");
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Btn_in_VStack_with_no_size_attrs_gets_no_LayoutElement()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            Assert.IsNull(btn.GameObject.GetComponent<LayoutElement>(),
                "No size/width/height -> no LayoutElement; intrinsic ILayoutElement (Image/TMP) drives sizing");
        }

        [Test]
        public void Btn_in_Frame_with_size_writes_sizeDelta_not_LayoutElement()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='frame' anchor='stretch' margin='0'>
    <Btn id='b' size='64x64'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            Assert.IsNull(btn.GameObject.GetComponent<LayoutElement>(),
                "Btn under Frame (non-LayoutGroup parent) must NOT get a LayoutElement");
            Assert.AreEqual(new Vector2(64f, 64f), btn.RectTransform.sizeDelta,
                "Non-LayoutGroup parent: size still writes to sizeDelta (existing behavior preserved)");
        }

        [Test]
        public void Btn_in_Grid_with_size_gets_no_LayoutElement()
        {
            // Grid uses cellSize for all children; LayoutElement on Grid children is ignored
            // by GridLayoutGroup, so we don't add one — avoids the appearance of a knob that
            // actually does nothing.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Grid id='grid' columns='2' cellSize='40x40' width='200' height='200'>
    <Btn id='b' size='64x64'/>
  </Grid>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            Assert.IsNull(btn.GameObject.GetComponent<LayoutElement>(),
                "Btn under Grid must NOT get a LayoutElement (GridLayoutGroup ignores it; cellSize wins)");
        }

        [Test]
        public void Variant_switch_from_size_to_width_only_resets_height_axis()
        {
            // 验证 ApplyLayoutElement 的"先重置两轴再写入"逻辑：
            // 切换变体让 height 轴从"被 size=指定为 64"变成"未指定"，preferredHeight 必须
            // 从 64 重置到 -1，而不是残留前一轮的值。
            //
            // 用法：base 提供 size=64x64；mobile 激活时把 size 覆写为空 + 提供 width=100。
            // (SizeSpec.Parse 视空串为"未指定"，所以 mobile 下只有 width 轴被指定。)
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b' size='64x64' size.mobile='' width.mobile='100'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            UI.Variants.Set("mobile", false);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.AreEqual(64f, le.preferredWidth, "desktop: size= writes both axes");
            Assert.AreEqual(64f, le.preferredHeight);
            Assert.AreEqual(0f, le.flexibleWidth);
            Assert.AreEqual(0f, le.flexibleHeight);

            UI.Variants.Set("mobile", true);
            // Variants.Changed → Screen.ReSolve → ApplyCommon → ApplyLayoutElement
            Assert.AreEqual(100f, le.preferredWidth, "mobile: width override = 100");
            Assert.AreEqual(0f, le.flexibleWidth);
            Assert.AreEqual(-1f, le.preferredHeight,
                "mobile has no height (size.mobile empty) — must reset to -1, not retain 64");
            Assert.AreEqual(-1f, le.flexibleHeight);
        }

        [Test]
        public void Btn_in_VStack_with_width_stretch_writes_flexible_one()
        {
            // width="stretch" inside V/HStack maps to LayoutElement.flexibleWidth=1
            // (with preferredWidth=0). The LayoutGroup then grows the child to fill the cross axis.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='380' height='180'>
    <Btn id='b' width='stretch' height='46'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "Btn with width=stretch must get a LayoutElement");
            Assert.AreEqual(0f, le.preferredWidth, "stretch: preferred width = 0");
            Assert.AreEqual(1f, le.flexibleWidth, "stretch: flexible width = 1 (LayoutGroup distributes remaining space)");
            Assert.AreEqual(46f, le.preferredHeight, "height numeric stays preferred");
            Assert.AreEqual(0f, le.flexibleHeight);
        }

        [Test]
        public void Btn_in_HStack_with_height_stretch_writes_flexible_one()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='200' height='200'>
    <Btn id='b' width='100' height='stretch'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.AreEqual(100f, le.preferredWidth);
            Assert.AreEqual(0f, le.flexibleWidth);
            Assert.AreEqual(0f, le.preferredHeight);
            Assert.AreEqual(1f, le.flexibleHeight);
        }

        [Test]
        public void Variant_switch_from_stretch_to_numeric_resets_flexible()
        {
            // mobile base 是 stretch；切到 desktop 写死 width=240 → flexibleWidth 必须从 1 回到 0。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='380' height='180'>
    <Btn id='b' width='stretch' width.desktop='240' height='46'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            UI.Variants.Set("desktop", false);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.AreEqual(0f, le.preferredWidth, "base: stretch");
            Assert.AreEqual(1f, le.flexibleWidth);

            UI.Variants.Set("desktop", true);
            Assert.AreEqual(240f, le.preferredWidth, "desktop override: numeric");
            Assert.AreEqual(0f, le.flexibleWidth, "flexibleWidth must reset to 0 when override is numeric");
        }

        [Test]
        public void Btn_in_VStack_with_stretch_weighted_two_writes_flexible_two()
        {
            // width="stretch*2" maps to LayoutElement.flexibleWidth=2 — so a 1:2:1 split
            // with two stretch-weight-1 siblings + this child yields exact 25/50/25.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b' width='stretch*2' height='46'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.AreEqual(0f, le.preferredWidth);
            Assert.AreEqual(2f, le.flexibleWidth, "weight 2 → flexibleWidth=2");
        }

        [Test]
        public void Btn_in_HStack_with_stretch_weighted_half_writes_flexible_half()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='200' height='200'>
    <Btn id='b' width='100' height='stretch*0.5'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var le = btn.GameObject.GetComponent<LayoutElement>();
            Assert.AreEqual(0.5f, le.flexibleHeight);
        }

        [Test]
        public void Stretch_under_Frame_throws()
        {
            // 'stretch' keyword is meaningless outside V/HStack — anchor.stretch is the right tool
            // for free-positioning containers. Reject loudly instead of silently doing nothing.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='frame' anchor='stretch' margin='0'>
    <Btn id='b' width='stretch' height='46'/>
  </Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            Assert.Throws<System.ArgumentException>(() => UI.Open("S"));
        }

        [Test]
        public void Stretch_under_Grid_throws()
        {
            // Grid uses cellSize and ignores LayoutElement; stretch on a Grid child is doubly meaningless.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Grid id='grid' columns='2' cellSize='40x40' width='200' height='200'>
    <Btn id='b' width='stretch'/>
  </Grid>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            Assert.Throws<System.ArgumentException>(() => UI.Open("S"));
        }

        [Test]
        public void LayoutElement_inside_VStack_skips_rect_anchored_and_size_writes()
        {
            // Author-supplied anchor/margin under a LayoutGroup is ignored by design (spec §6.5).
            // We also skip writing anchorMin/anchorMax/anchoredPosition/sizeDelta so the layout
            // pass owns geometry without contention.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='stack' width='200' height='200'>
    <Btn id='b' size='64x64'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            // ApplyCommon 不能把 size= 的值写到 sizeDelta（LayoutGroup 接管时只走 LayoutElement）。
            // 注意：RectTransform 默认 sizeDelta=(100,100)，所以这里反证"非 64x64"足够说明 size= 没被双写。
            Assert.AreNotEqual(new Vector2(64f, 64f), btn.RectTransform.sizeDelta,
                "ApplyCommon must not double-write user's size= value into sizeDelta when parent is a LayoutGroup");
        }
    }
}
