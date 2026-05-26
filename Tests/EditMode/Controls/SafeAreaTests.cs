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
        public void SafeArea_defaults_to_stretch_filling_parent_rect()
        {
            // SafeArea 拒绝 anchor 属性,但默认必须是 stretch — 否则 Control 基类
            // 默认 top-left + sizeDelta=(0,0) 会让 RectTransform 宽高=0,
            // direct 子的 stretch 直接子也跟着是 0(没东西可吸收 inset)。
            //
            // v2 适配:tracker 会主动用 Screen.safeArea 写 offsetMin/Max,Editor
            // Game View 的 safeArea 不保证等于全屏(host Unity 上实测有 top inset)。
            // 注入 full-screen override 让 device insets 都为 0,这样 sizeDelta
            // 才稳定地等于 (0,0),专心守 GetDefaultAnchor 这个 Inspector bug。
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 0f, 1080f, 1920f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride =
                    () => 1f;

                const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa'/>
</Screen></PromptUGUI>";
                UI.LoadDocument("test", xml);
                var screen = UI.Open("S");
                var rt = screen.Get<SafeArea>("sa").RectTransform;
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin, "SafeArea anchorMin should be (0,0)");
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax, "SafeArea anchorMax should be (1,1)");
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.sizeDelta, "SafeArea sizeDelta should be (0,0) → fills parent");
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
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
        public void Tracker_writes_max_blended_offsets_with_no_design_margin()
        {
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride =
                    () => 1f;

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.Apply();  // no CaptureDesignMargin call → _hasDesignMargin=false → design margins = 0

                var rt = (UnityEngine.RectTransform)go.transform;
                // v2 representation: anchor = stretch, offsets = device insets (design px since sf=1).
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin);
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
                // safe (0, 100, 1080, 1820), screen (1080, 1920) →
                //   insetL=0, insetR=0, insetB=100, insetT=0
                Assert.AreEqual(0f, rt.offsetMin.x, 0.001f);
                Assert.AreEqual(100f, rt.offsetMin.y, 0.001f);
                Assert.AreEqual(0f, rt.offsetMax.x, 0.001f);
                Assert.AreEqual(0f, rt.offsetMax.y, 0.001f);

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }

        [Test]
        public void Tracker_full_screen_safe_area_yields_zero_offsets()
        {
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 0f, 1080f, 1920f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride =
                    () => 1f;

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.Apply();

                var rt = (UnityEngine.RectTransform)go.transform;
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin);
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMin);
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.offsetMax);

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }

        [Test]
        public void SafeArea_offsets_persist_after_ReSolve()
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

                // ReSolve re-runs ApplyCommon → OnAfterApply → CaptureDesignMargin + tracker.Apply.
                // Result must still encode the inset on the bottom edge (insetB=100, design px),
                // not collapse back to zero offsets.
                screen.ReSolve();

                var rt = sa.RectTransform;
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin,
                    "v2 SafeArea anchor is always (0,0)/(1,1) stretch");
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
                // Canvas scaleFactor depends on host project's CanvasScaler config; we assert
                // the bottom offset is positive (inset absorbed) rather than a specific number.
                Assert.Greater(rt.offsetMin.y, 0f,
                    "bottom inset (100 device px) must absorb into offsetMin.y after ReSolve");
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
            }
        }

        [Test]
        public void Tracker_does_not_subscribe_to_rect_transform_dimensions_change()
        {
            // 守门测试：SafeAreaTracker 上不能存在 OnRectTransformDimensionsChange
            // magic method。一旦订阅，Unity 在 RectTransform setter 内部反向求解的
            // 中间态会反过来触发 tracker.Apply，跟 ApplyCommon 形成写入回环（实测
            // 卡在 var screen = UI.Open(...) 的 InstantiateRecursive 阶段，offsetMax
            // 在 0 / 0.65 间反复跳）。Unity 官方 SafeArea 示例同样用 Update poll，
            // 不订阅这个 magic method。
            var method = typeof(PromptUGUI.Controls.Internal.SafeAreaTracker)
                .GetMethod("OnRectTransformDimensionsChange",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic
                    | System.Reflection.BindingFlags.Public);
            Assert.IsNull(method,
                "SafeAreaTracker must not implement OnRectTransformDimensionsChange — " +
                "it forms a write loop with ApplyCommon. Use Update() polling instead.");
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
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride =
                    () => 1f;

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var rt = (UnityEngine.RectTransform)go.transform;
                rt.anchorMin = new UnityEngine.Vector2(0.5f, 0.5f);
                rt.anchorMax = new UnityEngine.Vector2(0.5f, 0.5f);

                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.Apply();

                // Zero screen size → tracker bails; anchors unchanged (still 0.5,0.5).
                Assert.AreEqual(new UnityEngine.Vector2(0.5f, 0.5f), rt.anchorMin);
                Assert.AreEqual(new UnityEngine.Vector2(0.5f, 0.5f), rt.anchorMax);

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }

        // Helper: pre-stage RectTransform offsets as if ApplyCommon wrote them for a given margin,
        // then capture + apply. Saves repetition in the parametric cases below.
        private static (UnityEngine.Vector2 offsetMin, UnityEngine.Vector2 offsetMax) RunTrackerWith(
            UnityEngine.Rect safe, UnityEngine.Vector2 screen, float scaleFactor,
            float marginTop, float marginRight, float marginBottom, float marginLeft)
        {
            PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = () => safe;
            PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = () => screen;
            PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = () => scaleFactor;

            var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
            var rt = (UnityEngine.RectTransform)go.transform;
            rt.anchorMin = UnityEngine.Vector2.zero;
            rt.anchorMax = UnityEngine.Vector2.one;
            // ApplyCommon convention: offsetMin = (l, b), offsetMax = (-r, -t).
            rt.offsetMin = new UnityEngine.Vector2(marginLeft, marginBottom);
            rt.offsetMax = new UnityEngine.Vector2(-marginRight, -marginTop);

            var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
            tracker.CaptureDesignMargin(rt);
            tracker.Apply();

            var result = (rt.offsetMin, rt.offsetMax);
            UnityEngine.Object.DestroyImmediate(go);
            return result;
        }

        [Test]
        public void Tracker_PC_with_margin_6_writes_margin_directly()
        {
            try
            {
                var (oMin, oMax) = RunTrackerWith(
                    safe: new UnityEngine.Rect(0f, 0f, 1920f, 1080f),
                    screen: new UnityEngine.Vector2(1920f, 1080f),
                    scaleFactor: 1f,
                    marginTop: 6f, marginRight: 6f, marginBottom: 6f, marginLeft: 6f);
                Assert.AreEqual(new UnityEngine.Vector2(6f, 6f), oMin);
                Assert.AreEqual(new UnityEngine.Vector2(-6f, -6f), oMax);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }

        [Test]
        public void Tracker_iPhone_with_margin_6_inset_absorbs_top_and_bottom()
        {
            try
            {
                // iPhone-like: top inset 134, bottom inset 132, l/r 0.
                //   t = max(6, 134) = 134 (inset wins)
                //   r = max(6,   0) = 6
                //   b = max(6, 132) = 132 (inset wins)
                //   l = max(6,   0) = 6
                var (oMin, oMax) = RunTrackerWith(
                    safe: new UnityEngine.Rect(0f, 132f, 1170f, 2266f),  // yMin=132, yMax=2398
                    screen: new UnityEngine.Vector2(1170f, 2532f),
                    scaleFactor: 1f,
                    marginTop: 6f, marginRight: 6f, marginBottom: 6f, marginLeft: 6f);
                Assert.AreEqual(new UnityEngine.Vector2(6f, 132f), oMin);
                Assert.AreEqual(new UnityEngine.Vector2(-6f, -134f), oMax);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }

        [Test]
        public void Tracker_design_top_200_beats_inset_134()
        {
            try
            {
                //   t = max(200, 134) = 200 (design wins)
                //   r = max(0,   0)   = 0
                //   b = max(0,   132) = 132 (inset)
                //   l = max(0,   0)   = 0
                var (oMin, oMax) = RunTrackerWith(
                    safe: new UnityEngine.Rect(0f, 132f, 1170f, 2266f),
                    screen: new UnityEngine.Vector2(1170f, 2532f),
                    scaleFactor: 1f,
                    marginTop: 200f, marginRight: 0f, marginBottom: 0f, marginLeft: 0f);
                Assert.AreEqual(new UnityEngine.Vector2(0f, 132f), oMin);
                Assert.AreEqual(new UnityEngine.Vector2(0f, -200f), oMax);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }

        [Test]
        public void Tracker_HiDPI_converts_device_inset_to_design_px_via_scaleFactor()
        {
            try
            {
                // Device 1170×2532, safe (l=0, r=0, bottomDev=68, topDev=134), scaleFactor=2
                //   design insets: t=67, r=0, b=34, l=0
                // margin top=6 → final t = max(6, 67) = 67
                var (oMin, oMax) = RunTrackerWith(
                    safe: new UnityEngine.Rect(0f, 68f, 1170f, 2330f),   // yMin=68, yMax=2398
                    screen: new UnityEngine.Vector2(1170f, 2532f),
                    scaleFactor: 2f,
                    marginTop: 6f, marginRight: 0f, marginBottom: 0f, marginLeft: 0f);
                Assert.AreEqual(0f, oMin.x, 0.001f);
                Assert.AreEqual(34f, oMin.y, 0.001f, "bottom design inset = 68/2");
                Assert.AreEqual(0f, oMax.x, 0.001f);
                Assert.AreEqual(-67f, oMax.y, 0.001f, "top final = max(6, 134/2) = 67");
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }

        [Test]
        public void SafeArea_margin_variant_override_re_blends_on_variant_switch()
        {
            try
            {
                // PC-like: no device inset, so design margin wins on every edge.
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 0f, 1920f, 1080f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1920f, 1080f);

                // Variant `wide` is auto-declared by being referenced via `margin.wide=...`;
                // no explicit `<Variants>` declaration block needed (see UIDocumentParserTests
                // and BtnContentSizingTests for prior art).
                const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa' margin='6' margin.wide='20'/>
</Screen></PromptUGUI>";
                UI.LoadDocument("test", xml);
                var screen = UI.Open("S");
                var sa = screen.Get<SafeArea>("sa");

                var rt = sa.RectTransform;
                Assert.AreEqual(6f, rt.offsetMin.x, 0.001f, "base margin=6 → left=6");
                Assert.AreEqual(-6f, rt.offsetMax.x, 0.001f, "base margin=6 → right=-6");

                // Switch variant: ApplyCommon re-runs with margin=20, OnAfterApply re-captures,
                // tracker.Apply re-blends. Inset still 0, so design margin still wins.
                UI.Variants.Set("wide", true);

                Assert.AreEqual(20f, rt.offsetMin.x, 0.001f, "variant margin=20 → left=20");
                Assert.AreEqual(-20f, rt.offsetMax.x, 0.001f, "variant margin=20 → right=-20");
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
            }
        }

        [Test]
        public void SafeArea_with_margin_attribute_absorbs_device_inset()
        {
            // End-to-end: <SafeArea margin> goes through ApplyCommon, OnAfterApply captures
            // the margin via CaptureDesignMargin, tracker.Apply max-blends with device inset.
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);

                const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <SafeArea id='sa' margin='6,6,6,6'/>
</Screen></PromptUGUI>";
                UI.LoadDocument("test", xml);
                var screen = UI.Open("S");
                var sa = screen.Get<SafeArea>("sa");

                var rt = sa.RectTransform;
                // Anchor must always be (0,0)/(1,1) in v2.
                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin);
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
                // safe (0, 100, 1080, 1820), screen (1080, 1920) → device insets (l=0, r=0, b=100, t=0).
                // Canvas scaleFactor depends on host CanvasScaler config; assert qualitatively:
                //   - left/right: max(6, 0) = 6 (design margin in design px) regardless of scaleFactor
                //   - bottom: at least max(6, 100/sf). With sf=1 → 100; with sf=2 → 50. Either way ≥ 6.
                //   - top: max(6, 0/sf) = 6
                Assert.AreEqual(6f, rt.offsetMin.x, 0.001f,
                    "left = max(designLeft=6, deviceL=0) = 6");
                Assert.AreEqual(-6f, rt.offsetMax.x, 0.001f,
                    "right encoded as -6 (margin design value)");
                Assert.AreEqual(-6f, rt.offsetMax.y, 0.001f,
                    "top = max(6, 0/sf) = 6 → offsetMax.y = -6");
                Assert.GreaterOrEqual(rt.offsetMin.y, 6f,
                    "bottom = max(6, 100/sf) ≥ 6; inset absorbs the 6 when sf yields ≥ 6 design px");
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
            }
        }

        [Test]
        public void Tracker_max_blends_design_margin_with_device_inset()
        {
            // v2: tracker writes anchor=stretch + offsetMin/Max = max(designMargin, inset_designPx) per edge.
            // safe rect (0, 100, 1080, 1820) over screen 1080×1920 → device insets t=0, r=0, b=100, l=0
            // (yMin=100 → bottom inset 100; yMax=1920 → top inset 0; xMin/Max touch screen edges → l/r=0)
            // With scaleFactor=1, design insets = device insets.
            // With design margin top=50, others=0:
            //   final top    = max(50, 0)   = 50
            //   final right  = max(0,  0)   = 0
            //   final bottom = max(0,  100) = 100  ← absorbed
            //   final left   = max(0,  0)   = 0
            // offsetMin = (left, bottom) = (0, 100)
            // offsetMax = (-right, -top) = (0, -50)
            try
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride =
                    () => new UnityEngine.Rect(0f, 100f, 1080f, 1820f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride =
                    () => new UnityEngine.Vector2(1080f, 1920f);
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride =
                    () => 1f;

                var go = new UnityEngine.GameObject("sa", typeof(UnityEngine.RectTransform));
                var rt = (UnityEngine.RectTransform)go.transform;
                // ApplyCommon convention: offsetMin = (left, bottom), offsetMax = (-right, -top).
                // margin top=50, others=0 → offsetMin=(0,0), offsetMax=(-0, -50).
                rt.offsetMin = new UnityEngine.Vector2(0f, 0f);
                rt.offsetMax = new UnityEngine.Vector2(0f, -50f);

                var tracker = go.AddComponent<PromptUGUI.Controls.Internal.SafeAreaTracker>();
                tracker.CaptureDesignMargin(rt);
                tracker.Apply();

                Assert.AreEqual(UnityEngine.Vector2.zero, rt.anchorMin, "anchor should be (0,0)/(1,1) stretch");
                Assert.AreEqual(UnityEngine.Vector2.one, rt.anchorMax);
                Assert.AreEqual(0f, rt.offsetMin.x, 0.001f, "left=max(0, 0)");
                Assert.AreEqual(100f, rt.offsetMin.y, 0.001f, "bottom=max(0, 100)=100 (absorbed)");
                Assert.AreEqual(0f, rt.offsetMax.x, 0.001f, "-right=-max(0,0)=0");
                Assert.AreEqual(-50f, rt.offsetMax.y, 0.001f, "-top=-max(50, 0)=-50 (design wins)");

                UnityEngine.Object.DestroyImmediate(go);
            }
            finally
            {
                PromptUGUI.Controls.Internal.SafeAreaTracker.SafeAreaOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScreenSizeOverride = null;
                PromptUGUI.Controls.Internal.SafeAreaTracker.ScaleFactorOverride = null;
            }
        }
    }
}
