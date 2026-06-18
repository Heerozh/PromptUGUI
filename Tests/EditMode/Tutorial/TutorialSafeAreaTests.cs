using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Tutorial;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.Tutorial
{
    // 引导气泡须避开设备安全区(notch / 挖孔 / Home 条):TutorialOverlayView 把全屏 overlay
    // 矩形按安全区内缩后再交给 TutorialPlacement 夹紧。遮罩仍满屏,只气泡/手指受此约束。
    // 用 SafeAreaTracker 的测试注入钩子模拟设备 inset(同 SafeAreaTests 的做法)。
    public class TutorialSafeAreaTests
    {
        // 竖屏全屏 overlay(中心原点),1080×1920。
        private static readonly Rect Full = new(-540f, -960f, 1080f, 1920f);
        private static readonly Vector2 Bubble = new(300f, 100f);
        private const float Gap = 60f;

        [TearDown]
        public void TearDown()
        {
            SafeAreaTracker.SafeAreaOverride = null;
            SafeAreaTracker.ScreenSizeOverride = null;
            SafeAreaTracker.ScaleFactorOverride = null;
        }

        [Test]
        public void NoInset_ReturnsFullRectUnchanged()
        {
            SafeAreaTracker.SafeAreaOverride = () => new Rect(0f, 0f, 1080f, 1920f);
            SafeAreaTracker.ScreenSizeOverride = () => new Vector2(1080f, 1920f);
            SafeAreaTracker.ScaleFactorOverride = () => 1f;

            var safe = TutorialOverlayView.ApplySafeInset(Full, 1f);
            Assert.AreEqual(Full.xMin, safe.xMin, 1e-3f);
            Assert.AreEqual(Full.xMax, safe.xMax, 1e-3f);
            Assert.AreEqual(Full.yMin, safe.yMin, 1e-3f);
            Assert.AreEqual(Full.yMax, safe.yMax, 1e-3f);
        }

        [Test]
        public void TopNotch_InsetsTopEdgeOnly()
        {
            // 顶部 134px notch:safeArea 原点在左下、y 向上 → safeArea.yMax = 1920-134 = 1786。
            SafeAreaTracker.SafeAreaOverride = () => new Rect(0f, 0f, 1080f, 1786f);
            SafeAreaTracker.ScreenSizeOverride = () => new Vector2(1080f, 1920f);
            SafeAreaTracker.ScaleFactorOverride = () => 1f;

            var safe = TutorialOverlayView.ApplySafeInset(Full, 1f);
            Assert.AreEqual(Full.yMax - 134f, safe.yMax, 1e-3f, "顶边应内缩 134(避开 notch)");
            Assert.AreEqual(Full.yMin, safe.yMin, 1e-3f, "底边不变");
            Assert.AreEqual(Full.xMin, safe.xMin, 1e-3f, "左右不变");
            Assert.AreEqual(Full.xMax, safe.xMax, 1e-3f, "左右不变");
        }

        [Test]
        public void HiDPI_ConvertsDeviceInsetToDesignPxViaScaleFactor()
        {
            // 设备 2160×3840、顶部 268 设备 px notch、scaleFactor=2 → 设计 px 顶边内缩 134。
            SafeAreaTracker.SafeAreaOverride = () => new Rect(0f, 0f, 2160f, 3840f - 268f);
            SafeAreaTracker.ScreenSizeOverride = () => new Vector2(2160f, 3840f);
            SafeAreaTracker.ScaleFactorOverride = () => 2f;

            var safe = TutorialOverlayView.ApplySafeInset(Full, 2f);
            Assert.AreEqual(Full.yMax - 134f, safe.yMax, 1e-3f, "268 设备 px / sf 2 = 134 设计 px");
        }

        [Test]
        public void TopNotch_BubbleStaysBelowNotch()
        {
            // 复刻"指向顶部控件、气泡放上方"的场景:不内缩时气泡会顶到屏幕上沿(notch 底下),
            // 内缩后气泡上沿不得越过安全区顶边。
            SafeAreaTracker.SafeAreaOverride = () => new Rect(0f, 0f, 1080f, 1786f);   // 顶部 inset 134
            SafeAreaTracker.ScreenSizeOverride = () => new Vector2(1080f, 1920f);
            SafeAreaTracker.ScaleFactorOverride = () => 1f;

            var safeOverlay = TutorialOverlayView.ApplySafeInset(Full, 1f);
            var target = new Rect(-50f, 900f, 100f, 60f);   // 贴近顶部
            var r = TutorialPlacement.Choose(safeOverlay, target, Bubble, Gap, Side.Top);

            float bubbleTop = r.BubblePos.y + Bubble.y / 2f;
            Assert.LessOrEqual(bubbleTop, Full.yMax - 134f + 1e-3f,
                "气泡上沿须落在安全区顶边或其下方(避开 notch)");
        }
    }
}
