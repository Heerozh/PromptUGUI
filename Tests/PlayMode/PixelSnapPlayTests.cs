using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode
{
    public class PixelSnapPlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator CenterAnchoredText_SnapsToIntegerDevicePixel_InPlayLoop()
        {
            UI.CanvasSizeOverride = () => new Vector2(5760f, 3240f); // factor 3
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Text id='t' anchor='center' width='100' height='20'>hi</Text>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var tmp = screen.Get<Text>("t").TmpComponent;
            var rt = tmp.rectTransform;

            // 强制一个分数设备位置
            rt.anchoredPosition = new Vector2(7.5f / 3f, 0f);
            yield return null;   // LateUpdate 跑 Snap
            yield return null;

            var localRef = PromptUGUI.Controls.Internal.PixelSnap.ReferencePoint(
                rt.rect, tmp.horizontalAlignment, tmp.verticalAlignment);
            var screenPt = RectTransformUtility.WorldToScreenPoint(
                null, rt.TransformPoint((Vector3)localRef));
            Assert.AreEqual(Mathf.Round(screenPt.x), screenPt.x, 0.02f, "x 应落整数设备像素");
            Assert.AreEqual(Mathf.Round(screenPt.y), screenPt.y, 0.02f, "y 应落整数设备像素");
        }

        [UnityTest]
        public IEnumerator Snap_DuringInertialScroll_TextStaysOnGrid()
        {
            var go = new GameObject("canvas");
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.scaleFactor = 3f;
            canvas.pixelPerfect = true;
            var canvasRT = (RectTransform)go.transform;
            canvasRT.sizeDelta = new Vector2(300f, 300f);

            var viewport = new GameObject("viewport").AddComponent<RectTransform>();
            viewport.SetParent(go.transform, false);
            viewport.anchorMin = Vector2.zero; viewport.anchorMax = Vector2.one;
            viewport.sizeDelta = Vector2.zero;
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();

            var content = new GameObject("content").AddComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f); content.anchorMax = new Vector2(0f, 1f);
            content.pivot = new Vector2(0f, 1f);
            content.sizeDelta = new Vector2(100f, 2000f);
            content.anchoredPosition = Vector2.zero;

            var t = new GameObject("txt"); t.transform.SetParent(content, false);
            var tmp = t.AddComponent<TextMeshProUGUI>();
            var rt = tmp.rectTransform;
            rt.anchorMin = new Vector2(0f, 1f); rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = new Vector2(80f, 20f);
            rt.anchoredPosition = new Vector2(5.3f, -5.3f); // fractional so snapping is non-trivial
            t.AddComponent<PromptUGUI.Controls.Internal.PixelSnap>();

            var sr = go.AddComponent<UnityEngine.UI.ScrollRect>();
            sr.viewport = viewport; sr.content = content;
            sr.horizontal = false; sr.vertical = true;
            sr.movementType = UnityEngine.UI.ScrollRect.MovementType.Unrestricted;
            sr.inertia = true;
            Canvas.ForceUpdateCanvases();
            yield return null;

            System.Func<float> contentY = () => content.anchoredPosition.y;
            System.Func<Vector2> refScreen = () =>
            {
                var localRef = PromptUGUI.Controls.Internal.PixelSnap.ReferencePoint(
                    rt.rect, tmp.horizontalAlignment, tmp.verticalAlignment);
                return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint((Vector3)localRef));
            };

            var y0 = contentY();
            sr.velocity = new Vector2(0f, 900f);   // launch inertial scroll
            bool moved = false;
            int offGridFrames = 0;
            for (int i = 0; i < 30; i++)
            {
                yield return null;                  // frame end: ScrollRect.LateUpdate moved, willRenderCanvases snapped
                if (Mathf.Abs(contentY() - y0) > 1f) moved = true;
                var p = refScreen();
                if (Mathf.Abs(p.x - Mathf.Round(p.x)) > 0.05f || Mathf.Abs(p.y - Mathf.Round(p.y)) > 0.05f)
                    offGridFrames++;
            }
            Assert.IsTrue(moved, "ScrollRect inertia should have moved the content (test not vacuous)");
            Assert.AreEqual(0, offGridFrames, "text reference must stay on the integer device-pixel grid during inertial scroll");
        }

        [UnityTest]
        public IEnumerator LayoutGroupChildText_SnapsAndStaysStable()
        {
            UI.CanvasSizeOverride = () => new Vector2(5760f, 3240f); // factor 3
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <VStack width='200' anchor='center'>
      <Text id='a' height='20'>aaa</Text>
      <Text id='b' height='20'>bbb</Text>
    </VStack>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var tmp = screen.Get<Text>("a").TmpComponent;
            var rt = tmp.rectTransform;
            yield return null; yield return null; yield return null; // layout + snap settle

            System.Func<Vector2> refScreen = () =>
            {
                var localRef = PromptUGUI.Controls.Internal.PixelSnap.ReferencePoint(
                    rt.rect, tmp.horizontalAlignment, tmp.verticalAlignment);
                return RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint((Vector3)localRef));
            };

            var p1 = refScreen();
            Assert.AreEqual(Mathf.Round(p1.x), p1.x, 0.02f, "layout-child text x should be pixel-snapped");
            Assert.AreEqual(Mathf.Round(p1.y), p1.y, 0.02f, "layout-child text y should be pixel-snapped");

            // PixelSnap writes localPosition every frame; it must NOT fight the LayoutGroup
            // (no oscillation / drift) — position stays put over several frames.
            for (int i = 0; i < 5; i++) yield return null;
            var p2 = refScreen();
            Assert.AreEqual(p1.x, p2.x, 0.5f, "layout-child text must stay stable (no fight/drift)");
            Assert.AreEqual(p1.y, p2.y, 0.5f, "layout-child text must stay stable (no fight/drift)");
        }
    }
}
