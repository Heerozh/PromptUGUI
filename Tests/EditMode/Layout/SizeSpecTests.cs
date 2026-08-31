using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Layout;

namespace PromptUGUI.Tests.Layout
{
    public class SizeSpecTests
    {
        [Test]
        public void Parses_WxH()
        {
            var s = SizeSpec.Parse(size: "240x80", width: null, height: null);
            Assert.AreEqual(240f, s.Width);
            Assert.AreEqual(80f, s.Height);
            Assert.IsTrue(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
        }

        [Test]
        public void Parses_width_only()
        {
            var s = SizeSpec.Parse(size: null, width: "200", height: null);
            Assert.AreEqual(200f, s.Width);
            Assert.IsTrue(s.HasWidth);
            Assert.IsFalse(s.HasHeight);
        }

        [Test]
        public void Parses_height_only()
        {
            var s = SizeSpec.Parse(size: null, width: null, height: "64");
            Assert.AreEqual(64f, s.Height);
            Assert.IsFalse(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
        }

        [Test]
        public void Empty_when_all_null()
        {
            var s = SizeSpec.Parse(null, null, null);
            Assert.IsFalse(s.HasWidth);
            Assert.IsFalse(s.HasHeight);
        }

        [TestCase(AnchorVertical.Top, AnchorHorizontal.Stretch, "240x80", null, null)]
        [TestCase(AnchorVertical.Stretch, AnchorHorizontal.Left, "240x80", null, null)]
        [TestCase(AnchorVertical.Top, AnchorHorizontal.Stretch, null, "200", null)]
        [TestCase(AnchorVertical.Stretch, AnchorHorizontal.Left, null, null, "64")]
        public void Throws_when_specifying_size_on_stretched_axis(
            AnchorVertical v, AnchorHorizontal h,
            string size, string width, string height)
        {
            var spec = SizeSpec.Parse(size, width, height);
            var anchor = new AnchorPreset(v, h);
            Assert.Throws<System.ArgumentException>(() =>
                spec.ValidateAgainst(anchor));
        }

        [TestCase("WxH")]
        [TestCase("100x")]
        [TestCase("x100")]
        [TestCase("100")]
        public void Throws_on_malformed_size(string bad)
        {
            Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(bad, null, null));
        }

        [Test]
        public void Parses_width_stretch_sets_flexible_flag()
        {
            var s = SizeSpec.Parse(size: null, width: "stretch", height: null);
            Assert.IsTrue(s.HasWidth, "stretch is a width assignment, HasWidth must be true");
            Assert.IsTrue(s.IsFlexibleWidth, "width='stretch' must set IsFlexibleWidth");
            Assert.IsFalse(s.HasHeight);
            Assert.IsFalse(s.IsFlexibleHeight);
        }

        [Test]
        public void Parses_height_stretch_sets_flexible_flag()
        {
            var s = SizeSpec.Parse(size: null, width: null, height: "stretch");
            Assert.IsTrue(s.HasHeight);
            Assert.IsTrue(s.IsFlexibleHeight);
            Assert.IsFalse(s.HasWidth);
            Assert.IsFalse(s.IsFlexibleWidth);
        }

        [Test]
        public void Parses_width_stretch_with_height_numeric()
        {
            var s = SizeSpec.Parse(size: null, width: "stretch", height: "72");
            Assert.IsTrue(s.IsFlexibleWidth);
            Assert.IsTrue(s.HasHeight);
            Assert.IsFalse(s.IsFlexibleHeight);
            Assert.AreEqual(72f, s.Height);
        }

        [TestCase("stretch")]
        [TestCase("stretchx72")]
        [TestCase("100xstretch")]
        [TestCase("stretchxstretch")]
        public void Throws_when_stretch_used_in_size_attribute(string bad)
        {
            // 'stretch' keyword is only valid on width=/height= attrs, never inside compact size=.
            Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(size: bad, width: null, height: null));
        }

        [Test]
        public void Stretch_on_anchor_stretched_axis_throws()
        {
            // Same rule as numeric: anchor stretched axis MUST use margin, cannot specify width/height.
            // 'stretch' keyword counts as specifying width.
            var spec = SizeSpec.Parse(size: null, width: "stretch", height: null);
            var anchor = new AnchorPreset(AnchorVertical.Top, AnchorHorizontal.Stretch);
            Assert.Throws<System.ArgumentException>(() => spec.ValidateAgainst(anchor));
        }

        // ───── weighted stretch ─────

        [Test]
        public void Bare_stretch_has_weight_one()
        {
            var s = SizeSpec.Parse(size: null, width: "stretch", height: null);
            Assert.AreEqual(1f, s.WeightWidth, "bare 'stretch' = weight 1");
        }

        [Test]
        public void Stretch_with_weight_two()
        {
            var s = SizeSpec.Parse(size: null, width: "stretch*2", height: null);
            Assert.IsTrue(s.IsFlexibleWidth);
            Assert.AreEqual(2f, s.WeightWidth);
        }

        [Test]
        public void Stretch_with_fractional_weight()
        {
            var s = SizeSpec.Parse(size: null, width: null, height: "stretch*0.5");
            Assert.IsTrue(s.IsFlexibleHeight);
            Assert.AreEqual(0.5f, s.WeightHeight);
        }

        [TestCase("stretch*0")]
        [TestCase("stretch*-1")]
        [TestCase("stretch*foo")]
        [TestCase("stretch*")]
        [TestCase("stretch**2")]
        public void Throws_on_invalid_stretch_weight(string bad)
        {
            Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(size: null, width: bad, height: null));
        }

        // ───── fractional % ─────

        [Test]
        public void Parses_width_50_percent()
        {
            var s = SizeSpec.Parse(size: null, width: "50%", height: null);
            Assert.IsTrue(s.HasWidth, "fractional is a width assignment");
            Assert.IsTrue(s.IsFractionalWidth);
            Assert.AreEqual(0.5f, s.WidthFraction);
            Assert.IsFalse(s.IsFlexibleWidth, "fractional and flexible are distinct modes");
        }

        [Test]
        public void Parses_height_decimal_percent()
        {
            var s = SizeSpec.Parse(size: null, width: null, height: "33.3%");
            Assert.IsTrue(s.IsFractionalHeight);
            Assert.AreEqual(0.333f, s.HeightFraction, 0.001f);
        }

        [Test]
        public void Hundred_percent_is_allowed()
        {
            // 100% == full parent axis; equivalent to anchor=stretch but expressible per-axis.
            var s = SizeSpec.Parse(size: null, width: "100%", height: null);
            Assert.IsTrue(s.IsFractionalWidth);
            Assert.AreEqual(1f, s.WidthFraction);
        }

        [TestCase("0%")]
        [TestCase("-10%")]
        [TestCase("150%")]
        [TestCase("100.1%")]
        [TestCase("%")]
        [TestCase("foo%")]
        public void Throws_on_invalid_percent(string bad)
        {
            Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(size: null, width: bad, height: null));
        }

        [TestCase("50%")]
        [TestCase("50%x100")]
        [TestCase("100x50%")]
        public void Throws_when_percent_used_in_size_attribute(string bad)
        {
            // size= stays numeric-only — keyword forms (stretch / %) belong on per-axis attrs.
            Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(size: bad, width: null, height: null));
        }

        [Test]
        public void Fractional_on_anchor_stretched_axis_throws()
        {
            // Same rule as numeric/stretch: fractional counts as specifying width.
            var spec = SizeSpec.Parse(size: null, width: "50%", height: null);
            var anchor = new AnchorPreset(AnchorVertical.Top, AnchorHorizontal.Stretch);
            Assert.Throws<System.ArgumentException>(() => spec.ValidateAgainst(anchor));
        }
        // ───── clamp(min, middle, max) ─────

        [Test]
        public void Clamp_percent_sets_fractional_and_bounds()
        {
            var s = SizeSpec.Parse(size: null, width: "clamp(167, 46.4%, 250)", height: null);
            Assert.IsTrue(s.HasWidth, "clamp is a width assignment");
            Assert.IsTrue(s.IsClampedWidth);
            Assert.IsTrue(s.IsFractionalWidth, "middle 'N%' keeps the fractional flag");
            Assert.AreEqual(0.464f, s.WidthFraction, 0.0001f);
            Assert.AreEqual(167f, s.MinWidth);
            Assert.AreEqual(250f, s.MaxWidth);
            Assert.IsFalse(s.IsFlexibleWidth);
            Assert.IsFalse(s.HasHeight);
            Assert.IsFalse(s.IsClampedHeight);
        }

        [Test]
        public void Clamp_open_min_is_negative_infinity()
        {
            var s = SizeSpec.Parse(size: null, width: "clamp(_, 46%, 250)", height: null);
            Assert.IsTrue(s.IsClampedWidth);
            Assert.IsTrue(float.IsNegativeInfinity(s.MinWidth), "'_' min = no lower bound");
            Assert.AreEqual(250f, s.MaxWidth);
        }

        [Test]
        public void Clamp_open_max_is_positive_infinity()
        {
            var s = SizeSpec.Parse(size: null, width: "clamp(320, 60%, _)", height: null);
            Assert.AreEqual(320f, s.MinWidth);
            Assert.IsTrue(float.IsPositiveInfinity(s.MaxWidth), "'_' max = no upper bound");
        }

        [Test]
        public void Clamp_tolerates_whitespace_around_parts()
        {
            var s = SizeSpec.Parse(size: null, width: "clamp( 167 , 46% , 250 )", height: null);
            Assert.IsTrue(s.IsClampedWidth);
            Assert.AreEqual(167f, s.MinWidth);
            Assert.AreEqual(0.46f, s.WidthFraction, 0.0001f);
            Assert.AreEqual(250f, s.MaxWidth);
        }

        [Test]
        public void Clamp_stretch_sets_flexible_and_bounds()
        {
            var s = SizeSpec.Parse(size: null, width: "clamp(167, stretch, 250)", height: null);
            Assert.IsTrue(s.IsClampedWidth);
            Assert.IsTrue(s.IsFlexibleWidth, "middle 'stretch' keeps the flexible flag");
            Assert.AreEqual(1f, s.WeightWidth);
            Assert.IsFalse(s.IsFractionalWidth);
            Assert.AreEqual(167f, s.MinWidth);
            Assert.AreEqual(250f, s.MaxWidth);
        }

        [Test]
        public void Clamp_weighted_stretch_allowed_with_open_max()
        {
            var s = SizeSpec.Parse(size: null, width: "clamp(167, stretch*2, _)", height: null);
            Assert.IsTrue(s.IsClampedWidth);
            Assert.IsTrue(s.IsFlexibleWidth);
            Assert.AreEqual(2f, s.WeightWidth);
            Assert.AreEqual(167f, s.MinWidth);
            Assert.IsTrue(float.IsPositiveInfinity(s.MaxWidth));
        }

        [Test]
        public void Clamp_on_height_axis()
        {
            var s = SizeSpec.Parse(size: null, width: null, height: "clamp(200, 55%, 400)");
            Assert.IsFalse(s.HasWidth);
            Assert.IsFalse(s.IsClampedWidth);
            Assert.IsTrue(s.HasHeight);
            Assert.IsTrue(s.IsClampedHeight);
            Assert.IsTrue(s.IsFractionalHeight);
            Assert.AreEqual(0.55f, s.HeightFraction, 0.0001f);
            Assert.AreEqual(200f, s.MinHeight);
            Assert.AreEqual(400f, s.MaxHeight);
        }

        [Test]
        public void Non_clamp_values_default_to_open_bounds()
        {
            // Defaults are the identity for Mathf.Clamp so consumers never need to branch.
            var s = SizeSpec.Parse(size: null, width: "46%", height: "72");
            Assert.IsFalse(s.IsClampedWidth);
            Assert.IsFalse(s.IsClampedHeight);
            Assert.IsTrue(float.IsNegativeInfinity(s.MinWidth));
            Assert.IsTrue(float.IsPositiveInfinity(s.MaxWidth));
            Assert.IsTrue(float.IsNegativeInfinity(s.MinHeight));
            Assert.IsTrue(float.IsPositiveInfinity(s.MaxHeight));
        }

        [TestCase("clamp(_, 46%, _)", "both bounds open")]
        [TestCase("clamp(300, 46%, 250)", "min 300 > max 250")]
        [TestCase("clamp(-1, 46%, 250)", "finite and >= 0")]
        [TestCase("clamp(167, 46%, NaN)", "finite and >= 0")]
        [TestCase("clamp(167, 46%, Infinity)", "finite and >= 0")]
        [TestCase("clamp(167, 200, 250)", "middle must be")]
        [TestCase("clamp(167, stretch*2, 250)", "cannot be capped")]
        [TestCase("clamp(167, 46%)", "exactly 3 parts")]
        [TestCase("clamp(167, 46%, 250, 1)", "exactly 3 parts")]
        [TestCase("clamp 167 46% 250", "clamp(min, middle, max)")]
        [TestCase("Clamp(167, 46%, 250)", "clamp(min, middle, max)")]
        [TestCase("clamp(167, 46%, 250", "clamp(min, middle, max)")]
        [TestCase("clamp(167, native, 250)", "middle must be")]
        [TestCase("clamp(167, , 250)", "middle must be")]
        public void Throws_on_invalid_clamp(string bad, string messageContains)
        {
            var ex = Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(size: null, width: bad, height: null));
            StringAssert.Contains(messageContains, ex.Message);
        }

        [Test]
        public void Throws_when_clamp_used_in_size_attribute()
        {
            // size= stays numeric-only; the keyword validator must recognise clamp so the error
            // points at the rule ("use width=/height=") rather than at "x is not a number".
            var ex = Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(size: "clamp(167,46%,250)", width: null, height: null));
            StringAssert.Contains("numeric-only", ex.Message);
        }

        [Test]
        public void Clamp_on_anchor_stretched_axis_throws()
        {
            var spec = SizeSpec.Parse(size: null, width: "clamp(167, 46%, 250)", height: null);
            var anchor = new AnchorPreset(AnchorVertical.Top, AnchorHorizontal.Stretch);
            Assert.Throws<System.ArgumentException>(() => spec.ValidateAgainst(anchor));
        }

        // ── hug (spec 2026-08-31-hug-reveal-flip-checked-design §1.3) ──────────────────────

        [Test]
        public void Hug_sets_only_the_hug_flag()
        {
            var s = SizeSpec.Parse(size: null, width: "hug", height: null);
            Assert.IsTrue(s.HasWidth, "hug is an authored size — the axis counts as written");
            Assert.IsTrue(s.IsHugWidth);
            Assert.AreEqual(0f, s.Width);
            Assert.IsFalse(s.IsNativeWidth);
            Assert.IsFalse(s.IsFlexibleWidth);
            Assert.IsFalse(s.IsFractionalWidth);
            Assert.IsFalse(s.IsClampedWidth);
            Assert.IsFalse(s.HasHeight);
            Assert.IsFalse(s.IsHugHeight);
        }

        [Test]
        public void Hug_on_height_axis()
        {
            var s = SizeSpec.Parse(size: null, width: null, height: "hug");
            Assert.IsTrue(s.IsHugHeight);
            Assert.IsFalse(s.IsHugWidth);
        }

        [Test]
        public void Hug_axes_are_independent()
        {
            var s = SizeSpec.Parse(size: null, width: "hug", height: "200");
            Assert.IsTrue(s.IsHugWidth);
            Assert.IsFalse(s.IsHugHeight);
            Assert.AreEqual(200f, s.Height);
        }

        [Test]
        public void Clamp_hug_keeps_the_hug_flag_and_bounds()
        {
            // Same rule as % / stretch: clamp only ADDS bounds, the middle term keeps its own flag.
            var s = SizeSpec.Parse(size: null, width: null, height: "clamp(_, hug, 200)");
            Assert.IsTrue(s.IsHugHeight);
            Assert.IsTrue(s.IsClampedHeight);
            Assert.IsFalse(s.IsFractionalHeight);
            Assert.IsFalse(s.IsFlexibleHeight);
            Assert.IsTrue(float.IsNegativeInfinity(s.MinHeight));
            Assert.AreEqual(200f, s.MaxHeight);
        }

        [Test]
        public void Clamp_hug_floor_only()
        {
            var s = SizeSpec.Parse(size: null, width: null, height: "clamp(100, hug, _)");
            Assert.IsTrue(s.IsHugHeight);
            Assert.IsTrue(s.IsClampedHeight);
            Assert.AreEqual(100f, s.MinHeight);
            Assert.IsTrue(float.IsPositiveInfinity(s.MaxHeight));
        }

        [Test]
        public void Clamp_hug_both_bounds()
        {
            var s = SizeSpec.Parse(size: null, width: "clamp(100, hug, 200)", height: null);
            Assert.IsTrue(s.IsHugWidth);
            Assert.IsTrue(s.IsClampedWidth);
            Assert.AreEqual(100f, s.MinWidth);
            Assert.AreEqual(200f, s.MaxWidth);
        }

        [TestCase("clamp(_, hug, _)", "both bounds open")]
        [TestCase("clamp(300, hug, 250)", "min 300 > max 250")]
        public void Throws_on_invalid_clamp_hug(string bad, string messageContains)
        {
            var ex = Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(size: null, width: bad, height: null));
            StringAssert.Contains(messageContains, ex.Message);
        }

        [TestCase("hug*2")]
        [TestCase("Hug")]
        [TestCase("hug%")]
        [TestCase("hug ")]
        public void Throws_on_hug_lookalikes(string bad)
        {
            Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(size: null, width: bad, height: null));
        }

        [TestCase("hug")]
        [TestCase("hugxhug")]
        public void Throws_when_hug_used_in_size_attribute(string bad)
        {
            // size= stays numeric-only; the keyword validator must recognise hug so the error
            // points at the rule ("use width=/height=") rather than at "x is not a number".
            var ex = Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(size: bad, width: null, height: null));
            StringAssert.Contains("numeric-only", ex.Message);
        }

        [Test]
        public void Hug_on_anchor_stretched_axis_throws()
        {
            var spec = SizeSpec.Parse(size: null, width: "hug", height: null);
            var anchor = new AnchorPreset(AnchorVertical.Top, AnchorHorizontal.Stretch);
            Assert.Throws<System.ArgumentException>(() => spec.ValidateAgainst(anchor));
        }

        [Test]
        public void Hug_axis_survives_native_and_fallback_resolution()
        {
            // Both helpers key off IsNative / Has; a hug axis is neither native nor missing, so
            // neither may overwrite it with a measured number.
            var s = SizeSpec.Parse(size: null, width: "hug", height: null);

            var resolved = s.WithNativeResolved(new UnityEngine.Vector2(33f, 44f));
            Assert.IsTrue(resolved.IsHugWidth);
            Assert.AreEqual(0f, resolved.Width);

            var filled = s.WithFallbackForMissing(new UnityEngine.Vector2(33f, 44f));
            Assert.IsTrue(filled.IsHugWidth, "hug is authored — native fallback must not claim the axis");
            Assert.AreEqual(0f, filled.Width);
            Assert.AreEqual(44f, filled.Height, "the omitted axis still takes the native fallback");
        }
    }
}
