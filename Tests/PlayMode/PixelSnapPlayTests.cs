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
