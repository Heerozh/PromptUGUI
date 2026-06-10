using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Toasts;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.Toast
{
    public class ToastOverlayTests
    {
        private const string ToastXml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='test/Toast'>
    <Text id='text' anchor='center' align='center' fontSize='40' color='white'/>
  </Screen>
</PromptUGUI>";

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            UI.SourceResolver = src => AwaitableHelpers.Completed(src == "test/Toast" ? ToastXml : null);
            UI.Toast.XmlSrc = "test/Toast";
            UI.Toast.DefaultPosition = ToastPosition.Bottom;
            UI.Toast.DefaultStackMode = ToastStackMode.Stacked;
            UI.Toast.MaxVisible = 5;
        }

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [Test]
        public void Stacked_shows_immediately()
        {
            UI.Toast.Show("hi");
            Assert.AreEqual(1, ToastOverlay.ActiveCount);
            Assert.AreEqual(0, ToastOverlay.QueuedCount);
        }

        [Test]
        public void Text_written_into_text_node()
        {
            UI.Toast.Show("已保存");
            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            Assert.AreEqual("已保存", screen.Get<Text>("text").TmpComponent.text);
        }

        [Test]
        public void Toast_does_not_block_raycasts()
        {
            UI.Toast.Show("x");
            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            var cg = screen.RootGameObject.GetComponent<CanvasGroup>();
            Assert.IsNotNull(cg);
            Assert.IsFalse(cg.blocksRaycasts);
            Assert.IsFalse(cg.interactable);
        }

        [Test]
        public void SortingOrder_above_modal_band()
        {
            UI.Toast.SortingOrder = 2000;
            UI.Toast.Show("x");
            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            Assert.AreEqual(2000, screen.RootGameObject.GetComponent<Canvas>().sortingOrder);
        }

        [Test]
        public void Bottom_position_applied_to_text_node()
        {
            UI.Toast.EdgeInset = 120f;
            UI.Toast.Show("x", ToastPosition.Bottom);
            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            var rt = screen.Get<Text>("text").RectTransform;
            Assert.AreEqual(new Vector2(0.5f, 0f), rt.anchorMin);
            Assert.AreEqual(new Vector2(0.5f, 0f), rt.pivot);
            // newest 落基准：anchoredPosition.y == EdgeInset
            Assert.AreEqual(120f, rt.anchoredPosition.y, 0.5f);
        }

        [Test]
        public void Sequential_waits_then_promotes()
        {
            UI.Toast.Show("a", mode: ToastStackMode.Sequential);
            UI.Toast.Show("b", mode: ToastStackMode.Sequential);
            Assert.AreEqual(1, ToastOverlay.ActiveCount);   // 仅 a 显示
            Assert.AreEqual(1, ToastOverlay.QueuedCount);   // b 等待

            Assert.IsTrue(ToastOverlay.CompleteOldestForTests());   // 模拟 a 结束
            Assert.AreEqual(1, ToastOverlay.ActiveCount);   // b 提升
            Assert.AreEqual(0, ToastOverlay.QueuedCount);
        }

        [Test]
        public void Stacked_two_coexist_and_older_pushed_up()
        {
            UI.Toast.Show("first", ToastPosition.Bottom);
            UI.Toast.Show("second", ToastPosition.Bottom);
            Assert.AreEqual(2, ToastOverlay.ActiveCount);

            // 读"分配到的目标位"（reflow 即定，不依赖 Update）：older 被顶得更高、newer 落基准。
            var screens = System.Linq.Enumerable.ToList(ToastOverlay.ActiveScreens);
            var firstTarget = screens[0].RootGameObject.GetComponent<ToastView>().CurrentTarget;
            var secondTarget = screens[1].RootGameObject.GetComponent<ToastView>().CurrentTarget;
            Assert.Greater(firstTarget.y, secondTarget.y, "先来的(older)目标位更高");
            Assert.AreEqual(UI.Toast.EdgeInset, secondTarget.y, 0.5f, "最新一条落基准 EdgeInset");
        }

        [Test]
        public void MaxVisible_evicts_oldest()
        {
            UI.Toast.MaxVisible = 2;
            UI.Toast.Show("1", ToastPosition.Bottom);
            UI.Toast.Show("2", ToastPosition.Bottom);
            UI.Toast.Show("3", ToastPosition.Bottom);   // 触发挤最老
            // 最老那条进入 FadeOut（IsEvicting）；EditMode 不 tick，故仍在 _live，但已标记淡出
            Assert.IsTrue(ToastOverlay.OldestIsEvictingForTests());
        }

        [Test]
        public void Default_mode_resolves_to_DefaultStackMode()
        {
            UI.Toast.DefaultStackMode = ToastStackMode.Sequential;
            UI.Toast.Show("a");          // mode 缺省 → Default → Sequential
            UI.Toast.Show("b");          // 第二条应排队
            Assert.AreEqual(1, ToastOverlay.ActiveCount);
            Assert.AreEqual(1, ToastOverlay.QueuedCount);
        }

        [Test]
        public void Path_overload_falls_back_to_default_on_miss()
        {
            // "Nope/x" 解析不到 → 退回 DefaultPosition(Bottom)，仍显示，不抛
            Assert.DoesNotThrow(() => UI.Toast.Show("x", "Nope/x"));
            Assert.AreEqual(1, ToastOverlay.ActiveCount);
        }

        [Test]
        public void Color_param_overrides_text_node_color()
        {
            UI.Toast.Show("出错了", color: "red");
            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            Assert.AreEqual(UnityEngine.Color.red, screen.Get<Text>("text").TmpComponent.color);
        }

        [Test]
        public void No_color_keeps_xml_default()
        {
            UI.Toast.Show("plain");   // 不传 color → 保留模板里的 white
            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            Assert.AreEqual(UnityEngine.Color.white, screen.Get<Text>("text").TmpComponent.color);
        }

        [Test]
        public void Configure_runs_after_color_and_can_override()
        {
            // color 是 configure 之前应用的语法糖 → configure 仍能最后覆盖
            UI.Toast.Show("x", color: "red",
                configure: s => s.Get<Text>("text").Color = "#00ff00");
            var screen = System.Linq.Enumerable.First(ToastOverlay.ActiveScreens);
            Assert.AreEqual(UnityEngine.Color.green, screen.Get<Text>("text").TmpComponent.color);
        }
    }
}
