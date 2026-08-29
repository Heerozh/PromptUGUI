using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    /// <summary>
    /// The fillet tail of the corner grammar (spec 2026-08-29 §4): <c>cut W[xH] rN</c>,
    /// <c>notch W[xH] rN</c> and <c>hexagon [W] rN</c>. <c>rN</c> is one glued token, the same rule
    /// as the <c>x</c> in <c>WxH</c>.
    ///
    /// <para>Everything the fillet must leave alone stays in <see cref="CornerTreatmentParserTests"/>
    /// and <see cref="RadiusParserTests"/>.</para>
    /// </summary>
    public class CornerFilletParserTests
    {
        private static void AssertAllCorners(RadiusSpec spec, CornerKind kind, float w, float h, float fillet)
        {
            foreach (var c in new[] { spec.TopLeftCorner, spec.TopRightCorner,
                                      spec.BottomRightCorner, spec.BottomLeftCorner })
            {
                Assert.AreEqual(kind, c.Kind);
                Assert.AreEqual(w, c.Width);
                Assert.AreEqual(h, c.Height);
                Assert.AreEqual(fillet, c.Fillet);
            }
        }

        // ---- cut / notch ----------------------------------------------------------------------

        [Test]
        public void Cut_WithFillet_CarriesTheRadius()
            => AssertAllCorners(RadiusParser.Parse("cut 16 r6"), CornerKind.Cut, 16f, 16f, 6f);

        [Test]
        public void Cut_WidthByHeight_WithFillet()
        {
            // The nav-tab shape from the spec's §1: a steep chamfer clamped to half height, softened.
            AssertAllCorners(RadiusParser.Parse("cut 16x99 r8"), CornerKind.Cut, 16f, 99f, 8f);
        }

        [Test]
        public void Notch_WithFillet()
            => AssertAllCorners(RadiusParser.Parse("notch 12 r4"), CornerKind.Notch, 12f, 12f, 4f);

        [Test]
        public void Fillet_Fractional_Parses_InvariantCulture()
            => Assert.AreEqual(2.5f, RadiusParser.Parse("cut 16 r2.5").TopLeftCorner.Fillet);

        [Test]
        public void Fillet_Zero_IsLegal_AndMeansSharp()
            => Assert.AreEqual(0f, RadiusParser.Parse("cut 16 r0").TopLeftCorner.Fillet);

        [Test]
        public void NoFillet_DefaultsToZero()
        {
            // Compatibility: every treatment written before fillets existed stays sharp.
            Assert.AreEqual(0f, RadiusParser.Parse("cut 16").TopLeftCorner.Fillet);
            Assert.AreEqual(0f, RadiusParser.Parse("notch 12x6").TopLeftCorner.Fillet);
            Assert.AreEqual(0f, RadiusParser.Parse("8").TopLeftCorner.Fillet);
        }

        [Test]
        public void PerCorner_EachFilletIsItsOwn()
        {
            var spec = RadiusParser.Parse("cut 16 r6, 8, notch 12 r4, cut 16");
            Assert.AreEqual(6f, spec.TopLeftCorner.Fillet);
            Assert.AreEqual(0f, spec.TopRightCorner.Fillet);
            Assert.AreEqual(4f, spec.BottomRightCorner.Fillet);
            Assert.AreEqual(0f, spec.BottomLeftCorner.Fillet);
            Assert.AreEqual(CornerKind.Notch, spec.BottomRightCorner.Kind);
        }

        [Test]
        public void Fillet_ToleratesWhitespaceAroundTokens()
            => Assert.AreEqual(6f, RadiusParser.Parse("  cut   16   r6 ").TopLeftCorner.Fillet);

        // ---- hexagon ----------------------------------------------------------------------------

        [Test]
        public void Hexagon_BareWithFillet_RoundsAllSixVertices()
        {
            var spec = RadiusParser.Parse("hexagon r6");
            Assert.AreEqual(PanelShape.Hexagon, spec.Shape);
            Assert.AreEqual(0f, spec.HexWidth, "no size means the tip reach stays 'auto'");
            foreach (var c in new[] { spec.TopLeftCorner, spec.TopRightCorner,
                                      spec.BottomRightCorner, spec.BottomLeftCorner })
                Assert.AreEqual(6f, c.Fillet, "the fillet rides in all four corners");
        }

        [Test]
        public void Hexagon_SizedWithFillet()
        {
            var spec = RadiusParser.Parse("hexagon 40 r6");
            Assert.AreEqual(PanelShape.Hexagon, spec.Shape);
            Assert.AreEqual(40f, spec.HexWidth);
            Assert.AreEqual(6f, spec.TopLeftCorner.Fillet);
        }

        [Test]
        public void Hexagon_NoFillet_StaysSharp()
            => Assert.AreEqual(0f, RadiusParser.Parse("hexagon 40").TopLeftCorner.Fillet);

        // ---- errors -----------------------------------------------------------------------------

        [Test]
        public void BareNumber_WithFillet_Throws()
        {
            // A round corner has nothing to fillet; the author most likely meant the radius alone.
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("16 r4"));
            StringAssert.Contains("fillet", ex.Message);
        }

        [Test]
        public void Pill_WithFillet_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("pill r4"));
            StringAssert.Contains("pill", ex.Message);
        }

        [TestCase("cut 16 r")]
        [TestCase("cut 16 r-2")]
        [TestCase("cut 16 rx")]
        [TestCase("cut 16 r4x2")]
        [TestCase("cut 16 rNaN")]
        [TestCase("notch 12 r")]
        public void MalformedFillet_Throws_NamingTheForm(string value)
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse(value));
            StringAssert.Contains("r", ex.Message);
            StringAssert.Contains("number", ex.Message);
        }

        [Test]
        public void FilletWithSpace_Throws_SayingToGlueIt()
        {
            // Same rule as the 'x' in WxH — and the same kind of targeted message, because
            // "too many parts" would send the author looking in the wrong place.
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("cut 16 r 8"));
            StringAssert.Contains("r8", ex.Message);
        }

        [TestCase("cut 16 R8")]
        [TestCase("cut 16 fillet 8")]
        [TestCase("cut 16 round 8")]
        public void UnknownTail_Throws_NamingTheOnlyLegalForm(string value)
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse(value));
            StringAssert.Contains("rN", ex.Message);
        }

        [TestCase("cut 16 r4 5")]
        [TestCase("cut 16 r4 r5")]
        public void TooManyParts_Throws(string value)
        {
            Assert.Throws<ParseException>(() => RadiusParser.Parse(value));
        }

        [Test]
        public void KeywordWithFilletButNoSize_Throws_AskingForTheSize()
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("cut r4"));
            StringAssert.Contains("size", ex.Message);
        }

        [TestCase("hexagon r6 40")]
        [TestCase("hexagon 40 r6 7")]
        [TestCase("hexagon 40x16 r6")]
        [TestCase("hexagon 40 R6")]
        public void Hexagon_BadTail_Throws(string value)
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse(value));
            StringAssert.Contains("hexagon", ex.Message);
        }

        [Test]
        public void FilletError_NamesTheCorner()
        {
            var ex = Assert.Throws<ParseException>(() => RadiusParser.Parse("0, 0, cut 8 r 2, 0"));
            StringAssert.Contains("bottom-right", ex.Message);
        }

        [Test]
        public void TryParse_ReportsFilletErrorsInsteadOfThrowing()
        {
            Assert.IsFalse(RadiusParser.TryParse("cut 16 rq", out _, out var error));
            Assert.IsNotNull(error);
        }
    }
}
