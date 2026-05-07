using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Layout;

namespace PromptUGUI.Tests.Layout {
    public class SizeSpecTests {
        [Test]
        public void Parses_WxH() {
            var s = SizeSpec.Parse(size: "240x80", width: null, height: null);
            Assert.AreEqual(240f, s.Width);
            Assert.AreEqual(80f, s.Height);
            Assert.IsTrue(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
        }

        [Test]
        public void Parses_width_only() {
            var s = SizeSpec.Parse(size: null, width: "200", height: null);
            Assert.AreEqual(200f, s.Width);
            Assert.IsTrue(s.HasWidth);
            Assert.IsFalse(s.HasHeight);
        }

        [Test]
        public void Parses_height_only() {
            var s = SizeSpec.Parse(size: null, width: null, height: "64");
            Assert.AreEqual(64f, s.Height);
            Assert.IsFalse(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
        }

        [Test]
        public void Empty_when_all_null() {
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
            string size, string width, string height) {
            var spec = SizeSpec.Parse(size, width, height);
            var anchor = new AnchorPreset(v, h);
            Assert.Throws<System.ArgumentException>(() =>
                spec.ValidateAgainst(anchor));
        }

        [TestCase("WxH")]
        [TestCase("100x")]
        [TestCase("x100")]
        [TestCase("100")]
        public void Throws_on_malformed_size(string bad) {
            Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse(bad, null, null));
        }
    }
}
