using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using UnityEngine;

namespace PromptUGUI.Tests.Layout {
    public class MarginResolverTests {

        [Test]
        public void Top_right_with_margin_16_and_size_240x80() {
            var anchor = AnchorPreset.Parse("top-right");
            var size   = SizeSpec.Parse("240x80", null, null);
            var r = MarginResolver.Resolve(anchor, size, "16");

            Assert.AreEqual(new Vector2(-16, -16), r.AnchoredPosition);
            Assert.AreEqual(new Vector2(240, 80), r.SizeDelta);
        }

        [Test]
        public void Top_left_with_margin_16_8() {
            var anchor = AnchorPreset.Parse("top-left");
            var size   = SizeSpec.Parse("240x80", null, null);
            var r = MarginResolver.Resolve(anchor, size, "16,8");

            Assert.AreEqual(new Vector2(8, -16), r.AnchoredPosition);
            Assert.AreEqual(new Vector2(240, 80), r.SizeDelta);
        }

        [Test]
        public void Center_with_no_margin() {
            var anchor = AnchorPreset.Parse("center");
            var size   = SizeSpec.Parse("400x300", null, null);
            var r = MarginResolver.Resolve(anchor, size, null);

            Assert.AreEqual(new Vector2(0, 0), r.AnchoredPosition);
            Assert.AreEqual(new Vector2(400, 300), r.SizeDelta);
        }

        [Test]
        public void Top_stretch_with_height_64_and_horizontal_margin_8() {
            var anchor = AnchorPreset.Parse("top-stretch");
            var size   = SizeSpec.Parse(null, null, "64");
            var r = MarginResolver.Resolve(anchor, size, "0,8,_,8");

            Assert.AreEqual(new Vector2(0, 0), r.AnchoredPosition);
            Assert.AreEqual(new Vector2(-16, 64), r.SizeDelta);
        }

        [Test]
        public void Stretch_all_with_margin_0() {
            var anchor = AnchorPreset.Parse("stretch");
            var size   = SizeSpec.Parse(null, null, null);
            var r = MarginResolver.Resolve(anchor, size, null);

            Assert.AreEqual(new Vector2(0, 0), r.AnchoredPosition);
            Assert.AreEqual(new Vector2(0, 0), r.SizeDelta);
        }
    }
}
