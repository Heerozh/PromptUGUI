using NUnit.Framework;
using PromptUGUI.IR;

namespace PromptUGUI.Tests.Layout {
    public class AnchorPresetTests {
        [TestCase("top-left",       AnchorVertical.Top,    AnchorHorizontal.Left)]
        [TestCase("bottom-right",   AnchorVertical.Bottom, AnchorHorizontal.Right)]
        [TestCase("stretch-left",   AnchorVertical.Stretch,AnchorHorizontal.Left)]
        [TestCase("top-stretch",    AnchorVertical.Top,    AnchorHorizontal.Stretch)]
        public void Parses_v_h_form(string s, AnchorVertical v, AnchorHorizontal h) {
            var a = AnchorPreset.Parse(s);
            Assert.AreEqual(v, a.V);
            Assert.AreEqual(h, a.H);
        }

        [Test]
        public void Center_alias_expands_to_center_center() {
            var a = AnchorPreset.Parse("center");
            Assert.AreEqual(AnchorVertical.Center, a.V);
            Assert.AreEqual(AnchorHorizontal.Center, a.H);
        }

        [Test]
        public void Stretch_alias_expands_to_stretch_stretch() {
            var a = AnchorPreset.Parse("stretch");
            Assert.AreEqual(AnchorVertical.Stretch, a.V);
            Assert.AreEqual(AnchorHorizontal.Stretch, a.H);
        }

        [Test]
        public void Fill_alias_equals_stretch() {
            Assert.AreEqual(AnchorPreset.Parse("stretch"), AnchorPreset.Parse("fill"));
        }

        [TestCase("")]
        [TestCase("topleft")]
        [TestCase("up-left")]
        [TestCase("top-middle")]
        public void Throws_on_invalid(string bad) {
            Assert.Throws<System.ArgumentException>(() => AnchorPreset.Parse(bad));
        }
    }
}
