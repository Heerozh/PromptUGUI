using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    /// <summary>
    /// <see cref="RadiusSpec"/> is public surface — custom control authors read it. The corner
    /// grammar extension widens the struct, and every member that shipped before has to keep
    /// meaning exactly what it meant: <c>TopLeft</c> and friends are the corner's horizontal size
    /// (for a round corner, its radius), and <c>IsPill</c> is still the pill sentinel.
    /// </summary>
    public class RadiusSpecCompatTests
    {
        [Test]
        public void LegacyConstructor_StillBuildsFourRoundCorners()
        {
            var spec = new RadiusSpec(1f, 2f, 3f, 4f);
            Assert.AreEqual(1f, spec.TopLeft);
            Assert.AreEqual(2f, spec.TopRight);
            Assert.AreEqual(3f, spec.BottomRight);
            Assert.AreEqual(4f, spec.BottomLeft);
            Assert.AreEqual(CornerKind.Round, spec.TopLeftCorner.Kind);
            Assert.AreEqual(PanelShape.None, spec.Shape);
        }

        [Test]
        public void RoundCorner_HeightMirrorsWidth()
        {
            // A round corner has no second axis — keeping height == width lets the shader read one
            // size pair per corner without branching on kind just to fetch it.
            var spec = RadiusParser.Parse("8");
            Assert.AreEqual(8f, spec.TopLeftCorner.Width);
            Assert.AreEqual(8f, spec.TopLeftCorner.Height);
        }

        [Test]
        public void PillStatic_AndIsPill_StillAgree()
        {
            Assert.IsTrue(RadiusSpec.Pill.IsPill);
            Assert.AreEqual(PanelShape.Pill, RadiusSpec.Pill.Shape);
            Assert.IsFalse(RadiusSpec.Zero.IsPill);
        }

        [Test]
        public void ZeroSizedTreatment_IsStillASquareCorner()
        {
            // A cut with no size removes nothing; IsZero exists to answer "is this square?", so it
            // must not be fooled by a keyword that happens to be present.
            Assert.IsTrue(RadiusParser.Parse("cut 0").IsZero);
            Assert.IsTrue(RadiusParser.Parse("notch 8x0").IsZero);
            Assert.IsFalse(RadiusParser.Parse("cut 8").IsZero);
            Assert.IsFalse(RadiusParser.Parse("hexagon").IsZero);
        }

        // ---- fillet (spec 2026-08-29) ----

        [Test]
        public void ThreeArgCornerSpec_AndFactories_HaveNoFillet()
        {
            // The fillet field is new; every construction path that predates it must read as sharp.
            Assert.AreEqual(0f, new CornerSpec(CornerKind.Cut, 8f, 8f).Fillet);
            Assert.AreEqual(0f, CornerSpec.Round(8f).Fillet);
            Assert.AreEqual(0f, CornerSpec.Square.Fillet);
            Assert.AreEqual(0f, RadiusSpec.Hexagon(32f).TopLeftCorner.Fillet);
            Assert.AreEqual(0f, new RadiusSpec(1f, 2f, 3f, 4f).TopLeftCorner.Fillet);
        }

        [Test]
        public void FourArgCornerSpec_CarriesTheFillet()
        {
            var c = new CornerSpec(CornerKind.Cut, 16f, 8f, 4f);
            Assert.AreEqual(CornerKind.Cut, c.Kind);
            Assert.AreEqual(16f, c.Width);
            Assert.AreEqual(8f, c.Height);
            Assert.AreEqual(4f, c.Fillet);
        }

        [Test]
        public void ZeroSizedTreatment_WithFillet_IsStillASquareCorner()
        {
            // A fillet needs a vertex to round; a treatment that removes nothing has none, and the
            // shader ignores r on a zero-sized corner for the same reason (spec §5.5).
            Assert.IsTrue(RadiusParser.Parse("cut 0 r4").IsZero);
            Assert.IsTrue(RadiusParser.Parse("cut 0 r4").TopLeftCorner.IsSquare);
        }
    }
}
