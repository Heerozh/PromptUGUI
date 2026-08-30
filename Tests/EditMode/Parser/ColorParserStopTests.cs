using NUnit.Framework;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Parser
{
    /// <summary>
    /// The stop-position suffix on one gradient segment (<c>"#fff 70%"</c>) and on the whole
    /// gradient value. Pure string work shared by the runtime resolver, the theme-definition
    /// parser and the UIXmlLint CLI.
    /// </summary>
    public class ColorParserStopTests
    {
        // ── TrySplitStop: one segment ────────────────────────────────────────────

        [Test]
        public void NoSuffix_LeavesSegmentAlone()
        {
            Assert.IsTrue(ColorParser.TrySplitStop("#ffe08a", out var value, out var stop, out var err));
            Assert.AreEqual("#ffe08a", value);
            Assert.IsFalse(stop.HasValue);
            Assert.IsNull(err);
        }

        [Test]
        public void PercentSuffix_SplitsAndNormalizes()
        {
            Assert.IsTrue(ColorParser.TrySplitStop("#ffe08a 70%", out var value, out var stop, out _));
            Assert.AreEqual("#ffe08a", value);
            Assert.AreEqual(0.7f, stop.Value, 1e-5f);
        }

        [Test]
        public void PercentSuffix_SurvivesAlphaSuffix()
        {
            // The stop comes off first; what is left still carries its own /alpha.
            Assert.IsTrue(ColorParser.TrySplitStop("primary-darker/0.45 70%", out var value, out var stop, out _));
            Assert.AreEqual("primary-darker/0.45", value);
            Assert.AreEqual(0.7f, stop.Value, 1e-5f);
        }

        [TestCase("#fff 0%", 0f)]
        [TestCase("#fff 100%", 1f)]
        [TestCase("#fff 12.5%", 0.125f)]
        public void PercentSuffix_BoundsAndFractions(string raw, float expected)
        {
            Assert.IsTrue(ColorParser.TrySplitStop(raw, out _, out var stop, out _));
            Assert.AreEqual(expected, stop.Value, 1e-5f);
        }

        [Test]
        public void MissingPercentSign_Fails()
        {
            Assert.IsFalse(ColorParser.TrySplitStop("#fff 70", out _, out _, out var err));
            StringAssert.Contains("percentage", err);
        }

        [TestCase("#fff 120%")]
        [TestCase("#fff -5%")]
        public void OutOfRange_Fails(string raw)
        {
            Assert.IsFalse(ColorParser.TrySplitStop(raw, out _, out _, out var err));
            StringAssert.Contains("0%..100%", err);
        }

        [Test]
        public void TwoPositions_OnOneSegment_Fails()
        {
            Assert.IsFalse(ColorParser.TrySplitStop("#fff 70% 80%", out _, out _, out var err));
            StringAssert.Contains("one stop position", err);
        }

        [Test]
        public void PositionWithNoColour_Fails()
        {
            Assert.IsFalse(ColorParser.TrySplitStop("70%", out _, out _, out var err));
            StringAssert.Contains("colour before it", err);
        }

        // ── TrySplitGradient: the whole value ───────────────────────────────────

        [Test]
        public void Gradient_StopsComeOffBothSegments()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("#fff 30%, #000 60%", out var g, out _));
            Assert.AreEqual("#fff", g.Top);
            Assert.AreEqual("#000", g.Bottom);
            Assert.AreEqual(0.3f, g.TopStop.Value, 1e-5f);
            Assert.AreEqual(0.6f, g.BottomStop.Value, 1e-5f);
        }

        [Test]
        public void Gradient_OnlyTopStop_BottomStaysUnset()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("primary 70%,complement", out var g, out _));
            Assert.AreEqual("primary", g.Top);
            Assert.AreEqual("complement", g.Bottom);
            Assert.AreEqual(0.7f, g.TopStop.Value, 1e-5f);
            Assert.IsFalse(g.BottomStop.HasValue);
            Assert.AreEqual(1f, g.EffectiveBottomStop, 1e-5f);
        }

        [Test]
        public void EqualStops_AreAHardEdge_NotAnError()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("#fff 50%,#000 50%", out var g, out var err));
            Assert.IsNull(err);
            Assert.AreEqual(g.TopStop.Value, g.BottomStop.Value, 1e-5f);
        }

        [Test]
        public void InvertedStops_Fail()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("#fff 70%,#000 30%", out _, out var err));
            StringAssert.Contains("second stop position", err);
        }

        [Test]
        public void StopOnASingleColour_Fails()
        {
            // Nothing to move: a solid colour has no transition point.
            Assert.IsFalse(ColorParser.TrySplitGradient("#fff 70%", out _, out var err));
            StringAssert.Contains("two-colour gradient", err);
        }

        // ── colour hint: the middle bare percentage ─────────────────────────────

        [Test]
        public void Hint_SplitsOutOfTheMiddle()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("primary, 70%, complement", out var g, out _));
            Assert.AreEqual("primary", g.Top);
            Assert.AreEqual("complement", g.Bottom);
            Assert.AreEqual(0.7f, g.Hint.Value, 1e-5f);
        }

        [Test]
        public void Hint_AtTheMidpoint_IsTheLinearRamp()
        {
            // CSS puts the hint in the stops' coordinate space, so dead centre must come out as 1.
            Assert.IsTrue(ColorParser.TrySplitGradient("#fff, 50%, #000", out var g, out _));
            Assert.AreEqual(1f, g.CurveExponent, 1e-4f);
        }

        [Test]
        public void Hint_At70Percent_SolvesToHalfMixThere()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("#fff, 70%, #000", out var g, out _));
            Assert.AreEqual(0.5f, Mathf.Pow(0.7f, g.CurveExponent), 1e-3f);
        }

        [Test]
        public void Hint_ComposesWithStops_InTheirOwnSpace()
        {
            // Ramp runs 20%..100%; a hint at 60% is its midpoint, hence linear again.
            Assert.IsTrue(ColorParser.TrySplitGradient("#fff 20%, 60%, #000", out var g, out _));
            Assert.AreEqual(1f, g.CurveExponent, 1e-4f);
        }

        [Test]
        public void NoHint_IsExponentOne()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("#fff,#000", out var g, out _));
            Assert.IsFalse(g.Hint.HasValue);
            Assert.AreEqual(1f, g.CurveExponent, 1e-6f);
        }

        [Test]
        public void Hint_OutsideTheStops_Fails()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("#fff 40%, 20%, #000", out _, out var err));
            StringAssert.Contains("between the two stop positions", err);
        }

        [Test]
        public void Hint_WithNoSecondColour_Fails()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("#fff, 70%", out _, out var err));
            StringAssert.Contains("BETWEEN two colours", err);
        }

        [Test]
        public void ThreeColours_StillFail()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("#fff,#000,#111", out _, out var err));
            StringAssert.Contains("two colours", err);
        }

        [Test]
        public void FourSegments_Fail()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("#fff, 30%, 60%, #000", out _, out var err));
            StringAssert.Contains("two colours", err);
        }

        [Test]
        public void LegacyOverload_StripsStops_SoHexChecksStillSee_CleanLiterals()
        {
            // The 4-arg form is what the hex validators call; it must not hand them "#fff 70%".
            Assert.IsTrue(ColorParser.TrySplitGradient("#fff 30%,#000", out var top, out var bottom, out _));
            Assert.AreEqual("#fff", top);
            Assert.AreEqual("#000", bottom);
        }
    }
}
