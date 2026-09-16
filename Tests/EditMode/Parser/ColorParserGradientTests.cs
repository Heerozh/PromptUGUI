using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    public class ColorParserGradientTests
    {
        [Test]
        public void NoComma_ReturnsSingleSegment()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("black/0.5", out var g, out var err));
            Assert.AreEqual(1, g.Count);
            Assert.IsFalse(g.IsGradient);
            Assert.AreEqual("black/0.5", g.Colours[0]);
            Assert.IsNull(err);
        }

        [Test]
        public void TwoSegments_SplitAndTrimmed()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("#ffe08a, #b8860b", out var g, out _));
            Assert.AreEqual(2, g.Count);
            Assert.AreEqual("#ffe08a", g.Colours[0]);
            Assert.AreEqual("#b8860b", g.Colours[1]);
        }

        [Test]
        public void ThreeAndFourColours_Split()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("a, b, c", out var g3, out _));
            Assert.AreEqual(3, g3.Count);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, g3.Colours);

            Assert.IsTrue(ColorParser.TrySplitGradient("a,b,c,d", out var g4, out _));
            Assert.AreEqual(4, g4.Count);
            CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, g4.Colours);
        }

        [Test]
        public void FiveColours_Fail()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("a,b,c,d,e", out _, out var err));
            StringAssert.Contains("2 to 4 colours", err);
        }

        [TestCase("a,")]
        [TestCase(",b")]
        [TestCase(",")]
        [TestCase("a,,b")]
        public void EmptySegment_Fails(string raw)
        {
            Assert.IsFalse(ColorParser.TrySplitGradient(raw, out _, out var err));
            StringAssert.Contains("empty", err);
        }

        [Test]
        public void Empty_IsSingleEmptySegment_HandledByCaller()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("", out var g, out _));
            Assert.AreEqual(1, g.Count);
            Assert.AreEqual("", g.Colours[0]);
        }

        // ── default stop positions (spec §3.3, CSS rules) ───────────────────────

        [Test]
        public void EffectiveStops_TwoColours_Are0And1()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("a,b", out var g, out _));
            CollectionAssert.AreEqual(new[] { 0f, 1f }, g.EffectiveStops());
        }

        [Test]
        public void EffectiveStops_ThreeColours_MiddleIsHalf()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("a,b,c", out var g, out _));
            var s = g.EffectiveStops();
            Assert.AreEqual(0f, s[0], 1e-6f);
            Assert.AreEqual(0.5f, s[1], 1e-6f);
            Assert.AreEqual(1f, s[2], 1e-6f);
        }

        [Test]
        public void EffectiveStops_UnsetMiddles_SpreadBetweenNearestSetNeighbours()
        {
            // CSS: A 0%, B 20%, C 40%, D 100%
            Assert.IsTrue(ColorParser.TrySplitGradient("a, b, c 40%, d", out var g, out _));
            var s = g.EffectiveStops();
            Assert.AreEqual(0f, s[0], 1e-6f);
            Assert.AreEqual(0.2f, s[1], 1e-6f);
            Assert.AreEqual(0.4f, s[2], 1e-6f);
            Assert.AreEqual(1f, s[3], 1e-6f);
        }

        [Test]
        public void EffectiveStops_FirstAndLastWritten_MiddlesSpreadInside()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("a 20%, b, c, d 80%", out var g, out _));
            var s = g.EffectiveStops();
            Assert.AreEqual(0.2f, s[0], 1e-6f);
            Assert.AreEqual(0.4f, s[1], 1e-6f);
            Assert.AreEqual(0.6f, s[2], 1e-6f);
            Assert.AreEqual(0.8f, s[3], 1e-6f);
        }

        [Test]
        public void DecreasingStops_Fail()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("a 60%, b 30%, c", out _, out var err));
            StringAssert.Contains("must not decrease", err);
        }

        [Test]
        public void DecreasingStops_ThroughAnUnsetMiddle_Fail()
        {
            // B auto-fills to 45%, which sits below A's 60%.
            Assert.IsFalse(ColorParser.TrySplitGradient("a 60%, b, c 30%", out _, out var err));
            StringAssert.Contains("must not decrease", err);
        }

        [Test]
        public void EqualStops_AreAHardEdge()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("a 50%, b 50%, c", out var g, out var err));
            Assert.IsNull(err);
            Assert.AreEqual(0.5f, g.EffectiveStops()[1], 1e-6f);
        }

        // ── hints, one per segment ──────────────────────────────────────────────

        [Test]
        public void Hints_OnePerSegment()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("a, 30%, b, c, 80%, d", out var g, out _));
            Assert.AreEqual(4, g.Count);
            Assert.AreEqual(3, g.Hints.Length);
            Assert.AreEqual(0.3f, g.Hints[0].Value, 1e-6f);
            Assert.IsFalse(g.Hints[1].HasValue);
            Assert.AreEqual(0.8f, g.Hints[2].Value, 1e-6f);
        }

        [Test]
        public void CurveExponents_OnePerSegment_LinearWithoutHint()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("a, 30%, b, c", out var g, out _));
            var e = g.CurveExponents();
            Assert.AreEqual(2, e.Length);
            Assert.AreNotEqual(1f, e[0]);
            Assert.AreEqual(1f, e[1], 1e-6f);
        }

        [TestCase("30%, a, b")]
        [TestCase("a, b, 30%")]
        [TestCase("a, 30%, 60%, b")]
        [TestCase("a, 30%")]
        public void Hint_NotBetweenTwoColours_Fails(string raw)
        {
            Assert.IsFalse(ColorParser.TrySplitGradient(raw, out _, out var err));
            StringAssert.Contains("BETWEEN two colours", err);
        }

        [Test]
        public void Hint_OutsideItsOwnSegment_Fails()
        {
            // Segment b..c runs 40%..100%; a hint at 20% belongs to nobody.
            Assert.IsFalse(ColorParser.TrySplitGradient("a, b 40%, 20%, c", out _, out var err));
            StringAssert.Contains("between the two stop positions", err);
        }

        [Test]
        public void Solid_HasNoHintsAndOneUnsetStop()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("a", out var g, out _));
            Assert.AreEqual(0, g.Hints.Length);
            Assert.AreEqual(1, g.Stops.Length);
            Assert.IsFalse(g.Stops[0].HasValue);
        }
    }
}
