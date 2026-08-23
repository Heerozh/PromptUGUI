using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    public class RadiusParserTests
    {
        [Test]
        public void SingleValue_AppliesToAllFourCorners()
        {
            var spec = RadiusParser.Parse("12");
            Assert.AreEqual(12f, spec.TopLeft);
            Assert.AreEqual(12f, spec.TopRight);
            Assert.AreEqual(12f, spec.BottomRight);
            Assert.AreEqual(12f, spec.BottomLeft);
            Assert.IsFalse(spec.IsPill);
        }

        [Test]
        public void FourValues_MapInCssClockwiseOrder()
        {
            // CSS border-radius: top-left, top-right, bottom-right, bottom-left.
            var spec = RadiusParser.Parse("1,2,3,4");
            Assert.AreEqual(1f, spec.TopLeft);
            Assert.AreEqual(2f, spec.TopRight);
            Assert.AreEqual(3f, spec.BottomRight);
            Assert.AreEqual(4f, spec.BottomLeft);
        }

        [Test]
        public void FourValues_TolerateWhitespace()
        {
            var spec = RadiusParser.Parse(" 16 , 16 , 0 , 0 ");
            Assert.AreEqual(16f, spec.TopLeft);
            Assert.AreEqual(0f, spec.BottomLeft);
        }

        [Test]
        public void Fractional_Parses_InvariantCulture()
        {
            Assert.AreEqual(2.5f, RadiusParser.Parse("2.5").TopLeft);
        }

        [Test]
        public void Pill_SetsSentinel_AndLeavesCornersZero()
        {
            // The numeric radius is resolved in the shader from the live rect, so nothing
            // size-dependent may leak into the parsed spec (that would break material sharing).
            var spec = RadiusParser.Parse("pill");
            Assert.IsTrue(spec.IsPill);
            Assert.AreEqual(0f, spec.TopLeft);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void NullOrEmpty_IsSquare_NotAnError(string value)
        {
            // A Variant can only change an attribute's value, never remove it — radius.desktop=""
            // is the only way back to square corners and must stay legal.
            var spec = RadiusParser.Parse(value);
            Assert.IsTrue(spec.IsZero);
        }

        [TestCase("1,2", "1 value")]
        [TestCase("1,2,3", "1 value")]
        [TestCase("1,2,3,4,5", "1 value")]
        public void WrongSegmentCount_Throws(string value, string expectedFragment)
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse(value));
            StringAssert.Contains(expectedFragment, ex.Message);
        }

        [Test]
        public void NegativeValue_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("-4"));
            StringAssert.Contains("negative", ex.Message);
        }

        [Test]
        public void NonNumericSegment_Throws_NamingTheCorner()
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("1,2,abc,4"));
            StringAssert.Contains("bottom-right", ex.Message);
        }

        [Test]
        public void EmptySegment_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("1,,3,4"));
            StringAssert.Contains("empty", ex.Message);
        }

        [Test]
        public void PillMixedWithNumbers_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("pill,0,0,0"));
            StringAssert.Contains("pill", ex.Message);
        }

        [Test]
        public void TryParse_ReportsErrorInsteadOfThrowing()
        {
            Assert.IsFalse(RadiusParser.TryParse("1,2", out _, out var error));
            Assert.IsNotNull(error);
        }
    }
}
