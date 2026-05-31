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

        // ---------- Parser validation: device-density 'Nx' ----------

        [Test]
        public void Parser_accepts_device_scale_integer()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='2x'/></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_accepts_device_scale_one_and_multidigit()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='a' scale='1x'/><Frame id='b' scale='10x'/></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_accepts_device_scale_variant()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='1x' scale.portrait='2x'/></Screen>
</PromptUGUI>";
            Assert.DoesNotThrow(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parser_rejects_fractional_device_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='1.5x'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("device-density", ex.Message);
        }

        [Test]
        public void Parser_rejects_zero_device_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='0x'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("device-density", ex.Message);
        }

        [Test]
        public void Parser_rejects_bare_x_device_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='x'/></Screen>
</PromptUGUI>";
            // 'x' length<2 → not the device branch → falls to float check → still errors (msg contains 'scale').
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale", ex.Message);
        }

        [Test]
        public void Parser_rejects_negative_device_scale()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='-1x'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("device-density", ex.Message);
        }

        [Test]
        public void Parser_rejects_uppercase_device_scale()
        {
            // Device-density is lowercase 'x' only (spec §9). Uppercase '2X' falls through
            // to the float check and is still rejected — pin it so the lowercase-only
            // decision can't silently regress.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'><Frame id='f' scale='2X'/></Screen>
</PromptUGUI>";
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("scale", ex.Message);
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

        // ---------- Box-preserving geometry ----------
        // The declared anchor/size/margin describes the VISUAL box; 'scale' only changes
        // render density, not the box. ApplyScales inflates the just-resolved RectTransform
        // by 1/scale so localScale=scale renders it back to the declared box. A stretch axis
        // widens its anchor span (1/scale, centered) so Unity re-drives it on resize; a fixed
        // axis divides sizeDelta. See the XML skill, "Relative scale (box-preserving)".

        [Test]
        public void Stretch_axis_widens_anchors_and_scales_sizeDelta()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame id='f' anchor='stretch' margin='10,10,10,10' scale='0.5'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            // span = 1/0.5 = 2, centered at 0.5 → [-0.5, 1.5] on both axes.
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1.5f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(-0.5f, rt.anchorMin.y, 1e-5f);
            Assert.AreEqual(1.5f, rt.anchorMax.y, 1e-5f);
            // base sizeDelta = -(l+r) = -20 each axis → /0.5 = -40.
            Assert.AreEqual(-40f, rt.sizeDelta.x, 1e-4f);
            Assert.AreEqual(-40f, rt.sizeDelta.y, 1e-4f);
            // symmetric margins → anchoredPosition unchanged at 0.
            Assert.AreEqual(0f, rt.anchoredPosition.x, 1e-4f);
            Assert.AreEqual(0f, rt.anchoredPosition.y, 1e-4f);
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
        }

        [Test]
        public void Fixed_axis_scales_sizeDelta_and_keeps_point_anchors()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame id='f' anchor='top-left' width='100' height='50' scale='0.5'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            // point anchor (span 0) → unchanged.
            Assert.AreEqual(0f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(0f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(1f, rt.anchorMin.y, 1e-5f);
            Assert.AreEqual(1f, rt.anchorMax.y, 1e-5f);
            // sizeDelta = declared / scale → 200 x 100 (visual size stays 100x50).
            Assert.AreEqual(200f, rt.sizeDelta.x, 1e-4f);
            Assert.AreEqual(100f, rt.sizeDelta.y, 1e-4f);
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
        }

        [Test]
        public void Mixed_stretch_and_fixed_axis_like_scaled_label()
        {
            // Mirrors the IconTab label: horizontal stretch (wrap must use the full box),
            // vertical top fixed. scale must NOT shrink the horizontal box.
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame id='f' anchor='top-stretch' margin='28,4,0,4' scale='0.5'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            // horizontal stretch widened: [-0.5, 1.5]; vertical top point unchanged: [1, 1].
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1.5f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(1f, rt.anchorMin.y, 1e-5f);
            Assert.AreEqual(1f, rt.anchorMax.y, 1e-5f);
            // base sizeDelta.x = -(l+r) = -(4+4) = -8 → /0.5 = -16.
            Assert.AreEqual(-16f, rt.sizeDelta.x, 1e-4f);
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
        }

        [Test]
        public void Scale_one_leaves_geometry_at_base()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame id='f' anchor='stretch' margin='10,10,10,10' scale='1'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(0f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(-20f, rt.sizeDelta.x, 1e-4f);
            Assert.AreEqual(1f, rt.localScale.x, 1e-6f);
        }

        [Test]
        public void Variant_reset_restores_base_geometry_and_does_not_accumulate()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame id='f' anchor='stretch' margin='10,10,10,10' scale.portrait='0.5'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            // landscape: scale unresolved → base geometry, identity scale.
            Assert.AreEqual(0f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(-20f, rt.sizeDelta.x, 1e-4f);

            UI.Orientation.AutoTrack = false;
            UI.Orientation.Set(isPortrait: true);
            // portrait: box-preserving applied.
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1.5f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(-40f, rt.sizeDelta.x, 1e-4f);

            UI.Orientation.Set(isPortrait: false);
            // back to landscape: reset to base — no leftover widening.
            Assert.AreEqual(0f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(1f, rt.anchorMax.x, 1e-5f);
            Assert.AreEqual(-20f, rt.sizeDelta.x, 1e-4f);

            UI.Orientation.Set(isPortrait: true);
            // portrait again: same widened values, NOT compounded (idempotent re-solve).
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-5f);
            Assert.AreEqual(-40f, rt.sizeDelta.x, 1e-4f);
        }

        [Test]
        public void Scale_under_layout_group_keeps_unscaled_slot_and_no_widen()
        {
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <VStack>
      <Frame id='f' width='100' height='50' scale='0.5'/>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = screen.Get("f");
            var rt = c.RectTransform;
            var le = c.GameObject.GetComponent<UnityEngine.UI.LayoutElement>();
            // localScale still applied under a LayoutGroup.
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-6f);
            // Documented footgun preserved: LayoutGroup reserves the UNSCALED slot.
            Assert.AreEqual(100f, le.preferredWidth, 1e-4f);
            // Compensation is skipped under a LayoutGroup → anchors are not widened negative.
            Assert.GreaterOrEqual(rt.anchorMin.x, 0f);
            Assert.GreaterOrEqual(rt.anchorMin.y, 0f);
        }

        // ---------- Runtime device-density: localScale = N / canvasFactor ----------

        [Test]
        public void DeviceScale_1x_in_pixel_factor3_is_one_third()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // /1920x1080 = factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='1x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(1f / 3f, rt.localScale.y, 1e-5f);
        }

        [Test]
        public void DeviceScale_2x_in_pixel_factor3_is_two_thirds()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='2x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_3x_in_pixel_factor3_is_identity()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='3x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_2x_in_pixel_factor2_is_identity()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f); // /1920x1080 = factor 2
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='2x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_2x_in_pixel_factor4_is_half()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(1920f, 1080f); // /480x270 = factor 4
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='480x270'>
    <Frame id='f' scale='2x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_1x_in_auto_factor2_is_half()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(3840f, 2160f); // /1920x1080 = factor 2
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto' reference='1920x1080'>
    <Frame id='f' scale='1x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_2x_in_auto_no_reference_is_two()
        {
            // No reference → ConstantPixelSize factor 1 → localScale = 2/1 = 2.
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='auto'>
    <Frame id='f' scale='2x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(2f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_2x_box_preserving_stretch_in_factor3()
        {
            // localScale = 2/3; inv = 1/0.6667 = 1.5. stretch span widened 1.5 about 0.5
            // → [-0.25, 1.25]; sizeDelta = -(10+10) * 1.5 = -30.
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' anchor='stretch' margin='10,10,10,10' scale='2x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.25f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(1.25f, rt.anchorMax.x, 1e-4f);
            Assert.AreEqual(-30f, rt.sizeDelta.x, 1e-3f);
        }

        [Test]
        public void DeviceScale_under_layout_group_keeps_unscaled_slot_and_no_widen()
        {
            UI.CanvasSizeOverride = () => new UnityEngine.Vector2(5760f, 3240f); // factor 3
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <VStack>
      <Frame id='f' width='100' height='50' scale='2x'/>
    </VStack>
  </Screen>
</PromptUGUI>");
            var c = screen.Get("f");
            var rt = c.RectTransform;
            var le = c.GameObject.GetComponent<UnityEngine.UI.LayoutElement>();
            Assert.AreEqual(2f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(100f, le.preferredWidth, 1e-4f);   // unscaled slot
            Assert.GreaterOrEqual(rt.anchorMin.x, 0f);          // compensation skipped
        }

        // ---------- Device-density recompute on canvas resize ----------

        [Test]
        public void DeviceScale_recomputes_localScale_on_resize()
        {
            UnityEngine.Vector2 size = new(5760f, 3240f); // factor 3
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='1x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);

            // Resize to factor 2; fire the relay (same seam ScreenScaleModeTests uses).
            size = new UnityEngine.Vector2(3840f, 2160f); // factor 2
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();
            relay.OnDimensionsChanged?.Invoke();

            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void DeviceScale_box_preserving_does_not_accumulate_across_resizes()
        {
            UnityEngine.Vector2 size = new(5760f, 3240f); // factor 3
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' anchor='stretch' margin='10,10,10,10' scale='1x'/>
  </Screen>
</PromptUGUI>");
            var rt = screen.Get("f").RectTransform;
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();

            // factor 3: localScale 1/3, inv 3 → span 3 about 0.5 → [-1, 2]; sizeDelta -20*3 = -60.
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-1f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(-60f, rt.sizeDelta.x, 1e-3f);

            // → factor 2: localScale 1/2, inv 2 → [-0.5, 1.5]; sizeDelta -20*2 = -40.
            size = new UnityEngine.Vector2(3840f, 2160f);
            relay.OnDimensionsChanged?.Invoke();
            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-0.5f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(-40f, rt.sizeDelta.x, 1e-3f);

            // → back to factor 3: must equal first reading, NOT compounded.
            size = new UnityEngine.Vector2(5760f, 3240f);
            relay.OnDimensionsChanged?.Invoke();
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);
            Assert.AreEqual(-1f, rt.anchorMin.x, 1e-4f);
            Assert.AreEqual(-60f, rt.sizeDelta.x, 1e-3f);
        }

        [Test]
        public void DeviceScale_in_add_block_recomputes_on_resize_after_activation()
        {
            UnityEngine.Vector2 size = new(5760f, 3240f); // factor 3
            UI.CanvasSizeOverride = () => size;
            // Nx appears ONLY inside an initially-inactive Add block (variant 'extra'),
            // so _hasDeviceScale must be (re)discovered when the block activates.
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='root' anchor='stretch'/>
    <Variant when='extra'>
      <Add into='#root'>
        <Frame id='added' scale='1x'/>
      </Add>
    </Variant>
  </Screen>
</PromptUGUI>");
            // Activate the variant → Add block instantiates; localScale = 1/factor = 1/3.
            UI.Variants.Set("extra", true);
            var rt = screen.Get("added").RectTransform;
            Assert.AreEqual(1f / 3f, rt.localScale.x, 1e-5f);

            // Resize to factor 2; must recompute even though Nx lived only in the
            // (now-active) Add block — regression guard for the _hasDeviceScale gap.
            size = new UnityEngine.Vector2(3840f, 2160f); // factor 2
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();
            relay.OnDimensionsChanged?.Invoke();

            Assert.AreEqual(0.5f, rt.localScale.x, 1e-5f);
        }

        [Test]
        public void Resize_without_device_scale_still_recomputes_factor()
        {
            // Regression: a Screen with NO 'Nx' takes the lightweight path (no ReSolve)
            // and still recomputes the canvas scaleFactor on resize.
            UnityEngine.Vector2 size = new(1920f, 1080f); // factor 1
            UI.CanvasSizeOverride = () => size;
            var screen = OpenScreen(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S' scale-mode='pixel' reference='1920x1080'>
    <Frame id='f' scale='0.5'/>
  </Screen>
</PromptUGUI>");
            var scaler = screen.RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
            Assert.AreEqual(1f, scaler.scaleFactor, 1e-6f);
            Assert.AreEqual(0.5f, screen.Get("f").RectTransform.localScale.x, 1e-6f);

            size = new UnityEngine.Vector2(3840f, 2160f); // factor 2
            var relay = screen.RootGameObject.GetComponent<PromptUGUI.Application.RectDimensionsRelay>();
            relay.OnDimensionsChanged?.Invoke();

            Assert.AreEqual(2f, scaler.scaleFactor, 1e-6f);
            Assert.AreEqual(0.5f, screen.Get("f").RectTransform.localScale.x, 1e-6f); // unchanged
        }
    }
}
