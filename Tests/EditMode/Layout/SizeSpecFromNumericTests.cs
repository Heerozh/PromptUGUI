using NUnit.Framework;
using PromptUGUI.Layout;

namespace PromptUGUI.Tests.EditMode.Layout
{
    public class SizeSpecFromNumericTests
    {
        [Test]
        public void FromNumeric_sets_both_axes_as_explicit_numeric()
        {
            var s = SizeSpec.FromNumeric(120f, 44f);
            Assert.IsTrue(s.HasWidth);
            Assert.IsTrue(s.HasHeight);
            Assert.AreEqual(120f, s.Width);
            Assert.AreEqual(44f, s.Height);
            Assert.IsFalse(s.IsNativeWidth);
            Assert.IsFalse(s.IsNativeHeight);
            Assert.IsFalse(s.IsFlexibleWidth);
            Assert.IsFalse(s.IsFlexibleHeight);
            Assert.IsFalse(s.IsFractionalWidth);
            Assert.IsFalse(s.IsFractionalHeight);
        }
    }
}
