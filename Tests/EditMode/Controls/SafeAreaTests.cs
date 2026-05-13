using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class SafeAreaTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void SafeArea_parses_and_instantiates()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var sa = screen.Get<SafeArea>("sa");
            Assert.IsNotNull(sa);
            Assert.IsNotNull(sa.GameObject);
            Assert.IsNotNull(sa.RectTransform);
        }

        [Test]
        public void SafeArea_attaches_tracker_on_instantiation()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var sa = screen.Get<SafeArea>("sa");
            var tracker = sa.GameObject.GetComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
            Assert.IsNotNull(tracker, "SafeArea.OnAttached should add SafeAreaTracker");
        }

        [Test]
        public void Tracker_applies_safe_area_fractions()
        {
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.Apply();

                var rt = (UnityEngine.RectTransform)go.transform;
                Assert.AreEqual(0f, rt.anchorMin.x, 0.001f);
                Assert.AreEqual(100f / 1920f, rt.anchorMin.y, 0.001f);
                Assert.AreEqual(1f, rt.anchorMax.x, 0.001f);
                Assert.AreEqual(1f, rt.anchorMax.y, 0.001f);
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMin);
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMax);

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
            }
        }

        [Test]
        public void Tracker_full_screen_safe_area_yields_identity_anchors()
        {
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 0f, 1080f, 1920f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.Apply();

                var rt = (UnityEngine.RectTransform)go.transform;
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin);
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
            }
        }

        [Test]
        public void SafeArea_anchor_persists_after_ReSolve()
        {
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);

                const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa'/>
</Screen></PromptUGUI>";
                UI.LoadDocument("test", xml);
                var screen = UI.Open("S");
                var sa = screen.Get<SafeArea>("sa");

                // ReSolve clobbers anchorMin/Max via ApplyCommon (defaults to top-left).
                // OnAfterApply must restore the safe-area fractions in the same call.
                screen.ReSolve();

                var rt = sa.RectTransform;
                Assert.AreEqual(0f, rt.anchorMin.x, 0.001f);
                Assert.AreEqual(100f / 1920f, rt.anchorMin.y, 0.001f,
                    "anchorMin.y should equal safeArea.y / Screen.height after ReSolve");
                Assert.AreEqual(1f, rt.anchorMax.x, 0.001f);
                Assert.AreEqual(1f, rt.anchorMax.y, 0.001f);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
            }
        }

        [Test]
        public void Tracker_apply_is_idempotent_no_writes_when_already_at_target()
        {
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.Apply();

                var rt = (UnityEngine.RectTransform)go.transform;
                // Inspector hook: hasChanged 反映 RectTransform 自上次置 false 起是否被写过
                rt.hasChanged = false;
                tracker.Apply();
                Assert.IsFalse(rt.hasChanged,
                    "Apply must be a no-op when the RectTransform is already at the target; " +
                    "otherwise OnRectTransformDimensionsChange → Apply 会形成回环");

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
            }
        }

        [Test]
        public void Tracker_zero_screen_size_is_noop()
        {
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 0f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => UnityEngine.Vector2.zero;

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var rt = (UnityEngine.RectTransform)go.transform;
                rt.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                rt.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);

                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.Apply();

                // Zero screen size → tracker bails; anchors unchanged.
                Assert.AreEqual(new UnityEngine.Vector2(0.5f, 0.5f), rt.anchorMin);
                Assert.AreEqual(new UnityEngine.Vector2(0.5f, 0.5f), rt.anchorMax);

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
            }
        }
    }
}
