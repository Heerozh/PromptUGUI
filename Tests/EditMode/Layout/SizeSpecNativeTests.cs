using NUnit.Framework;
using PromptUGUI.Layout;

namespace PromptUGUI.Tests.Layout {
    public class SizeSpecNativeTests {
        [Test]
        public void Size_native_sets_both_flags() {
            var s = SizeSpec.Parse("native", null, null);
            Assert.IsTrue(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
            Assert.IsTrue(s.IsNativeWidth);
            Assert.IsTrue(s.IsNativeHeight);
        }

        [Test]
        public void Width_native_only_axis_flagged() {
            var s = SizeSpec.Parse(null, "native", null);
            Assert.IsTrue(s.HasWidth);
            Assert.IsTrue(s.IsNativeWidth);
            Assert.IsFalse(s.HasHeight);
            Assert.IsFalse(s.IsNativeHeight);
        }

        [Test]
        public void Height_native_only_axis_flagged() {
            var s = SizeSpec.Parse(null, null, "native");
            Assert.IsFalse(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
            Assert.IsTrue(s.IsNativeHeight);
        }

        [Test]
        public void Numeric_size_does_not_set_native() {
            var s = SizeSpec.Parse("32x24", null, null);
            Assert.IsFalse(s.IsNativeWidth);
            Assert.IsFalse(s.IsNativeHeight);
            Assert.AreEqual(32f, s.Width);
            Assert.AreEqual(24f, s.Height);
        }

        [Test]
        public void WithNativeResolved_fills_axes_from_provided_size() {
            var s = SizeSpec.Parse("native", null, null)
                            .WithNativeResolved(new UnityEngine.Vector2(48, 32));
            Assert.AreEqual(48f, s.Width);
            Assert.AreEqual(32f, s.Height);
            Assert.IsFalse(s.IsNativeWidth);
            Assert.IsFalse(s.IsNativeHeight);
        }

        [Test]
        public void WithNativeResolved_only_replaces_native_axes() {
            var s = SizeSpec.Parse(null, "16", "native")
                            .WithNativeResolved(new UnityEngine.Vector2(99, 24));
            Assert.AreEqual(16f, s.Width);
            Assert.AreEqual(24f, s.Height);
        }

        [Test]
        public void Cannot_specify_both_size_and_width_with_native() {
            Assert.Throws<System.ArgumentException>(() =>
                SizeSpec.Parse("native", "32", null));
        }
    }
}
