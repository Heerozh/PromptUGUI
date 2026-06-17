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

        // ---- Task 2: SnapToPixelGrid ----
        private static (GameObject root, RectTransform rt, Canvas canvas) MakeRT(
            Vector2 anchoredPos, Vector2 size)
        {
            var go = new GameObject("c");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.scaleFactor = 3f;
            var child = new GameObject("ch");
            child.transform.SetParent(go.transform, false);
            var rt = child.AddComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = size;
            rt.anchoredPosition = anchoredPos;
            Canvas.ForceUpdateCanvases();
            return (go, rt, canvas);
        }

        [Test]
        public void SnapToPixelGrid_FractionalScreenPos_SnapsToIntegerScreen()
        {
            var (root, rt, canvas) = MakeRT(new Vector2(7.3f, 4.1f), new Vector2(10f, 16f));
            PixelSnap.SnapToPixelGrid(rt, canvas, rt.rect.min);
            Canvas.ForceUpdateCanvases();
            var after = RectTransformUtility.WorldToScreenPoint(
                null, rt.TransformPoint((Vector3)rt.rect.min));
            Assert.AreEqual(22f, after.x, 0.01f);
            Assert.AreEqual(12f, after.y, 0.01f);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void SnapToPixelGrid_AlreadyAligned_Idempotent()
        {
            var (root, rt, canvas) = MakeRT(new Vector2(7.3f, 4.1f), new Vector2(10f, 16f));
            PixelSnap.SnapToPixelGrid(rt, canvas, rt.rect.min);
            Canvas.ForceUpdateCanvases();
            var pos1 = rt.position;
            PixelSnap.SnapToPixelGrid(rt, canvas, rt.rect.min);
            Assert.AreEqual(pos1.x, rt.position.x, 1e-4f);
            Assert.AreEqual(pos1.y, rt.position.y, 1e-4f);
            Object.DestroyImmediate(root);
        }
    }
}
