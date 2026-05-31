using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Application
{
    public class ScreenScaleModeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Parser_stores_scale_mode_attr_on_screen_root()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("pixel", doc.Screens[0].Root.Attributes["scale-mode"]);
        }

        [Test]
        public void Parser_stores_scale_mode_auto()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("auto", doc.Screens[0].Root.Attributes["scale-mode"]);
        }

        [Test]
        public void Parser_screen_without_scale_mode_has_no_attr()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            Assert.IsFalse(doc.Screens[0].Root.Attributes.ContainsKey("scale-mode"));
        }

        [Test]
        public void Parser_rejects_invalid_scale_mode_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel-perfect'><Frame/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale-mode", ex.Message);
            StringAssert.Contains("'auto'", ex.Message);
            StringAssert.Contains("'pixel'", ex.Message);
        }

        [Test]
        public void Parser_accepts_empty_scale_mode_as_unset_semantics()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode=''><Frame/></Screen>
</PromptUGUI>";
            // Empty string is stored verbatim; runtime treats it as 'inherit DefaultScaleMode'.
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("", doc.Screens[0].Root.Attributes["scale-mode"]);
        }

        [Test]
        public void Parser_stores_scale_mode_variant_override()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'
          scale-mode='auto'
          scale-mode.portrait='pixel'
          scale-mode.landscape='auto'
          reference='1920x1080'>
    <Frame/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var list = doc.Screens[0].Root.VariantOverrides["scale-mode"];
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("portrait", list[0].Variant);
            Assert.AreEqual("pixel", list[0].Value);
            Assert.AreEqual("landscape", list[1].Variant);
            Assert.AreEqual("auto", list[1].Value);
        }

        [Test]
        public void Parser_rejects_invalid_scale_mode_variant_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode.mobile='nope'><Frame/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale-mode.mobile", ex.Message);
        }

        [Test]
        public void Parser_rejects_scale_mode_variant_with_extra_dot()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode.foo.bar='pixel'><Frame/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale-mode.foo.bar", ex.Message);
        }

        private static PromptUGUI.Application.Screen OpenScreen(string xml)
        {
            UI.LoadDocument("test", xml);
            return (PromptUGUI.Application.Screen)UI.Open("S");
        }

        [Test]
        public void Pixel_with_design_equal_to_canvas_yields_factor_1()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize, scaler.uiScaleMode);
            Assert.AreEqual(1f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_with_4k_canvas_yields_factor_2()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(2f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_with_smaller_canvas_snaps_to_half()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1366f, 768f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(0.5f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_without_reference_logs_error_and_falls_back_to_1()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f);
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("requires.*reference"));
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize, scaler.uiScaleMode);
            Assert.AreEqual(1f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Default_scale_mode_pixel_applies_without_xml_attr()
        {
            UI.DefaultScaleMode = ScaleMode.Pixel;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize, scaler.uiScaleMode);
            Assert.AreEqual(2f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Xml_auto_overrides_default_pixel()
        {
            UI.DefaultScaleMode = ScaleMode.Pixel;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
        }

        [Test]
        public void Variant_flip_switches_to_pixel_mode()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'
          scale-mode='auto'
          scale-mode.portrait='pixel'
          reference='1920x1080'
          reference.portrait='1080x1920'>
    <Frame/>
  </Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);

            UI.Orientation.AutoTrack = false;
            UI.Orientation.Set(isPortrait: true);
            // ReSolve re-applies the scaler.
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize, scaler.uiScaleMode);
        }

        [Test]
        public void Pixel_mode_enables_canvas_pixelPerfect()
        {
            // scale-mode=pixel naturally pairs with Canvas.pixelPerfect: scale-mode handles
            // integer outer scaling, pixelPerfect snaps every UI vertex inside the canvas
            // to integer pixels (anchor/margin math can produce sub-pixel positions).
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var canvas = screen.RootGameObject.GetComponent<UnityEngine.Canvas>();
            Assert.IsTrue(canvas.pixelPerfect);
        }

        [Test]
        public void Auto_mode_leaves_canvas_pixelPerfect_off()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var canvas = screen.RootGameObject.GetComponent<UnityEngine.Canvas>();
            Assert.IsFalse(canvas.pixelPerfect);
        }

        [Test]
        public void Variant_flip_toggles_canvas_pixelPerfect()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'
          scale-mode='auto'
          scale-mode.portrait='pixel'
          reference='1920x1080'
          reference.portrait='1080x1920'>
    <Frame/>
  </Screen>
</PromptUGUI>");
            var canvas = screen.RootGameObject.GetComponent<UnityEngine.Canvas>();
            Assert.IsFalse(canvas.pixelPerfect, "auto branch should leave pixelPerfect off");

            UI.Orientation.AutoTrack = false;
            UI.Orientation.Set(isPortrait: true);
            // ReSolve re-applies the scaler and must flip pixelPerfect on with the mode.
            Assert.IsTrue(canvas.pixelPerfect, "pixel branch should turn pixelPerfect on");

            UI.Orientation.Set(isPortrait: false);
            Assert.IsFalse(canvas.pixelPerfect, "flipping back to auto must turn pixelPerfect off again");
        }

        [Test]
        public void CanvasConfigurator_can_override_pixelPerfect_off()
        {
            // CanvasConfigurator runs AFTER ApplyCanvasScaler in Open(), so a user who
            // explicitly wants smooth animations on a pixel-art Screen can opt out.
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f);
            UI.CanvasConfigurator = (c, _) => c.pixelPerfect = false;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var canvas = screen.RootGameObject.GetComponent<UnityEngine.Canvas>();
            Assert.IsFalse(canvas.pixelPerfect);
        }

        [Test]
        public void ResetForTests_clears_default_and_override()
        {
            UI.DefaultScaleMode = ScaleMode.Pixel;
            UI.MinPixelScale = 0.5f;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(99f, 99f);
            UI.ResetForTests();
            Assert.AreEqual(ScaleMode.Auto, UI.DefaultScaleMode);
            Assert.AreEqual(0f, UI.MinPixelScale, 1e-6f);
            Assert.IsNull(UI.CanvasSizeOverride);
        }

        [Test]
        public void Pixel_MinPixelScale_clamps_low_factor_up()
        {
            // raw = min(480/1920, 270/1080) = 0.25 → solver returns 0.25.
            // MinPixelScale = 0.5 pulls it up to 0.5.
            UI.MinPixelScale = 0.5f;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(480f, 270f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(0.5f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_MinPixelScale_does_not_cap_high_factor()
        {
            // 4K screen + 1080p design = factor 2, which is well above MinPixelScale.
            UI.MinPixelScale = 0.5f;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(2f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_MinPixelScale_one_locks_to_at_least_native()
        {
            // MinPixelScale = 1 means "never shrink below 1x; let content overflow
            // anchor=stretch elements instead". With a 1366x768 screen against
            // 1920x1080 design, raw = 0.711 → solver = 0.5 → clamp → 1.
            UI.MinPixelScale = 1f;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1366f, 768f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(1f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_MinPixelScale_default_zero_means_no_floor()
        {
            // Default UI.MinPixelScale = 0 must preserve pre-feature behavior:
            // the algorithm can fall through to 1/2^n on tiny screens.
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(480f, 270f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(0.25f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_PowerOfTwo_snaps_3x_screen_down_to_2x()
        {
            // raw = min(5760/1920, 3240/1080) = 3 -> default would floor to 3,
            // power-of-two snaps DOWN to 2.
            UI.PixelScalePowerOfTwo = true;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(2f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_PowerOfTwo_default_false_keeps_integer_3x()
        {
            // Default UI.PixelScalePowerOfTwo = false must preserve the integer ladder:
            // a 3x-capable screen renders at 3x, not snapped to 2x.
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(3f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_PowerOfTwo_snap_runs_before_MinPixelScale_clamp()
        {
            // raw = 5. Power-of-two snaps it DOWN to 4 (the integer ladder would
            // keep 5). MinPixelScale = 4 is a floor: 4 is not below it, so it stays 4.
            // Without the pow2 snap this would be floor(5)=5 clamped by 4 -> 5, so a
            // result of 4 proves the snap ran and the clamp left it alone.
            UI.PixelScalePowerOfTwo = true;
            UI.MinPixelScale = 4f;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(9600f, 5400f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(4f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void ResetForTests_clears_PixelScalePowerOfTwo()
        {
            UI.PixelScalePowerOfTwo = true;
            UI.ResetForTests();
            Assert.IsFalse(UI.PixelScalePowerOfTwo);
        }

        [Test]
        public void Auto_mode_ignores_PixelScalePowerOfTwo()
        {
            // Power-of-two is documented as Pixel-only; the Auto branch must not read it.
            UI.PixelScalePowerOfTwo = true;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            Assert.AreEqual(1f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Auto_mode_ignores_MinPixelScale()
        {
            // Auto mode uses ScaleWithScreenSize (continuous fractional). MinPixelScale
            // is documented as Pixel-only; verify the Auto branch doesn't read it.
            UI.MinPixelScale = 1f;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(960f, 540f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize, scaler.uiScaleMode);
            // Auto leaves scaleFactor at default 1; Unity computes effective scale
            // internally from referenceResolution + matchWidthOrHeight, not from
            // CanvasScaler.scaleFactor (which only applies in ConstantPixelSize mode).
            Assert.AreEqual(1f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Pixel_without_canvas_size_override_reads_canvas_rect_without_NRE()
        {
            // Reproduces the NRE that occurred when ApplyCanvasScaler ran before
            // RootGameObject was assigned in Open(). The point isn't the factor —
            // it's that ReadCanvasRectSize can read RootGameObject's RectTransform
            // without throwing. CanvasSizeOverride intentionally NOT set so the
            // pixel branch takes the ReadCanvasRectSize path.
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize, scaler.uiScaleMode);
            // PixelScaleSolver always returns a positive factor (>= 0 degenerate guard or
            // power-of-two snap). The exact value depends on the host EditMode canvas
            // rect — we only care that Open() completed and a factor was assigned.
            Assert.Greater(scaler.scaleFactor, 0f);
        }

        [Test]
        public void Resize_event_recomputes_pixel_factor()
        {
            UnityEngine.Vector2 size = new(1920f, 1080f);
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(1f, scaler.scaleFactor, 1e-6f);

            // Simulate window resize: change the override and fire the relay.
            size = new UnityEngine.Vector2(3840f, 2160f);
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();
            relay.OnDimensionsChanged?.Invoke();

            Assert.AreEqual(2f, scaler.scaleFactor, 1e-6f);
        }

        [Test]
        public void Resize_does_not_recurse()
        {
            UnityEngine.Vector2 size = new(1920f, 1080f);
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>");
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();
            // Manually fire 5 times in a row; should not stack-overflow.
            for (int i = 0; i < 5; i++) relay.OnDimensionsChanged?.Invoke();
            Assert.Pass();
        }

        [Test]
        public void Pixel_apply_is_idempotent_under_repeated_relay_fires()
        {
            // Reproduces the runtime flicker bug: ApplyPixel previously read
            // RectTransform.rect, which equals Screen.size / scaleFactor in
            // ConstantPixelSize mode — so each ApplyPixel write triggered a new
            // OnRectTransformDimensionsChange with a different rect, computing a
            // different factor next time. The fix is to read Canvas.pixelRect
            // (independent of scaleFactor). With a stable input source, repeated
            // relay invocations must yield the same scaleFactor every time.
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='480x270'><Frame/></Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();
            var initial = scaler.scaleFactor;
            Assert.AreEqual(4f, initial, 1e-6f, "expected factor=4 for 1920x1080 canvas against 480x270 design");

            for (int i = 0; i < 5; i++)
            {
                relay.OnDimensionsChanged?.Invoke();
                Assert.AreEqual(initial, scaler.scaleFactor, 1e-6f,
                    $"scaleFactor oscillated on relay fire #{i + 1}");
            }
        }

        [Test]
        public void RectDimensionsRelay_skips_invoke_when_rect_size_unchanged()
        {
            // Direct unit test of the relay's size-diff guard. We trigger via the
            // InvokeRectChangedForTests seam because Unity's auto-fire of
            // OnRectTransformDimensionsChange doesn't run on orphan GameObjects in
            // EditMode (the magic-method bridge only fires inside a real Canvas /
            // scene hierarchy).
            var go = new UnityEngine.GameObject("relay-test-rt", typeof(UnityEngine.RectTransform));
            try
            {
                var rt = (UnityEngine.RectTransform)go.transform;
                rt.sizeDelta = new UnityEngine.Vector2(100f, 100f);
                var relay = go.AddComponent<PromptUGUI.Application.RectDimensionsRelay>();
                int callCount = 0;
                relay.OnDimensionsChanged = () => callCount++;

                // First trigger → _lastSize was NaN, so guard sees change → fire.
                relay.InvokeRectChangedForTests();
                Assert.AreEqual(1, callCount, "first trigger should fire (NaN sentinel)");

                // Same size → guard skips.
                relay.InvokeRectChangedForTests();
                relay.InvokeRectChangedForTests();
                relay.InvokeRectChangedForTests();
                Assert.AreEqual(1, callCount, "relay must skip when rect size unchanged");

                // Real size change → fires again.
                rt.sizeDelta = new UnityEngine.Vector2(200f, 100f);
                relay.InvokeRectChangedForTests();
                Assert.AreEqual(2, callCount, "relay must fire when rect size actually changes");

                // Stable again → no more fires.
                relay.InvokeRectChangedForTests();
                relay.InvokeRectChangedForTests();
                Assert.AreEqual(2, callCount, "relay re-skips after the new size becomes stable");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }
    }
}
