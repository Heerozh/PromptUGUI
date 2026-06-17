using NUnit.Framework;
using PromptUGUI.Application;
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

        // ---- Task 3: 组件 Snap() ----
        private static (GameObject root, TextMeshProUGUI tmp, PixelSnap snap) MakeText(
            Vector2 anchoredPos, bool pixelPerfect)
        {
            var go = new GameObject("c");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.scaleFactor = 3f;
            canvas.pixelPerfect = pixelPerfect;
            var t = new GameObject("txt");
            t.transform.SetParent(go.transform, false);
            var tmp = t.AddComponent<TextMeshProUGUI>();   // 默认 TopLeft 对齐
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(10f, 16f);
            rt.anchoredPosition = anchoredPos;
            var snap = t.AddComponent<PixelSnap>();
            Canvas.ForceUpdateCanvases();
            return (go, tmp, snap);
        }

        [Test]
        public void Snap_PixelPerfectOn_SnapsLeftTopReferenceToInteger()
        {
            var (root, tmp, snap) = MakeText(new Vector2(7.3f, 4.1f), pixelPerfect: true);
            snap.Snap();
            Canvas.ForceUpdateCanvases();
            // TopLeft → 参考点 (xMin, yMax) = (0,16) → 屏幕 (21.9, (4.1+16)*3=60.3) → (22,60)
            var after = RectTransformUtility.WorldToScreenPoint(
                null, tmp.rectTransform.TransformPoint((Vector3)tmp.rectTransform.rect.min
                       + Vector3.up * tmp.rectTransform.rect.height));
            Assert.AreEqual(22f, after.x, 0.01f);
            Assert.AreEqual(60f, after.y, 0.01f);
            Object.DestroyImmediate(root);
        }

        [Test]
        public void Snap_PixelPerfectOff_NoOp()
        {
            var (root, tmp, snap) = MakeText(new Vector2(7.3f, 4.1f), pixelPerfect: false);
            var before = tmp.rectTransform.position;
            snap.Snap();
            Assert.AreEqual(before, tmp.rectTransform.position);   // 门控：不动
            Object.DestroyImmediate(root);
        }

        // ---- Task 5: 动态子树挂载 ----
        [Test]
        public void BindItems_PixelMode_DynamicTextGetsPixelSnap()
        {
            UI.ResetForTests();
            UI.CanvasSizeOverride = () => new Vector2(5760f, 3240f); // factor 3
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Row'><Frame width='200' height='50'><Text id='label'>x</Text></Frame></Template>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <ScrollList id='list' itemTemplate='Row'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var list = screen.Get<PromptUGUI.Controls.ScrollList>("list");
            PromptUGUI.Controls.IControl captured = null;
            list.BindItems(
                R3.Observable.Return<System.Collections.Generic.IReadOnlyList<string>>(new[] { "a" }),
                (PromptUGUI.Controls.IControl slot, string s) => captured = slot);
            var label = captured.Get<PromptUGUI.Controls.Text>("label");
            Assert.IsNotNull(label.GameObject.GetComponent<PixelSnap>(),
                "动态实例化的文本也应挂 PixelSnap");
            UI.ResetForTests();
        }

        // ---- Task 4: Screen 静态挂载 ----
        private static PromptUGUI.Application.Screen OpenPixel(string body)
        {
            UI.CanvasSizeOverride = () => new Vector2(5760f, 3240f); // /1920x1080 → factor 3
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>" + body + @"</Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            return (PromptUGUI.Application.Screen)UI.Open("S");
        }

        [Test]
        public void Open_PixelMode_AttachesPixelSnapToText()
        {
            UI.ResetForTests();
            var screen = OpenPixel("<Text id='t'>hi</Text>");
            var tmp = screen.RootGameObject.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNotNull(tmp);
            Assert.IsNotNull(tmp.GetComponent<PixelSnap>(), "pixel 模式应挂 PixelSnap");
            UI.ResetForTests();
        }

        [Test]
        public void Open_AutoMode_DoesNotAttachPixelSnap()
        {
            UI.ResetForTests();
            UI.CanvasSizeOverride = () => new Vector2(1920f, 1080f);
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'><Text id='t'>hi</Text></Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = (PromptUGUI.Application.Screen)UI.Open("S");
            var tmp = screen.RootGameObject.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNull(tmp.GetComponent<PixelSnap>(), "auto 模式不应挂");
            UI.ResetForTests();
        }

        // ---- Task 8: ReSolve re-attaches (runtime scale-mode flip / Add-block activation) ----
        [Test]
        public void ReSolve_ScaleModeFlipsToPixel_AttachesPixelSnap()
        {
            UI.ResetForTests();
            UI.CanvasSizeOverride = () => new Vector2(5760f, 3240f); // factor 3 in pixel mode
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' scale-mode.portrait='pixel' reference='1920x1080'>
    <Text id='t'>hi</Text>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = (PromptUGUI.Application.Screen)UI.Open("S");
            var tmp = screen.RootGameObject.GetComponentInChildren<TMP_Text>(true);
            Assert.IsNull(tmp.GetComponent<PixelSnap>(), "auto 模式开屏时不应有 PixelSnap");

            UI.Variants.Set("portrait", true);   // scale-mode.portrait='pixel' → ReSolve, _isPixelMode=true
            Assert.IsNotNull(tmp.GetComponent<PixelSnap>(),
                "翻到 pixel 变体后 ReSolve 应补挂 PixelSnap");
            UI.ResetForTests();
        }

        // ---- Task 9: slow scroll must not detach text from scroll content ----
        [Test]
        public void Snap_SlowScrollInParent_TextFollowsScroll_NoDetach()
        {
            var go = new GameObject("c");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.scaleFactor = 3f;
            canvas.pixelPerfect = true;
            var content = new GameObject("content").AddComponent<RectTransform>();
            content.SetParent(go.transform, false);
            content.anchorMin = content.anchorMax = Vector2.zero;
            content.pivot = Vector2.zero;
            var t = new GameObject("txt");
            t.transform.SetParent(content, false);
            var tmp = t.AddComponent<TextMeshProUGUI>();
            var rt = tmp.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(10f, 16f);
            rt.anchoredPosition = new Vector2(5f, 5f);
            var snap = t.AddComponent<PixelSnap>();
            Canvas.ForceUpdateCanvases();

            snap.Snap();
            Canvas.ForceUpdateCanvases();
            var localStartY = rt.localPosition.y;
            var screenStartY = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(Vector3.zero)).y;

            // slow scroll: parent content moves +0.1 design units (=0.3 screen px @sf3) per frame, 30 frames = 9px
            for (int i = 0; i < 30; i++)
            {
                content.anchoredPosition += new Vector2(0f, 0.1f);
                Canvas.ForceUpdateCanvases();
                snap.Snap();
                Canvas.ForceUpdateCanvases();
            }

            var localEndY = rt.localPosition.y;
            var screenEndY = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(Vector3.zero)).y;
            // text must FOLLOW the scroll (~+9px), not stay pinned (~0); and its local offset within
            // content must NOT drift (no detach).
            Assert.AreEqual(9f, screenEndY - screenStartY, 1.5f, "text should follow scroll, not stay pinned");
            Assert.AreEqual(localStartY, localEndY, 1f, "text local position must not drift off the scroll content");
            Object.DestroyImmediate(go);
        }
    }
}
