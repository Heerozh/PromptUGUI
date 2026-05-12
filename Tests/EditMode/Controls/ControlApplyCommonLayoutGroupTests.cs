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
