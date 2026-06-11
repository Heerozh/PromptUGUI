using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    // V/HStack 直下声明 scale 的 <Text> 在实例化期自动插 wrapper + 布局桥（spec STW-D8）。
    // 条件矩阵 + 动态子树（BindItems 与静态树共用 InstantiateRecursive，零特判）。
    public class ScaledTextWrapperTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen OpenScreen(string xml)
        {
            UI.LoadDocument("test", xml);
            return (PromptUGUI.Application.Screen)UI.Open("S");
        }

        private static Control GetControl(IScreen screen, string id)
            => (Control)screen.Get(id);

        [Test]
        public void Text_with_scale_in_VStack_gets_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' wrap='true' scale='0.5'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreNotEqual(c.RectTransform, c.LayoutHost, "wrapper expected");
            Assert.AreEqual("t [scale-host]", c.LayoutHost.gameObject.name);
            // 层级：VStack → wrapper → text
            Assert.IsNotNull(c.LayoutHost.parent
                .GetComponent<UnityEngine.UI.VerticalLayoutGroup>());
            Assert.AreEqual(c.LayoutHost, c.RectTransform.parent);
            Assert.IsNotNull(c.LayoutHost.GetComponent<ScaledTextLayoutBridge>());
        }

        [Test]
        public void Text_without_scale_gets_no_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreEqual(c.RectTransform, c.LayoutHost);
            Assert.IsNotNull(c.RectTransform.parent
                .GetComponent<UnityEngine.UI.VerticalLayoutGroup>());
        }

        [Test]
        public void Text_with_scale_in_Frame_gets_no_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame size='200x100'>
      <Text id='t' anchor='stretch' margin='0' scale='0.5'>hello</Text>
    </Frame>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreEqual(c.RectTransform, c.LayoutHost);
        }

        [Test]
        public void Text_with_scale_in_Grid_gets_no_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Grid anchor='top-left' size='300x100' columns='3' cellSize='100x100'>
      <Text id='t' scale='0.5'>hello</Text>
    </Grid>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreEqual(c.RectTransform, c.LayoutHost,
                "Grid is excluded (STW-D2) — cellSize is the declared box");
        }

        [Test]
        public void Text_with_variant_only_scale_gets_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' scale.mobile='0.5'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreNotEqual(c.RectTransform, c.LayoutHost,
                "variant 运行期才激活而 GO 永不重建 → 创建期必须备好 wrapper");
        }

        [Test]
        public void Btn_with_scale_in_VStack_gets_no_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Btn id='b' size='100x44' scale='0.5'>ok</Btn>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "b");
            Assert.AreEqual(c.RectTransform, c.LayoutHost, "non-Text 控件不在范围（spec §1.3）");
        }

        [Test]
        public void BindItems_text_root_card_gets_wrapper_and_slot_resolves()
        {
            // 模板根 = <Text scale>：ScrollList Content 是 VerticalLayoutGroup → 包装；
            // 同时回归 InstantiateNode 的 rootControl 查找（必须按 HostGameObject 匹配，
            // 否则 BindItems 拿不到 slot）。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'>
    <Text width='stretch' wrap='true' scale='0.5'>x</Text>
  </Template>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <ScrollList id='list' itemTemplate='Row' size='200x300'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");
            IControl captured = null;
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "a" }),
                (IControl slot, string s) => captured = slot);
            Assert.IsNotNull(captured, "BindItems should instantiate one slot");
            var c = (Control)captured;
            Assert.AreNotEqual(c.RectTransform, c.LayoutHost);
            Assert.AreEqual(0.5f, c.RectTransform.localScale.x, 1e-5f);
        }

        [Test]
        public void Inner_text_inflated_by_box_preserving_compensation()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' wrap='true' scale='0.5'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var rt = GetControl(screen, "t").RectTransform;
            // stretch 基线 span 1 → /0.5 = 2，关于中心放宽 → [-0.5, 1.5]，两轴同。
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1.5f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(-0.5f, rt.anchorMin.y, 1e-5f);
            Assert.AreEqual(1.5f, rt.anchorMax.y, 1e-5f);
            Assert.AreEqual(0f, rt.sizeDelta.x, 1e-4f);
            Assert.AreEqual(0f, rt.sizeDelta.y, 1e-4f);
        }

        [Test]
        public void Hidden_attr_deactivates_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' scale='0.5' hidden='true'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.IsFalse(c.LayoutHost.gameObject.activeSelf,
                "hidden 必须作用在 wrapper，否则空 wrapper 仍占行高");
            Assert.IsTrue(c.GameObject.activeSelf);
        }

        [Test]
        public void Variant_flip_resets_and_reapplies_idempotently()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' scale='0.5' scale.mobile=''>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var rt = GetControl(screen, "t").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);

            UI.Variants.Set("mobile", true);   // scale 清空 → 恒等 + stretch 基线
            Assert.AreEqual(1f, rt.localScale.x, 1e-6f);
            Assert.AreEqual(0f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1f, rt.anchorMax.x, 1e-5f);

            UI.Variants.Set("mobile", false);  // 回到 0.5 + 膨胀
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);

            // 显式双跑 ReSolve：补偿不得跨次累积（幂等）。
            screen.ReSolve();
            screen.ReSolve();
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1.5f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
        }

        [Test]
        public void Relative_scale_in_wrapper_recomputes_with_factor()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Text id='t' width='stretch' wrap='true' scale='0.5r'>hello</Text>
    </VStack>
  </Screen>
</PromptUGUI>");
            var rt = GetControl(screen, "t").RectTransform;
            // round(3×0.5)=2 → localScale 2/3；膨胀 span = 1/(2/3) = 1.5 → [-0.25, 1.25]。
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.25f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1.25f, rt.anchorMax.x, 1e-5f);

            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f); // factor 2
            screen.ReSolve();
            // round(2×0.5)=1 → localScale 0.5 → [-0.5, 1.5]。
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);
        }

        [Test]
        public void Text_with_scale_in_HStack_gets_wrapper()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <HStack anchor='top-stretch' height='40' margin='0,0,_,0'>
      <Text id='t' scale='0.5'>hello</Text>
    </HStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreNotEqual(c.RectTransform, c.LayoutHost,
                "HorizontalOrVerticalLayoutGroup 条件须同时覆盖 HStack");
        }

        [Test]
        public void Wrapper_keeps_sibling_order()
        {
            // [Frame, Text scale, Frame] —— wrapper 必须顶在 Text 原来的中间槽位
            //（ApplyAddBlock 的 SetSiblingIndex 流程依赖这个不变量）。
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='top-stretch' height='200' margin='0,0,_,0'>
      <Frame id='a' height='10'/>
      <Text id='t' width='stretch' scale='0.5'>hello</Text>
      <Frame id='b' height='10'/>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = GetControl(screen, "t");
            Assert.AreEqual(1, c.LayoutHost.GetSiblingIndex());
            Assert.AreEqual(0, GetControl(screen, "a").RectTransform.GetSiblingIndex());
            Assert.AreEqual(2, GetControl(screen, "b").RectTransform.GetSiblingIndex());
        }
    }
}
