using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class PixelSnapTests
    {
        // ---- Task 1: ReferencePoint ----
        [Test]
        public void ReferencePoint_LeftTop_ReturnsMinXMaxY()
        {
            var p = PixelSnap.ReferencePoint(new Rect(0, 0, 10, 16),
                HorizontalAlignmentOptions.Left, VerticalAlignmentOptions.Top);
            Assert.AreEqual(new Vector2(0f, 16f), p);
        }

        [Test]
        public void ReferencePoint_CenterMiddle_ReturnsCenter()
        {
            var p = PixelSnap.ReferencePoint(new Rect(0, 0, 10, 16),
                HorizontalAlignmentOptions.Center, VerticalAlignmentOptions.Middle);
            Assert.AreEqual(new Vector2(5f, 8f), p);
        }

        [Test]
        public void ReferencePoint_RightBottom_ReturnsMaxXMinY()
        {
            var p = PixelSnap.ReferencePoint(new Rect(0, 0, 10, 16),
                HorizontalAlignmentOptions.Right, VerticalAlignmentOptions.Bottom);
            Assert.AreEqual(new Vector2(10f, 0f), p);
        }

        [Test]
        public void ReferencePoint_Justified_TreatedAsLeft()
        {
            var p = PixelSnap.ReferencePoint(new Rect(0, 0, 10, 16),
                HorizontalAlignmentOptions.Justified, VerticalAlignmentOptions.Top);
            Assert.AreEqual(0f, p.x);
        }
    }
}
