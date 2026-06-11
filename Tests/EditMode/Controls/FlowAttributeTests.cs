using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// flow="false"：layout-group（V/HStack/Grid）子节点退出排版流 —— 挂
    /// LayoutElement.ignoreLayout=true 让 LayoutGroup 跳过它，anchor / margin / size
    /// 恢复自由定位语义。典型用途：Stack 里铺满的 9-slice 背景层、角标、装饰 overlay。
    /// </summary>
    public class FlowAttributeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Flow_false_child_of_HStack_gets_ignoreLayout_and_stretch_anchors()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='200' height='18'>
    <Image id='bg' anchor='stretch' flow='false'/>
    <Btn id='b' size='64x16'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var bg = screen.Get<PromptUGUI.Controls.Image>("bg");
            var le = bg.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le, "flow=false child must get a LayoutElement (ignoreLayout carrier)");
            Assert.IsTrue(le.ignoreLayout, "flow=false → LayoutElement.ignoreLayout=true");
            Assert.AreEqual(Vector2.zero, bg.RectTransform.anchorMin,
                "flow=false: anchor='stretch' applies (free-positioning path)");
            Assert.AreEqual(Vector2.one, bg.RectTransform.anchorMax);
            Assert.AreEqual(Vector2.zero, bg.RectTransform.sizeDelta);
        }

        [Test]
        public void Flow_false_child_margin_applies_offsets()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='200' height='100'>
    <Image id='bg' anchor='stretch' margin='10,10,10,10' flow='false'/>
    <Btn id='b' size='64x16'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var bg = screen.Get<PromptUGUI.Controls.Image>("bg");
            Assert.AreEqual(new Vector2(-20f, -20f), bg.RectTransform.sizeDelta,
                "flow=false: margin drives sizeDelta like any free-positioned stretch child");
        }

        [Test]
        public void Flow_false_child_excluded_from_HStack_preferred_width()
        {
            // 核心需求：背景层不得撑大 Stack 的 hug 宽度。bg 若在流内，其 500 宽
            // 会进 preferred 总和；ignoreLayout 后 preferred 只看 Btn 的 64。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack id='v' width='300' height='100'>
    <HStack id='stack' height='18'>
      <Image id='bg' width='500' height='18' flow='false'/>
      <Btn id='b' size='64x16'/>
    </HStack>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var stack = screen.Get<HStack>("stack");
            var lg = stack.GameObject.GetComponent<HorizontalLayoutGroup>();
            lg.CalculateLayoutInputHorizontal();
            Assert.AreEqual(64f, lg.preferredWidth,
                "flow=false child must not contribute to the layout group's preferred width");
        }

        [Test]
        public void Flow_false_allows_fractional_width()
        {
            // '%' 在流内是硬错误（flex 权重无法表达父百分比）；流外恢复自由定位语义 → 合法。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='200' height='100'>
    <Image id='bg' width='50%' height='10' flow='false'/>
    <Btn id='b' size='64x16'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var bg = screen.Get<PromptUGUI.Controls.Image>("bg");
            Assert.AreEqual(0f, bg.RectTransform.anchorMin.x, 1e-4f);
            Assert.AreEqual(0.5f, bg.RectTransform.anchorMax.x, 1e-4f,
                "flow=false: width='50%' maps to anchor sub-range like under a Frame");
        }

        [Test]
        public void Flow_false_with_width_stretch_throws()
        {
            // 流外没有 flex 权重概念，'stretch' 关键字无意义 —— 与 Frame 子节点同样响亮报错。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='200' height='100'>
    <Image id='bg' width='stretch' flow='false'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            Assert.Throws<ParseException>(() => UI.Open("S"));
        }

        [Test]
        public void Flow_variant_toggle_flips_ignoreLayout_both_ways()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='200' height='100'>
    <Image id='bg' anchor='stretch' flow.deco='false'/>
    <Btn id='b' size='64x16'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            UI.Variants.Set("deco", false);
            var screen = UI.Open("S");
            var bg = screen.Get<PromptUGUI.Controls.Image>("bg");
            var le0 = bg.GameObject.GetComponent<LayoutElement>();
            Assert.IsTrue(le0 == null || !le0.ignoreLayout, "base: in flow");

            UI.Variants.Set("deco", true);
            var le = bg.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            Assert.IsTrue(le.ignoreLayout, "variant deco: out of flow");
            Assert.AreEqual(Vector2.zero, bg.RectTransform.anchorMin);
            Assert.AreEqual(Vector2.one, bg.RectTransform.anchorMax);

            UI.Variants.Set("deco", false);
            Assert.IsFalse(le.ignoreLayout,
                "back to base: ignoreLayout must reset to false (ReSolve idempotency)");
        }

        [Test]
        public void Flow_false_under_Grid_gets_ignoreLayout_and_free_anchors()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Grid id='grid' columns='2' cellSize='40x40' width='200' height='200'>
    <Image id='bg' anchor='stretch' flow='false'/>
    <Btn id='b'/>
  </Grid>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var bg = screen.Get<PromptUGUI.Controls.Image>("bg");
            var le = bg.GameObject.GetComponent<LayoutElement>();
            Assert.IsNotNull(le);
            Assert.IsTrue(le.ignoreLayout, "GridLayoutGroup also honors ignoreLayout");
            Assert.AreEqual(Vector2.zero, bg.RectTransform.anchorMin);
            Assert.AreEqual(Vector2.one, bg.RectTransform.anchorMax);
        }

        [Test]
        public void Scaled_text_with_flow_false_skips_layout_bridge_wrapper()
        {
            // STW-D8 wrapper 只为"被 LayoutGroup 量算"服务；流外的 Text 不被量算，
            // wrapper 反而会变成一个没人定位的中间层 —— 必须跳过。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <HStack id='stack' width='200' height='40'>
    <Text id='t' scale='2x' flow='false' anchor='top-left' width='50' height='20' tr='false'>hi</Text>
    <Btn id='b' size='64x16'/>
  </HStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var t = screen.Get<PromptUGUI.Controls.Text>("t");
            var stack = screen.Get<HStack>("stack");
            Assert.AreSame(stack.GameObject.transform, t.GameObject.transform.parent,
                "flow=false <Text scale> must NOT get a [scale-host] wrapper");
        }
    }
}
