using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using PFrame = PromptUGUI.Controls.Frame;
using PImage = PromptUGUI.Controls.Image;

namespace PromptUGUI.Tests.Modals
{
    // 内置模态除半透明遮罩(backdrop)以外的内容,都应包在 <SafeArea> 里:
    // 遮罩满屏出血,内容避开刘海 / 挖孔 / Home 指示条。SafeArea 的 RectTransform
    // 上挂着 SafeAreaTracker,以此作为「是否位于 SafeArea 内」的结构标记。
    public class ModalSafeAreaTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void AssertContentInSafeAreaButBackdropOutside(
            GameObject content, GameObject backdrop)
        {
            Assert.IsNotNull(content.GetComponentInParent<SafeAreaTracker>(true),
                "模态内容必须位于 <SafeArea> 内(祖先链上应能找到 SafeAreaTracker)");
            Assert.IsNull(backdrop.GetComponentInParent<SafeAreaTracker>(true),
                "半透明遮罩必须满屏出血,位于 SafeArea 之外(祖先链上不应有 SafeAreaTracker)");
        }

        [Test]
        public void MessageBox_content_inside_safe_area_backdrop_outside()
        {
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
            MessageBox.Open("hello");
            var s = UI.Modal.TopScreen;
            AssertContentInSafeAreaButBackdropOutside(
                s.Get<PImage>("dialog").GameObject,
                s.Get<PImage>("backdrop").GameObject);
        }

        [Test]
        public void InputBox_content_inside_safe_area_backdrop_outside()
        {
            InputBox.XmlSrc = "PromptUGUI/Modals/InputBox.ui";
            InputBox.Open("title");
            var s = UI.Modal.TopScreen;
            AssertContentInSafeAreaButBackdropOutside(
                s.Get<PImage>("dialog").GameObject,
                s.Get<PImage>("backdrop").GameObject);
        }

        [Test]
        public void MarkdownBox_content_inside_safe_area_backdrop_outside()
        {
            // MarkdownBox 的 dialog 是 anchor="stretch" 的大面板 + Tiled 9-slice 皮肤
            // (pugui_9slice_round, 中心格 14px)。EditMode 测试画布是退化的 1920×4155
            // (横屏 reference 套在竖屏 Game view、portrait 变体未激活),把 dialog 拉到
            // ~960×3400 → Tiled 网格超出 Unity 贴片预算 → 良性的 "Too many sprite tiles"
            // 报错(真机竖屏 ~920×1440 远低于预算,无影响)。包进 <SafeArea> 后该网格会在
            // 测试里真正生成,故容忍这条环境相关日志(同 AddressableSpriteResolverTests 的做法)。
            UnityEngine.TestTools.LogAssert.ignoreFailingMessages = true;
            MarkdownBox.XmlSrc = "PromptUGUI/Modals/MarkdownBox.ui";
            MarkdownBox.Open("body");
            var s = UI.Modal.TopScreen;
            AssertContentInSafeAreaButBackdropOutside(
                s.Get<PImage>("dialog").GameObject,
                s.Get<PImage>("backdrop").GameObject);
        }

        [Test]
        public void CenteredSlideBox_content_inside_safe_area_backdrop_outside()
        {
            CenteredSlideBox.XmlSrc = "PromptUGUI/Modals/CenteredSlideBox.ui";
            CenteredSlideBox.Open(new[] { "1", "2", "3" }, (card, scr) => { });
            var s = UI.Modal.TopScreen;
            AssertContentInSafeAreaButBackdropOutside(
                s.Get<PFrame>("panel").GameObject,
                s.Get<PImage>("backdrop").GameObject);
        }

        [Test]
        public void Loading_content_inside_safe_area_backdrop_outside()
        {
            Loading.XmlSrc = "PromptUGUI/Modals/Loading.ui";
            var handle = Loading.Open("loading");
            var s = System.Linq.Enumerable.First(LoadingOverlay.ActiveScreens);
            AssertContentInSafeAreaButBackdropOutside(
                s.Get<PFrame>("dialog").GameObject,
                s.Get<PImage>("backdrop").GameObject);
            handle.Close();
        }
    }
}
