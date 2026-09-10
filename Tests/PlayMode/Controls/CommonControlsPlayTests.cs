using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class CommonControlsPlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator Toggle_group_runtime_switching()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <VStack>
    <Toggle id='a' group='g' isOn='true'/>
    <Toggle id='b' group='g'/>
  </VStack>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var a = screen.Get<Toggle>("a");
            var b = screen.Get<Toggle>("b");

            yield return null;  // give Unity one frame to wire up ToggleGroup

            b.IsOn = true;
            yield return null;
            Assert.IsFalse(a.IsOn);
            Assert.IsTrue(b.IsOn);
        }

        [UnityTest]
        public IEnumerator ScrollList_renders_via_real_layout()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><HStack height='32'><Text id='label'/></HStack></Template>
  <Screen name='S'>
    <ScrollList id='list' anchor='center' size='400x300' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "alpha", "beta", "gamma" }),
                (IControl slot, string s) => slot.Get<Text>("label").TextValue = s);

            yield return null;
            yield return null;
            Assert.AreEqual(3, list.SlotCount);
        }

        // Grid mode + static children through the real play-mode layout pass. EditMode drives layout
        // by hand (Canvas.ForceUpdateCanvases); here the ContentSizeFitter on Content and the
        // GridLayoutGroup settle over real frames, which is where a wrong rebuild order would show.
        [UnityTest]
        public IEnumerator ScrollList_grid_lays_static_cards_out_in_rows()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Cell'><Frame/></Template>
  <Screen name='S'>
    <ScrollList id='list' anchor='center' size='200x300' itemTemplate='Cell'
                columns='4' cellSize='40x40' spacing='0' padding='0'>
      <Frame/><Frame/><Frame/><Frame/><Frame/><Frame/>
    </ScrollList>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<ScrollList>("list");
            var content = (UnityEngine.RectTransform)list.GameObject.transform.Find("Viewport/Content");

            yield return null;
            yield return null;

            Assert.AreEqual(6, list.SlotCount, "the six XML cards are the initial slots");
            Assert.AreEqual(80f, content.rect.height, 0.5f, "6 cards over 4 columns = 2 rows x 40");

            list.BindItems(
                Observable.Return<IReadOnlyList<int>>(new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12 }),
                (IControl _, int __) => { });

            yield return null;
            yield return null;

            Assert.AreEqual(12, list.SlotCount);
            Assert.AreEqual(120f, content.rect.height, 0.5f, "12 items over 4 columns = 3 rows");
        }

        // Issue 1 回归：动态绑定（BindOptions/BindItems）里的 UI.Tr 不会随 ReSolve 自动重译——
        // ReSolve 只重译 XML 声明的 text=，不重跑 C# 绑定。sample 改用 "UI.Locale.Changed → Observable"
        // 脉冲流（FromEvent + Prepend）让选项在切语言时重新计算。这里验证该脉冲流：订阅即发一次
        // （首帧选项就有值），切 locale 后再发一次（触发重译）。同时编译验证 sample 用到的 R3 API。
        [UnityTest]
        public IEnumerator Locale_change_re_emits_for_dynamic_retranslation()
        {
            var emissions = 0;
            var ticks = Observable.FromEvent(h => UI.Locale.Changed += h, h => UI.Locale.Changed -= h)
                                  .Prepend(Unit.Default);
            var sub = ticks.Subscribe(_ => emissions++);
            try
            {
                Assert.AreEqual(1, emissions, "Prepend：订阅即发一次（首帧选项就有值）");

                UI.Locale.Set("en");   // 异步加载 .po 后 fire UI.Locale.Changed
                yield return new UnityEngine.WaitForSecondsRealtime(0.3f);
                Assert.GreaterOrEqual(emissions, 2, "切 locale 后应重发，驱动 BindOptions/BindItems 重译");
            }
            finally { sub.Dispose(); }
        }
    }
}
