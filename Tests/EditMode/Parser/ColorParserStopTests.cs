using NUnit.Framework;
using PromptUGUI.Parser;

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
            Assert.IsTrue(ColorParser.TrySplitGradient("#fff 30%, #000 60%",
                out var top, out var bottom, out var topStop, out var bottomStop, out _));
            Assert.AreEqual("#fff", top);
            Assert.AreEqual("#000", bottom);
            Assert.AreEqual(0.3f, topStop.Value, 1e-5f);
            Assert.AreEqual(0.6f, bottomStop.Value, 1e-5f);
        }

        [Test]
        public void Gradient_OnlyTopStop_BottomStaysUnset()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("primary 70%,complement",
                out var top, out var bottom, out var topStop, out var bottomStop, out _));
            Assert.AreEqual("primary", top);
            Assert.AreEqual("complement", bottom);
            Assert.AreEqual(0.7f, topStop.Value, 1e-5f);
            Assert.IsFalse(bottomStop.HasValue);
        }

        [Test]
        public void EqualStops_AreAHardEdge_NotAnError()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("#fff 50%,#000 50%",
                out _, out _, out var topStop, out var bottomStop, out var err));
            Assert.IsNull(err);
            Assert.AreEqual(topStop.Value, bottomStop.Value, 1e-5f);
        }

        [Test]
        public void InvertedStops_Fail()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("#fff 70%,#000 30%",
                out _, out _, out _, out _, out var err));
            StringAssert.Contains("second stop position", err);
        }

        [Test]
        public void StopOnASingleColour_Fails()
        {
            // Nothing to move: a solid colour has no transition point.
            Assert.IsFalse(ColorParser.TrySplitGradient("#fff 70%",
                out _, out _, out _, out _, out var err));
            StringAssert.Contains("two-colour gradient", err);
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
