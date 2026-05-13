using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.Controls
{
    public class SafeAreaTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown]
        public void TearDown()
        {
            SafeAreaTracker.SafeAreaOverride = null;
            SafeAreaTracker.ScreenSizeOverride = null;
            UI.ResetForTests();
        }

        [UnityTest]
        public IEnumerator Children_inherit_safe_area_rect_after_layout()
        {
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa'>
    <Frame id='inner' anchor='stretch'/>
  </SafeArea>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var sa = screen.Get<SafeArea>("sa");
            var inner = screen.Get<Frame>("sa/inner");

            // Force the canvas to drive a known parent rect so anchor fractions resolve to a checkable size.
            var canvasRt = (RectTransform)sa.RectTransform.parent;
            canvasRt.anchorMin = Vector2.zero;
            canvasRt.anchorMax = Vector2.one;
            canvasRt.offsetMin = Vector2.zero;
            canvasRt.offsetMax = Vector2.zero;
            canvasRt.sizeDelta = new Vector2(1080f, 1920f);

            LayoutRebuilder.ForceRebuildLayoutImmediate(canvasRt);
            yield return null;

            var innerRect = inner.RectTransform.rect;
            Assert.AreEqual(1080f, innerRect.width, 1f);
            Assert.AreEqual(1820f, innerRect.height, 1f);
        }

        [UnityTest]
        public IEnumerator Tracker_reapplies_when_provider_changes_via_dimensions_event()
        {
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var sa = screen.Get<SafeArea>("sa");
            var rt = sa.RectTransform;
            yield return null;

            Assert.AreEqual(100f / 1920f, rt.anchorMin.y, 0.001f);

            // Swap the simulated device: notch moves to bottom (gesture bar style).
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 0f, 1080f, 1830f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            // Trigger OnRectTransformDimensionsChange by mutating the parent canvas rect.
            var canvasRt = (RectTransform)rt.parent;
            canvasRt.sizeDelta = new Vector2(1080f, 1921f); // any change re-fires the magic method
            yield return null;
            canvasRt.sizeDelta = new Vector2(1080f, 1920f);
            yield return null;

            Assert.AreEqual(0f, rt.anchorMin.y, 0.001f, "new safe area starts at y=0");
            Assert.AreEqual(1830f / 1920f, rt.anchorMax.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator SafeArea_inside_variant_add_block_works_after_toggle()
        {
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 100f, 1080f, 1820f);
            SafeAreaTracker.ScreenSizeOverride =
                () => new Vector2(1080f, 1920f);

            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Variant when='mobile'>
    <Add>
      <SafeArea id='sa'/>
    </Add>
  </Variant>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            screen.Variants.Set("mobile", true);
            yield return null;

            var sa = screen.Get<SafeArea>("sa");
            Assert.IsNotNull(sa);
            Assert.AreEqual(100f / 1920f, sa.RectTransform.anchorMin.y, 0.001f);

            screen.Variants.Set("mobile", false);
            yield return null;
            Assert.IsFalse(sa.GameObject.activeSelf, "Add block goes inactive");

            screen.Variants.Set("mobile", true);
            yield return null;
            Assert.IsTrue(sa.GameObject.activeSelf);
            Assert.AreEqual(100f / 1920f, sa.RectTransform.anchorMin.y, 0.001f,
                "tracker re-applies after reactivation");
        }
    }
}
