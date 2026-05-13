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
    }
}
