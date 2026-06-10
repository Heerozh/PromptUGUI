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
    }
}
