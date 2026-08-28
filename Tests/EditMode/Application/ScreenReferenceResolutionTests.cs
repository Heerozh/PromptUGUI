using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Application
{
    public class ScreenReferenceResolutionTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Parser_stores_reference_attr_on_screen_root()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080'>
    <Frame/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var screen = doc.Screens[0];
            Assert.AreEqual("1920x1080", screen.Root.Attributes["reference"]);
        }

        [Test]
        public void Parser_screen_without_reference_has_no_attr()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame/></Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var screen = doc.Screens[0];
            Assert.IsFalse(screen.Root.Attributes.ContainsKey("reference"));
        }

        [Test]
        public void Parser_stores_reference_variant_override()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080' reference.mobile='1080x1920'>
    <Frame/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var screen = doc.Screens[0];
            Assert.AreEqual("1920x1080", screen.Root.Attributes["reference"]);
            Assert.IsTrue(screen.Root.VariantOverrides.ContainsKey("reference"));
            var list = screen.Root.VariantOverrides["reference"];
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("mobile", list[0].Variant);
            Assert.AreEqual("1080x1920", list[0].Value);
        }

        [Test]
        public void Parser_stores_reference_variant_empty_clears_base()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080' reference.tv=''>
    <Frame/>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var list = doc.Screens[0].Root.VariantOverrides["reference"];
            Assert.AreEqual("tv", list[0].Variant);
            Assert.AreEqual("", list[0].Value);
        }

        [Test]
        public void Parser_rejects_invalid_reference_base()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x0'><Frame/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("reference", ex.Message);
            StringAssert.Contains("positive", ex.Message);
        }

        [Test]
        public void Parser_rejects_invalid_reference_variant()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference.mobile='-1x100'><Frame/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("reference.mobile", ex.Message);
        }

        [Test]
        public void Parser_rejects_reference_without_x()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920'><Frame/></Screen>
</PromptUGUI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Open_unset_reference_keeps_constant_pixel_size()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Frame/></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize,
                            scaler.uiScaleMode);
            Assert.AreEqual(1f, scaler.scaleFactor);
        }

        [Test]
        public void Open_landscape_reference_uses_expand()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize,
                            scaler.uiScaleMode);
            Assert.AreEqual(new UnityEngine.Vector2(1920, 1080), scaler.referenceResolution);
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand,
                            scaler.screenMatchMode);
        }

        [Test]
        public void Open_portrait_reference_uses_expand()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1080x1920'><Frame/></Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(new UnityEngine.Vector2(1080, 1920), scaler.referenceResolution);
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand,
                            scaler.screenMatchMode);
        }

        [Test]
        public void Open_square_reference_uses_expand()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1000x1000'><Frame/></Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand,
                            scaler.screenMatchMode);
        }

        [Test]
        public void Open_canvas_configurator_runs_after_xml_reference()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080'><Frame/></Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            UI.CanvasConfigurator = (canvas, _) =>
            {
                var s = canvas.GetComponent<UnityEngine.UI.CanvasScaler>();
                s.referenceResolution = new UnityEngine.Vector2(2560, 1440);
            };
            try
            {
                var screen = UI.Open("S");
                var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
                Assert.AreEqual(new UnityEngine.Vector2(2560, 1440), scaler.referenceResolution);
            }
            finally { UI.CanvasConfigurator = null; }
        }

        [Test]
        public void Variant_flip_reapplies_canvas_scaler()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080' reference.mobile='1080x1920'>
    <Frame/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(new UnityEngine.Vector2(1920, 1080), scaler.referenceResolution);

            UI.Variants.Set("mobile", true);
            try
            {
                Assert.AreEqual(new UnityEngine.Vector2(1080, 1920), scaler.referenceResolution);
                Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand,
                                scaler.screenMatchMode);
            }
            finally { UI.Variants.Set("mobile", false); }
        }

        [Test]
        public void Variant_empty_value_clears_to_constant_pixel_size()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080' reference.raw=''>
    <Frame/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();

            UI.Variants.Set("raw", true);
            try
            {
                Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize,
                                scaler.uiScaleMode);
                Assert.AreEqual(1f, scaler.scaleFactor);
            }
            finally { UI.Variants.Set("raw", false); }
        }

        [Test]
        public void Variant_off_returns_to_base_reference()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080' reference.mobile='1080x1920'>
    <Frame/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            UI.Variants.Set("mobile", true);
            UI.Variants.Set("mobile", false);
            Assert.AreEqual(new UnityEngine.Vector2(1920, 1080), scaler.referenceResolution);
            Assert.AreEqual(UnityEngine.UI.CanvasScaler.ScreenMatchMode.Expand,
                            scaler.screenMatchMode);
        }

        [Test]
        public void Auto_factor_scales_by_the_tighter_axis_on_a_tall_phone()
        {
            // 1080x1920 design on a 1179x2556 phone (taller than 16:9). Width is the
            // tighter axis (1179/1080 = 1.092 < 2556/1920 = 1.331), so Expand scales by
            // width and the full 1080 design width still fits; the slack lands on height.
            // The retired lock-by-orientation rule locked height here and pushed ~194
            // design units of width off-screen. 'Nx' divides by the cached factor, which
            // is how the choice becomes observable.
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1179f, 2556f);
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1080x1920'><Frame id='f' scale='1x'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1080f / 1179f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void Auto_factor_scales_by_the_tighter_axis_on_an_ultrawide_desktop()
        {
            // 1920x1080 design on 2560x1080 (21:9). Height is the tighter axis
            // (1080/1080 = 1 < 2560/1920 = 1.333), so the full 1080 design height fits
            // and the extra width is slack. Locking width would have scaled 1.333x and
            // cropped 270 design units of height.
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(2560f, 1080f);
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' reference='1920x1080'><Frame id='f' scale='1x'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f, rt.localScale.x, 1e-5f);
        }
    }
}
