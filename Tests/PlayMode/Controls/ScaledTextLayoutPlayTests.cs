using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode
{
    // 真实布局 pass 下的验收（spec §1.2 语义 2/3）：行高 = TMP 换行高 × s；
    // 运行时改文本后行高跟随（bridge 的 TEXT_CHANGED 脏标传播）。
    public class ScaledTextLayoutPlayTests
    {
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack anchor='stretch'>
      <Text id='t' width='stretch' wrap='true' fontSize='24' scale='0.5'>hello world hello world hello world hello world hello world</Text>
    </VStack>
  </Screen>
</PromptUGUI>";

        [UnityTest]
        public IEnumerator Row_height_matches_scaled_preferred_and_grows_with_text()
        {
            UI.ResetForTests();
            UI.LoadDocument("test", Xml);
            var screen = UI.Open("S");
            var text = screen.Get<Text>("t");
            var control = (Control)(IControl)text;
            var wrapper = control.LayoutHost;
            var tmp = text.TmpComponent;

            // ScreenSpaceOverlay Canvas 首帧初始化后才有有效尺寸；yield 一帧后 ForceUpdateCanvases
            // 保证 VStack 已完成水平+垂直两轮 layout pass（Bridge 的 preferredHeight 写入 wrapper）。
            yield return null;
            Canvas.ForceUpdateCanvases();

            // 内层 rect 宽 = wrapper 宽 × 2（anchors 放宽 1/0.5）→ TMP 按整行宽换行。
            Assert.AreEqual(wrapper.rect.width * 2f, control.RectTransform.rect.width, 1f);
            // 行高 = TMP 换行后 preferredHeight × 0.5（容差 1px）。
            var rowHeight = wrapper.rect.height;
            Assert.AreEqual(tmp.preferredHeight * 0.5f, rowHeight, 1f);
            Assert.Greater(rowHeight, 1f);

            // 动态改文本 → TEXT_CHANGED → bridge 标脏 → 下一帧行高增长。
            text.TextValue = string.Concat(
                System.Linq.Enumerable.Repeat("hello world ", 40));
            yield return null;
            Canvas.ForceUpdateCanvases();
            Assert.Greater(wrapper.rect.height, rowHeight,
                "longer text must grow the row height");
            Assert.AreEqual(tmp.preferredHeight * 0.5f, wrapper.rect.height, 1f);
        }
    }
}
