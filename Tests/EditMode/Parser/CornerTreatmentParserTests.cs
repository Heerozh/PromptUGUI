using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    /// <summary>
    /// The corner-treatment half of the <c>radius</c> value grammar: <c>cut</c> / <c>notch</c>
    /// per corner, and <c>hexagon</c> alongside <c>pill</c> as a whole-shape sentinel.
    ///
    /// <para>Round-only values live in <see cref="RadiusParserTests"/> and stay there — that file
    /// is now also the compatibility guard for everything this grammar extension must not
    /// disturb.</para>
    /// </summary>
    public class CornerTreatmentParserTests
    {
        // ---- cut ----------------------------------------------------------------------------

        [Test]
        public void Cut_SingleSize_IsSymmetric_OnAllFourCorners()
        {
            var spec = RadiusParser.Parse("cut 16");
            foreach (var c in new[] { spec.TopLeftCorner, spec.TopRightCorner,
                                      spec.BottomRightCorner, spec.BottomLeftCorner })
            {
                Assert.AreEqual(CornerKind.Cut, c.Kind);
                Assert.AreEqual(16f, c.Width);
                Assert.AreEqual(16f, c.Height, "a single size is a 45° cut: height tracks width");
            }
        }

        [Test]
        public void Cut_WidthByHeight_KeepsAxesIndependent()
        {
            // The flat hexagon tip in the reference art is a wide, shallow cut — 45° cannot do it.
            var spec = RadiusParser.Parse("cut 24x16");
            Assert.AreEqual(24f, spec.TopLeftCorner.Width);
            Assert.AreEqual(16f, spec.TopLeftCorner.Height);
        }

        [Test]
        public void Cut_MixesPerCornerWithBareRoundValues()
        {
            var spec = RadiusParser.Parse("cut 16, 8, cut 16, 8");
            Assert.AreEqual(CornerKind.Cut, spec.TopLeftCorner.Kind);
            Assert.AreEqual(16f, spec.TopLeftCorner.Width);
            Assert.AreEqual(CornerKind.Round, spec.TopRightCorner.Kind);
            Assert.AreEqual(8f, spec.TopRightCorner.Width);
            Assert.AreEqual(CornerKind.Cut, spec.BottomRightCorner.Kind);
            Assert.AreEqual(CornerKind.Round, spec.BottomLeftCorner.Kind);
        }

        [Test]
        public void Cut_ToleratesExtraWhitespaceBetweenKeywordAndSize()
        {
            var spec = RadiusParser.Parse(" cut   16 , 0 , 0 , 0 ");
            Assert.AreEqual(CornerKind.Cut, spec.TopLeftCorner.Kind);
            Assert.AreEqual(16f, spec.TopLeftCorner.Width);
        }

        // ---- notch --------------------------------------------------------------------------

        [Test]
        public void Notch_SingleSize_IsSquare()
        {
            var spec = RadiusParser.Parse("notch 12");
            Assert.AreEqual(CornerKind.Notch, spec.TopLeftCorner.Kind);
            Assert.AreEqual(12f, spec.TopLeftCorner.Width);
            Assert.AreEqual(12f, spec.TopLeftCorner.Height);
        }

        [Test]
        public void Notch_TopTwoCornersOnly()
        {
            var spec = RadiusParser.Parse("notch 12, notch 12, 0, 0");
            Assert.AreEqual(CornerKind.Notch, spec.TopLeftCorner.Kind);
            Assert.AreEqual(CornerKind.Notch, spec.TopRightCorner.Kind);
            Assert.AreEqual(CornerKind.Round, spec.BottomRightCorner.Kind);
            Assert.AreEqual(0f, spec.BottomRightCorner.Width);
        }

        [Test]
        public void Notch_WidthByHeight_IsHorizontalThenDepth()
        {
            var spec = RadiusParser.Parse("notch 12x6");
            Assert.AreEqual(12f, spec.TopLeftCorner.Width);
            Assert.AreEqual(6f, spec.TopLeftCorner.Height);
        }

        // ---- hexagon ------------------------------------------------------------------------

        [Test]
        public void Hexagon_BareKeyword_LeavesTheTipSizeToTheShader()
        {
            // Same reasoning as pill: the tip height is half the live rect, so resolving anything
            // size-dependent here would give two same-styled panels different material keys.
            var spec = RadiusParser.Parse("hexagon");
            Assert.AreEqual(PanelShape.Hexagon, spec.Shape);
            Assert.AreEqual(0f, spec.HexWidth, "0 means 'auto' — the shader takes half the height");
            Assert.IsFalse(spec.IsPill);
        }

        [Test]
        public void Hexagon_WithSize_CarriesTheHorizontalReachOnly()
        {
            var spec = RadiusParser.Parse("hexagon 32");
            Assert.AreEqual(PanelShape.Hexagon, spec.Shape);
            Assert.AreEqual(32f, spec.HexWidth);
        }

        [Test]
        public void Hexagon_RejectsWidthByHeight_BecauseHeightIsAlwaysHalfTheRect()
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("hexagon 32x16"));
            StringAssert.Contains("hexagon", ex.Message);
        }

        [Test]
        public void Hexagon_MixedWithPerCornerValues_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("hexagon,0,0,0"));
            StringAssert.Contains("hexagon", ex.Message);
        }

        // ---- errors -------------------------------------------------------------------------

        [TestCase("bevel 16")]
        [TestCase("scoop 8")]
        [TestCase("CUT 16")]
        public void UnknownKeyword_Throws_ListingTheLegalOnes(string value)
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse(value));
            StringAssert.Contains("cut", ex.Message);
            StringAssert.Contains("notch", ex.Message);
        }

        [TestCase("cut")]
        [TestCase("notch")]
        public void KeywordWithoutSize_Throws(string value)
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse(value));
            StringAssert.Contains("size", ex.Message);
        }

        [TestCase("cut 16x")]
        [TestCase("cut x8")]
        [TestCase("cut 16x8x4")]
        [TestCase("cut axb")]
        public void MalformedSize_Throws(string value)
        {
            Assert.Throws<ParseException>(() => RadiusParser.Parse(value));
        }

        [Test]
        public void NegativeSizeComponent_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("cut 16x-2"));
            StringAssert.Contains("negative", ex.Message);
        }

        [Test]
        public void NonFiniteSizeComponent_Throws()
        {
            Assert.IsFalse(RadiusParser.TryParse("cut NaNx4", out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void TooManyTokensInASegment_Throws()
        {
            Assert.Throws<ParseException>(() => RadiusParser.Parse("cut 16 8"));
        }

        [Test]
        public void MalformedSegment_NamesTheCorner()
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("0, 0, bevel 4, 0"));
            StringAssert.Contains("bottom-right", ex.Message);
        }
    }
}
