using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;

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
        public IEnumerator SafeArea_anchor_settles_after_one_frame()
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
            yield return null;

            var rt = sa.RectTransform;
            Assert.AreEqual(0f, rt.anchorMin.x, 0.001f);
            Assert.AreEqual(100f / 1920f, rt.anchorMin.y, 0.001f);
            Assert.AreEqual(1f, rt.anchorMax.x, 0.001f);
            Assert.AreEqual(1f, rt.anchorMax.y, 0.001f);
        }

        [UnityTest]
        public IEnumerator Tracker_polls_provider_changes()
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

            // 切换"设备":notch 跑到下方变成手势条。下一帧 Update poll 到 safeArea 变化 → 重算 anchor。
            SafeAreaTracker.SafeAreaOverride =
                () => new Rect(0f, 0f, 1080f, 1830f);
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
    <Add into='@root'>
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
                "tracker re-applies after reactivation via OnEnable");
        }
    }
}
