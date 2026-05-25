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
        public void ResetForTests_clears_default_and_override()
        {
            UI.DefaultScaleMode = ScaleMode.Pixel;
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(99f, 99f);
            UI.ResetForTests();
            Assert.AreEqual(ScaleMode.Auto, UI.DefaultScaleMode);
            Assert.IsNull(UI.CanvasSizeOverride);
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
    }
}
