using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Application
{
    public class ScaleAttributeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen OpenScreen(string xml)
        {
            UI.LoadDocument("test", xml);
            return (PromptUGUI.Application.Screen)UI.Open("S");
        }

        // ---------- Parser validation ----------

        [Test]
        public void Parser_rejects_zero_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='0'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale", ex.Message);
        }

        [Test]
        public void Parser_rejects_negative_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='-1'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale", ex.Message);
        }

        [Test]
        public void Parser_rejects_non_numeric_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='half'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale", ex.Message);
        }

        [Test]
        public void Parser_rejects_invalid_scale_variant_value()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='1' scale.mobile='nope'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale.mobile", ex.Message);
        }

        [Test]
        public void Parser_accepts_integer_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='2'/></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_accepts_fractional_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='0.5'/></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        // <Animation scale> uses 'from:to' keyframe syntax (parsed by AnimationSpec at
        // runtime), not the static positive-float form — parser must defer.
        [Test]
        public void Parser_accepts_Animation_scale_from_to()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Animation id='a' scale='1:0.5' duration='0.1s'><Frame id='f'/></Animation></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_accepts_Animation_scale_vec2_from_to()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Animation id='a' scale='0.5,1:1,2' duration='0.1s'><Frame id='f'/></Animation></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_accepts_Animation_scale_variant_from_to()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Animation id='a' scale='1:0.5' scale.mobile='1:0.8' duration='0.1s'><Frame id='f'/></Animation></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        // ---------- Runtime (relative semantic: localScale = N) ----------

        [Test]
        public void Scale_one_is_identity()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='1'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f, rt.localScale.x, 1e-6f);
            Assert.AreEqual(1f, rt.localScale.y, 1e-6f);
            Assert.AreEqual(1f, rt.localScale.z, 1e-6f);
        }

        [Test]
        public void Scale_half_sets_localScale_to_half_in_pixel_mode()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='0.5'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
            Assert.AreEqual(0.5f, rt.localScale.y, 1e-6f);
        }

        [Test]
        public void Scale_two_doubles_localScale()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f);
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='480x270'>
    <Frame id='f' scale='2'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(2f, rt.localScale.x, 1e-6f);
        }

        [Test]
        public void Scale_works_in_auto_mode_too()
        {
            // Relative semantic — not pixel-mode-gated. localScale = N regardless of canvas mode.
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame id='f' scale='0.5'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
        }

        [Test]
        public void Scale_independent_of_canvas_factor()
        {
            // Same XML, different canvas sizes → same localScale (the whole point of
            // relative semantic vs. the original absolute device-pixel semantic).
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f);
            var s1 = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='480x270'>
    <Frame id='f' scale='0.5'/>
  </Screen>
</PromptUGUI>");
            var localScale1 = s1.Get("f").RectTransform.localScale.x;
            s1.Close();

            UI.ResetForTests();
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f);
            var s2 = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='480x270'>
    <Frame id='f' scale='0.5'/>
  </Screen>
</PromptUGUI>");
            var localScale2 = s2.Get("f").RectTransform.localScale.x;

            Assert.AreEqual(localScale1, localScale2, 1e-6f);
            Assert.AreEqual(0.5f, localScale1, 1e-6f);
        }

        [Test]
        public void Element_without_scale_keeps_identity_localScale()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f, rt.localScale.x, 1e-6f);
            Assert.AreEqual(1f, rt.localScale.y, 1e-6f);
        }

        [Test]
        public void Variant_override_changes_localScale_on_ReSolve()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame id='f' scale='1' scale.portrait='0.5'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f, rt.localScale.x, 1e-6f);

            UI.Orientation.AutoTrack = false;
            UI.Orientation.Set(isPortrait: true);
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
        }

        [Test]
        public void Variant_only_scale_resets_localScale_when_variant_inactive()
        {
            // scale only declared via variant; inactive variant → no resolved value → localScale=1.
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame id='f' scale.portrait='0.5'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            // landscape (default): scale not resolved → identity.
            Assert.AreEqual(1f, rt.localScale.x, 1e-6f);

            UI.Orientation.AutoTrack = false;
            UI.Orientation.Set(isPortrait: true);
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);

            UI.Orientation.Set(isPortrait: false);
            // back to landscape: scale unresolved again → identity.
            Assert.AreEqual(1f, rt.localScale.x, 1e-6f);
        }
    }
}
