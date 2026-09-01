using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// <c>ColorSpec.Evaluate</c> — the CPU twin of the shader's <c>PuguiFillRamp</c>. The two have to
    /// agree row for row, or the same colour token would change over at different pixels on a
    /// &lt;Frame&gt; (per fragment) and on an &lt;Image&gt; (per vertex). Spec 2026-09-01 VGS §4.1.
    /// </summary>
    public class ColorSpecEvaluateTests
    {
        private static void AssertColor(Color expected, Color actual, float tol, string what)
        {
            Assert.AreEqual(expected.r, actual.r, tol, what + " (r)");
            Assert.AreEqual(expected.g, actual.g, tol, what + " (g)");
            Assert.AreEqual(expected.b, actual.b, tol, what + " (b)");
            Assert.AreEqual(expected.a, actual.a, tol, what + " (a)");
        }

        [Test]
        public void Solid_IsFlatEverywhere()
        {
            var spec = ColorSpec.Solid(Color.red);
            AssertColor(Color.red, spec.Evaluate(0f), 1e-5f, "top");
            AssertColor(Color.red, spec.Evaluate(0.3f), 1e-5f, "middle");
            AssertColor(Color.red, spec.Evaluate(1f), 1e-5f, "bottom");
        }

        [Test]
        public void PlainGradient_IsTheLinearLerpTodaysVerticesDraw()
        {
            // No stops: every vertex must land exactly where the current per-vertex path puts it,
            // so switching an existing document to the new evaluator changes nothing (VGS-D1).
            var spec = ColorSpec.Gradient(Color.red, Color.blue);
            foreach (var s in new[] { 0f, 0.25f, 0.5f, 1f })
                AssertColor(Color.Lerp(Color.red, Color.blue, s), spec.Evaluate(s), 1e-5f, "s=" + s);
        }

        [Test]
        public void MovedStops_ClampOutsideAndRemapBetween()
        {
            var spec = ColorSpec.Gradient(Color.red, Color.blue, 0.3f, 0.6f);
            AssertColor(Color.red, spec.Evaluate(0f), 1e-5f, "above the top stop");
            AssertColor(Color.red, spec.Evaluate(0.1f), 1e-5f, "above the top stop");
            AssertColor(Color.blue, spec.Evaluate(0.9f), 1e-5f, "below the bottom stop");
            AssertColor(Color.blue, spec.Evaluate(1f), 1e-5f, "below the bottom stop");
            // 0.45 is halfway through the 0.3 .. 0.6 band.
            AssertColor(Color.Lerp(Color.red, Color.blue, 0.5f), spec.Evaluate(0.45f), 1e-5f, "band midpoint");
        }

        [Test]
        public void HardEdge_FlipsAtTheStop()
        {
            var spec = ColorSpec.Gradient(Color.red, Color.blue, 0.5f, 0.5f);
            AssertColor(Color.red, spec.Evaluate(0.499f), 1e-5f, "just above");
            AssertColor(Color.blue, spec.Evaluate(0.501f), 1e-5f, "just below");
        }

        [Test]
        public void Hint_MixesHalfAndHalfAtTheHint()
        {
            var curve = ColorParser.StopCurveExponent(0f, 1f, 0.3f);
            var spec = ColorSpec.Gradient(Color.white, Color.black, 0f, 1f, curve);
            Assert.AreEqual(0.5f, spec.Evaluate(0.3f).r, 0.01f, "half and half at the hint");
            Assert.Greater(spec.Evaluate(0.15f).r, 0.5f, "still mostly white before the hint");
            Assert.Less(spec.Evaluate(0.6f).r, 0.5f, "mostly black after it");
        }

        [Test]
        public void Hint_LivesInStopSpace()
        {
            // CSS puts the hint in the same coordinate space as the stops, so a hint exactly midway
            // between them is the linear case — and the whole ramp still sits inside the band.
            var curve = ColorParser.StopCurveExponent(0.2f, 0.8f, 0.5f);
            var spec = ColorSpec.Gradient(Color.white, Color.black, 0.2f, 0.8f, curve);
            Assert.AreEqual(0.5f, spec.Evaluate(0.5f).r, 0.01f, "midway hint is linear");
            AssertColor(Color.white, spec.Evaluate(0.1f), 1e-5f, "above the band");
            AssertColor(Color.black, spec.Evaluate(0.9f), 1e-5f, "below the band");
        }

        [Test]
        public void Evaluate_MatchesTheShaderRampFormula()
        {
            // Transcription guard: the same arithmetic PuguiFillRamp runs per fragment.
            var top = new Color(0.9f, 0.1f, 0.2f, 1f);
            var bottom = new Color(0.1f, 0.4f, 0.8f, 0.25f);
            var spec = ColorSpec.Gradient(top, bottom, 0.25f, 0.75f, 2f);
            for (var i = 0; i <= 10; i++)
            {
                var s = i / 10f;
                var u = Mathf.Clamp01((s - 0.25f) / Mathf.Max(0.75f - 0.25f, 1e-4f));
                u = Mathf.Pow(u, 2f);
                AssertColor(Color.Lerp(top, bottom, u), spec.Evaluate(s), 1e-5f, "s=" + s);
            }
        }
    }
}
