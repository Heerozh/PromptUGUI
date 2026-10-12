using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser
{
    public class ScaleSyntaxTests
    {
        [TestCase("2x", 2)]
        [TestCase("1x", 1)]
        [TestCase("12x", 12)]
        public void TryParseDevice_accepts_positive_integers(string raw, int expected)
        {
            Assert.IsTrue(ScaleSyntax.TryParseDevice(raw, out var n));
            Assert.AreEqual(expected, n);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("x")]
        [TestCase("0x")]
        [TestCase("1.5x")]
        [TestCase("-2x")]
        [TestCase("2")]
        [TestCase("0.5r")]
        public void TryParseDevice_rejects_everything_else(string raw)
        {
            Assert.IsFalse(ScaleSyntax.TryParseDevice(raw, out _));
        }

        [TestCase("0.5r", 0.5f)]
        [TestCase("1r", 1f)]
        [TestCase("2.25r", 2.25f)]
        public void TryParseRelative_accepts_positive_floats(string raw, float expected)
        {
            Assert.IsTrue(ScaleSyntax.TryParseRelative(raw, out var r));
            Assert.AreEqual(expected, r, 1e-6f);
        }

        [TestCase(null)]
        [TestCase("r")]
        [TestCase("0r")]
        [TestCase("-1r")]
        [TestCase("2x")]
        [TestCase("0.5")]
        public void TryParseRelative_rejects_everything_else(string raw)
        {
            Assert.IsFalse(ScaleSyntax.TryParseRelative(raw, out _));
        }

        [TestCase("0.5", 0.5f)]
        [TestCase("2", 2f)]
        [TestCase("1.25", 1.25f)]
        public void TryParseMultiplier_accepts_positive_floats(string raw, float expected)
        {
            Assert.IsTrue(ScaleSyntax.TryParseMultiplier(raw, out var v));
            Assert.AreEqual(expected, v, 1e-6f);
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("2x")]
        [TestCase("0.5r")]
        [TestCase("1:0.5")]   // an <Animation scale="from:to">, not a node scale
        public void TryParseMultiplier_rejects_everything_else(string raw)
        {
            Assert.IsFalse(ScaleSyntax.TryParseMultiplier(raw, out _));
        }

        // The localScale a node gets at runtime: 'Nx' divides the canvas factor out, '<r>r' rounds
        // factor·r to an integer (>= 1) and divides it back out, a plain number is itself, anything
        // else is identity. A non-positive factor counts as 1.
        [TestCase("2x", 4f, 0.5f)]
        [TestCase("3x", 1.5f, 2f)]
        [TestCase("2x", 0f, 2f)]
        [TestCase("0.5r", 3f, 2f / 3f)]       // round(1.5) = 2
        [TestCase("0.5r", 2.9f, 1f / 2.9f)]   // round(1.45) = 1
        [TestCase("0.1r", 2f, 0.5f)]          // round(0.2) = 0 -> clamped to 1
        [TestCase("0.75", 3f, 0.75f)]
        [TestCase(null, 3f, 1f)]
        [TestCase("1:0.5", 3f, 1f)]
        public void LocalScale_matches_the_runtime_formula(string raw, float factor, float expected)
        {
            Assert.AreEqual(expected, ScaleSyntax.LocalScale(raw, factor), 1e-6f);
        }

        [TestCase("2x", true)]
        [TestCase("0.5r", true)]
        [TestCase("0.5", false)]
        [TestCase(null, false)]
        public void IsFactorDependent(string raw, bool expected)
        {
            Assert.AreEqual(expected, ScaleSyntax.IsFactorDependent(raw));
        }
    }
}
