using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    /// <summary>
    /// The multi-stop, directed <c>ColorSpec</c> (spec 2026-09-17 §5.2): the direction vector and
    /// gradient-line length CSS defines, up to four stops evaluated per segment, and the whole-value
    /// transforms (multiply / alpha / opaque / desaturate) that must keep the ramp's shape.
    /// </summary>
    public class ColorSpecGradientTests
    {
        private static void AssertColor(Color expected, Color actual, float tol, string what)
        {
            Assert.AreEqual(expected.r, actual.r, tol, what + " (r)");
            Assert.AreEqual(expected.g, actual.g, tol, what + " (g)");
            Assert.AreEqual(expected.b, actual.b, tol, what + " (b)");
            Assert.AreEqual(expected.a, actual.a, tol, what + " (a)");
        }

        private static void AssertVec(Vector2 expected, Vector2 actual, string what)
        {
            Assert.AreEqual(expected.x, actual.x, 1e-4f, what + " (x)");
            Assert.AreEqual(expected.y, actual.y, 1e-4f, what + " (y)");
        }

        private static ColorSpec Three(GradientDirection dir)
            => ColorSpec.Gradient(dir,
                                  new[] { Color.red, Color.green, Color.blue },
                                  new[] { 0f, 0.5f, 1f },
                                  new[] { 1f, 1f });

        // ── direction vectors (§4.1) ────────────────────────────────────────────

        [TestCase(0f, 0f, 1f)]
        [TestCase(90f, 1f, 0f)]
        [TestCase(180f, 0f, -1f)]
        [TestCase(270f, -1f, 0f)]
        [TestCase(45f, 0.70710678f, 0.70710678f)]
        public void DirectionFor_Angle_IsCssSinCos(float deg, float x, float y)
        {
            var spec = ColorSpec.Gradient(Color.red, Color.blue).WithDirection(GradientDirection.Angle(deg));
            AssertVec(new Vector2(x, y), spec.DirectionFor(new Vector2(200f, 100f)), deg + "deg");
        }

        [Test]
        public void DirectionFor_Default_IsDown()
        {
            AssertVec(Vector2.down, ColorSpec.Gradient(Color.red, Color.blue).DirectionFor(new Vector2(30f, 70f)), "default");
        }

        [Test]
        public void DirectionFor_Corner_OnASquare_Is45DegreeFamily()
        {
            var d = 0.70710678f;
            var size = new Vector2(100f, 100f);
            AssertVec(new Vector2(d, -d), ColorSpec.Gradient(Color.red, Color.blue).WithDirection(GradientDirection.Corner(1, -1)).DirectionFor(size), "to bottom right");
            AssertVec(new Vector2(d, d), ColorSpec.Gradient(Color.red, Color.blue).WithDirection(GradientDirection.Corner(1, 1)).DirectionFor(size), "to top right");
            AssertVec(new Vector2(-d, -d), ColorSpec.Gradient(Color.red, Color.blue).WithDirection(GradientDirection.Corner(-1, -1)).DirectionFor(size), "to bottom left");
            AssertVec(new Vector2(-d, d), ColorSpec.Gradient(Color.red, Color.blue).WithDirection(GradientDirection.Corner(-1, 1)).DirectionFor(size), "to top left");
        }

        [Test]
        public void DirectionFor_Corner_OnAWideBox_IsPerpendicularToTheOtherDiagonal()
        {
            // CSS: the gradient line is perpendicular to the line through the two NEIGHBOURING corners.
            // For "to bottom right" on w×h those are top-right and bottom-left: (w, h) → normal (h, −w).
            var size = new Vector2(200f, 100f);
            var expected = new Vector2(100f, -200f).normalized;
            AssertVec(expected, ColorSpec.Gradient(Color.red, Color.blue).WithDirection(GradientDirection.Corner(1, -1)).DirectionFor(size), "to bottom right, 2:1");
        }

        [Test]
        public void LineLengthFor_ReachesTheCorners()
        {
            var spec = ColorSpec.Gradient(Color.red, Color.blue);
            Assert.AreEqual(100f, spec.LineLengthFor(new Vector2(200f, 100f), Vector2.down), 1e-4f, "vertical = height");
            Assert.AreEqual(200f, spec.LineLengthFor(new Vector2(200f, 100f), Vector2.right), 1e-4f, "horizontal = width");
            // 45° on a 100×100 box: |100·cos45| + |100·sin45| = 100·√2 — the diagonal.
            Assert.AreEqual(100f * Mathf.Sqrt(2f), spec.LineLengthFor(new Vector2(100f, 100f), new Vector2(1f, -1f).normalized), 1e-3f, "diagonal");
        }

        [Test]
        public void Corner_OnAWideBox_PutsTheFourCornersAt0_50_50_100()
        {
            // The whole point of the magic corner: the two other corners sit exactly on the 50% line.
            var size = new Vector2(200f, 100f);
            var spec = ColorSpec.Gradient(Color.red, Color.blue).WithDirection(GradientDirection.Corner(1, -1));
            var dir = spec.DirectionFor(size);
            var len = spec.LineLengthFor(size, dir);
            float S(Vector2 p) => (Vector2.Dot(p, dir) + len / 2f) / len;
            Assert.AreEqual(0f, S(new Vector2(-100f, 50f)), 1e-4f, "top-left");
            Assert.AreEqual(1f, S(new Vector2(100f, -50f)), 1e-4f, "bottom-right");
            Assert.AreEqual(0.5f, S(new Vector2(100f, 50f)), 1e-4f, "top-right");
            Assert.AreEqual(0.5f, S(new Vector2(-100f, -50f)), 1e-4f, "bottom-left");
        }

        // ── multi-stop evaluation (§4.2) ────────────────────────────────────────

        [Test]
        public void ThreeStops_EvaluatePerSegment()
        {
            var spec = Three(GradientDirection.Default);
            Assert.AreEqual(3, spec.Count);
            AssertColor(Color.red, spec.Evaluate(0f), 1e-5f, "start");
            AssertColor(Color.Lerp(Color.red, Color.green, 0.5f), spec.Evaluate(0.25f), 1e-5f, "first segment midpoint");
            AssertColor(Color.green, spec.Evaluate(0.5f), 1e-5f, "middle stop");
            AssertColor(Color.Lerp(Color.green, Color.blue, 0.5f), spec.Evaluate(0.75f), 1e-5f, "second segment midpoint");
            AssertColor(Color.blue, spec.Evaluate(1f), 1e-5f, "end");
            AssertColor(Color.blue, spec.Evaluate(1.5f), 1e-5f, "past the end clamps");
        }

        [Test]
        public void FourStops_WithMovedPositions_ClampOutside()
        {
            var spec = ColorSpec.Gradient(GradientDirection.Default,
                                          new[] { Color.red, Color.green, Color.blue, Color.white },
                                          new[] { 0.2f, 0.4f, 0.6f, 0.8f },
                                          new[] { 1f, 1f, 1f });
            Assert.AreEqual(4, spec.Count);
            AssertColor(Color.red, spec.Evaluate(0.1f), 1e-5f, "before first");
            AssertColor(Color.Lerp(Color.green, Color.blue, 0.5f), spec.Evaluate(0.5f), 1e-5f, "middle segment");
            AssertColor(Color.white, spec.Evaluate(0.9f), 1e-5f, "after last");
        }

        [Test]
        public void Hint_BendsOnlyItsOwnSegment()
        {
            var curve = ColorParser.StopCurveExponent(0f, 0.5f, 0.1f);   // half-mix at 10%: very early
            var spec = ColorSpec.Gradient(GradientDirection.Default,
                                          new[] { Color.white, Color.black, Color.white },
                                          new[] { 0f, 0.5f, 1f },
                                          new[] { curve, 1f });
            Assert.AreEqual(0.5f, spec.Evaluate(0.1f).r, 0.01f, "half and half at the hint");
            Assert.AreEqual(0.5f, spec.Evaluate(0.75f).r, 1e-5f, "second segment is still linear");
        }

        [Test]
        public void StartAndEnd_AreTheOuterStops()
        {
            var spec = Three(GradientDirection.Default);
            Assert.AreEqual(Color.red, spec.Start);
            Assert.AreEqual(Color.blue, spec.End);
            Assert.AreEqual(0f, spec.StartStop);
            Assert.AreEqual(1f, spec.EndStop);
            Assert.AreEqual(Color.green, spec.ColorAt(1));
            Assert.AreEqual(0.5f, spec.StopAt(1));
        }

        // ── HasStops truth table ────────────────────────────────────────────────

        [Test]
        public void HasStops_IgnoresDirection_CountsAThirdColour()
        {
            Assert.IsFalse(ColorSpec.Gradient(Color.red, Color.blue).HasStops, "plain");
            Assert.IsFalse(ColorSpec.Gradient(Color.red, Color.blue).WithDirection(GradientDirection.Angle(90f)).HasStops,
                           "a direction alone is still four corners' worth of gradient");
            Assert.IsTrue(ColorSpec.Gradient(Color.red, Color.blue, 0.3f, 1f).HasStops, "moved stop");
            Assert.IsTrue(ColorSpec.Gradient(Color.red, Color.blue, 0f, 1f, 2f).HasStops, "hint");
            Assert.IsTrue(Three(GradientDirection.Default).HasStops, "third colour");
        }

        // ── whole-value transforms keep the shape ───────────────────────────────

        [Test]
        public void Multiply_HitsEveryStop_KeepsShapeAndDirection()
        {
            var spec = Three(GradientDirection.Angle(90f)).Multiply(new Color(0.5f, 0.5f, 0.5f, 1f));
            AssertColor(new Color(0.5f, 0f, 0f, 1f), spec.ColorAt(0), 1e-5f, "c0");
            AssertColor(new Color(0f, 0.5f, 0f, 1f), spec.ColorAt(1), 1e-5f, "c1");
            AssertColor(new Color(0f, 0f, 0.5f, 1f), spec.ColorAt(2), 1e-5f, "c2");
            Assert.AreEqual(3, spec.Count);
            Assert.AreEqual(0.5f, spec.StopAt(1));
            Assert.AreEqual(90f, spec.Direction.AngleDeg);
        }

        [Test]
        public void WithAlpha_ReplacesEveryStopsAlpha()
        {
            var spec = Three(GradientDirection.Default).WithAlpha(0.25f);
            for (var i = 0; i < spec.Count; i++)
                Assert.AreEqual(0.25f, spec.ColorAt(i).a, 1e-6f, "stop " + i);
            Assert.AreEqual(3, spec.Count);
        }

        [Test]
        public void Opaque_ForcesAlphaOne_OnEveryStop()
        {
            var spec = ColorSpec.Gradient(new Color(1f, 0f, 0f, 0.2f), new Color(0f, 0f, 1f, 0f), 0.3f, 0.7f)
                .WithDirection(GradientDirection.Corner(1, -1))
                .Opaque();
            Assert.AreEqual(1f, spec.Start.a);
            Assert.AreEqual(1f, spec.End.a);
            Assert.AreEqual(0.3f, spec.StartStop, 1e-6f);
            Assert.AreEqual(GradientDirection.Kinds.Corner, spec.Direction.Kind);
        }

        [Test]
        public void Desaturate_GreysEveryStop_KeepsAlpha()
        {
            var spec = ColorSpec.Gradient(new Color(1f, 0f, 0f, 0.5f), new Color(0f, 0f, 1f, 1f)).Desaturate();
            Assert.AreEqual(spec.Start.r, spec.Start.g, 1e-6f);
            Assert.AreEqual(spec.Start.g, spec.Start.b, 1e-6f);
            Assert.AreEqual(0.5f, spec.Start.a, 1e-6f);
            Assert.AreEqual(spec.End.r, spec.End.b, 1e-6f);
        }

        [Test]
        public void AnyVisible_IsTrueWhenAnyStopHasAlpha()
        {
            Assert.IsFalse(ColorSpec.Gradient(Color.clear, Color.clear).AnyVisible);
            Assert.IsTrue(ColorSpec.Gradient(Color.clear, new Color(0f, 0f, 0f, 0.1f)).AnyVisible);
            Assert.IsTrue(ColorSpec.Solid(Color.white).AnyVisible);
            Assert.IsFalse(ColorSpec.Solid(Color.clear).AnyVisible);
        }

        // ── value semantics (the material cache key hashes these) ───────────────

        [Test]
        public void Equals_IsByValue_AcrossEveryField()
        {
            var a = Three(GradientDirection.Angle(90f));
            var b = Three(GradientDirection.Angle(90f));
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(a, Three(GradientDirection.Angle(45f)), "direction");
            Assert.AreNotEqual(a, Three(GradientDirection.Corner(1, -1)), "corner vs angle");
            Assert.AreNotEqual(ColorSpec.Gradient(Color.red, Color.blue), ColorSpec.Gradient(Color.red, Color.blue, 0.1f, 1f), "stop");
            Assert.AreNotEqual(ColorSpec.Solid(Color.red), ColorSpec.Gradient(Color.red, Color.red), "count");
        }

        [Test]
        public void Solid_UnusedSlotsReadAsTheOneColour()
        {
            var spec = ColorSpec.Solid(Color.red);
            Assert.AreEqual(1, spec.Count);
            Assert.AreEqual(Color.red, spec.Start);
            Assert.AreEqual(Color.red, spec.End);
            Assert.IsTrue(spec.Direction.IsDefault);
        }
    }
}
