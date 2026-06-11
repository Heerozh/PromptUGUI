using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Tutorial;
using UnityEngine;

namespace PromptUGUI.Tests.Tutorial
{
    public class TutorialPlacementTests
    {
        private static readonly Rect Overlay = new(-960, -540, 1920, 1080);
        private static readonly Vector2 Bubble = new(300, 100);
        private const float Gap = 60f;   // 手指 + 间距占用

        [Test]
        public void Auto_TargetNearBottom_PlacesTop()
        {
            var target = new Rect(-50, -520, 100, 60);   // 贴近下边缘
            var r = TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Auto);
            Assert.AreEqual(Side.Top, r.Side);
            Assert.Greater(r.BubblePos.y, target.yMax);
        }

        [Test]
        public void Auto_TargetNearTop_PlacesBottom()
        {
            var target = new Rect(-50, 460, 100, 60);
            var r = TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Auto);
            Assert.AreEqual(Side.Bottom, r.Side);
            Assert.Less(r.BubblePos.y, target.yMin);
        }

        [Test]
        public void Auto_TargetNearRightEdge_NeverOverflows()
        {
            var target = new Rect(900, -30, 50, 60);   // 贴右缘,Right 放不下
            var r = TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Auto);
            Assert.AreNotEqual(Side.Right, r.Side);
            // 气泡完全在 overlay 内
            Assert.GreaterOrEqual(r.BubblePos.x - Bubble.x / 2, Overlay.xMin);
            Assert.LessOrEqual(r.BubblePos.x + Bubble.x / 2, Overlay.xMax);
        }

        [Test]
        public void ExplicitPlace_Respected()
        {
            var target = new Rect(-50, -30, 100, 60);
            var r = TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Left);
            Assert.AreEqual(Side.Left, r.Side);
            Assert.Less(r.BubblePos.x, target.xMin);
        }

        [Test]
        public void FingerAngle_PointsAtTarget()
        {
            var target = new Rect(-50, -30, 100, 60);
            // 手指默认素材朝上;气泡在上方 → 手指在气泡与目标之间、旋转 180° 朝下指目标
            Assert.AreEqual(180f, TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Top).FingerAngle);
            Assert.AreEqual(0f, TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Bottom).FingerAngle);
            Assert.AreEqual(90f, TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Right).FingerAngle);
            Assert.AreEqual(-90f, TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Left).FingerAngle);
        }

        [Test]
        public void FingerPos_BetweenBubbleAndTarget()
        {
            var target = new Rect(-50, -30, 100, 60);
            var r = TutorialPlacement.Choose(Overlay, target, Bubble, Gap, Side.Top);
            Assert.Greater(r.FingerPos.y, target.yMax);
            Assert.Less(r.FingerPos.y, r.BubblePos.y - Bubble.y / 2);
        }
    }
}
