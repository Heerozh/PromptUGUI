using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    /// <summary>
    /// The direction segment of a gradient value (spec 2026-09-17 §3.1): CSS <c>&lt;N&gt;deg</c>
    /// or <c>to &lt;side-or-corner&gt;</c>, first segment only. Pure string work shared by the
    /// runtime resolver, the theme-definition parser and the UIXmlLint CLI.
    /// </summary>
    public class ColorParserDirectionTests
    {
        // ── TryParseDirection: one segment ──────────────────────────────────────

        [TestCase("0deg", 0f)]
        [TestCase("90deg", 90f)]
        [TestCase("180deg", 180f)]
        [TestCase("270deg", 270f)]
        [TestCase("45DEG", 45f)]
        [TestCase("22.5deg", 22.5f)]
        public void Angle_Parses(string raw, float expected)
        {
            Assert.IsTrue(ColorParser.LooksLikeDirection(raw));
            Assert.IsTrue(ColorParser.TryParseDirection(raw, out var dir, out var err));
            Assert.IsNull(err);
            Assert.AreEqual(GradientDirection.Kinds.Angle, dir.Kind);
            Assert.AreEqual(expected, dir.AngleDeg, 1e-5f);
        }

        [TestCase("360deg", 0f)]
        [TestCase("-90deg", 270f)]
        [TestCase("450deg", 90f)]
        public void Angle_NormalizesInto0To360(string raw, float expected)
        {
            Assert.IsTrue(ColorParser.TryParseDirection(raw, out var dir, out _));
            Assert.AreEqual(expected, dir.AngleDeg, 1e-4f);
        }

        [TestCase("to top", 0f)]
        [TestCase("to right", 90f)]
        [TestCase("to bottom", 180f)]
        [TestCase("to left", 270f)]
        [TestCase("TO Bottom", 180f)]
        [TestCase("to   right", 90f)]
        public void Side_IsAnAngle(string raw, float expected)
        {
            Assert.IsTrue(ColorParser.LooksLikeDirection(raw));
            Assert.IsTrue(ColorParser.TryParseDirection(raw, out var dir, out _));
            Assert.AreEqual(GradientDirection.Kinds.Angle, dir.Kind);
            Assert.AreEqual(expected, dir.AngleDeg, 1e-5f);
        }

        [TestCase("to top left", -1, 1)]
        [TestCase("to top right", 1, 1)]
        [TestCase("to bottom right", 1, -1)]
        [TestCase("to bottom left", -1, -1)]
        [TestCase("to left top", -1, 1)]
        [TestCase("to right bottom", 1, -1)]
        public void Corner_KeepsSigns_EitherOrder(string raw, int x, int y)
        {
            Assert.IsTrue(ColorParser.TryParseDirection(raw, out var dir, out _));
            Assert.AreEqual(GradientDirection.Kinds.Corner, dir.Kind);
            Assert.AreEqual(x, dir.CornerX);
            Assert.AreEqual(y, dir.CornerY);
        }

        [TestCase("to bottom right", 2)]
        [TestCase("to top left", 0)]
        [TestCase("to top right", 1)]
        [TestCase("to bottom left", 3)]
        public void Corner_IndexFollowsCssOrder(string raw, int index)
        {
            Assert.IsTrue(ColorParser.TryParseDirection(raw, out var dir, out _));
            Assert.AreEqual(index, dir.CornerIndex);
        }

        [TestCase("to up")]
        [TestCase("to top bottom")]
        [TestCase("to left right")]
        [TestCase("to top left right")]
        [TestCase("to")]
        [TestCase("to right #fff")]
        public void Malformed_Fails_WithTheGrammar(string raw)
        {
            Assert.IsTrue(ColorParser.LooksLikeDirection(raw));
            Assert.IsFalse(ColorParser.TryParseDirection(raw, out _, out var err));
            StringAssert.Contains("deg", err);
            StringAssert.Contains("to <side or corner>", err);
        }

        [TestCase("#fff")]
        [TestCase("primary")]
        [TestCase("70%")]
        [TestCase("tomato")]
        [TestCase("deg")]
        [TestCase("45")]
        public void Colours_DoNotLookLikeDirections(string raw)
        {
            Assert.IsFalse(ColorParser.LooksLikeDirection(raw));
        }

        [Test]
        public void Default_IsToBottom()
        {
            Assert.AreEqual(GradientDirection.Kinds.Angle, GradientDirection.Default.Kind);
            Assert.AreEqual(180f, GradientDirection.Default.AngleDeg);
            Assert.IsTrue(GradientDirection.Default.IsDefault);
            Assert.IsTrue(ColorParser.TryParseDirection("to bottom", out var d, out _));
            Assert.IsTrue(d.IsDefault);
            Assert.AreEqual(GradientDirection.Default, d);
        }

        // ── TrySplitGradient: the direction in context ──────────────────────────

        [Test]
        public void Gradient_DirectionComesOffTheFront()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("to right, #fff, #000", out var g, out var err));
            Assert.IsNull(err);
            Assert.AreEqual(2, g.Count);
            Assert.AreEqual("#fff", g.Colours[0]);
            Assert.AreEqual("#000", g.Colours[1]);
            Assert.AreEqual(90f, g.Direction.AngleDeg, 1e-5f);
        }

        [Test]
        public void Gradient_NoDirection_IsTheDefault()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("#fff, #000", out var g, out _));
            Assert.IsTrue(g.Direction.IsDefault);
        }

        [Test]
        public void Direction_WithOneColour_Fails()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("to right, #fff", out _, out var err));
            StringAssert.Contains("at least two colours", err);
        }

        [Test]
        public void Direction_NotFirst_Fails()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("#fff, to right, #000", out _, out var err));
            StringAssert.Contains("first segment", err);
        }

        [Test]
        public void Direction_GluedToAColour_Fails()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("45deg #fff, #000", out _, out var err));
            StringAssert.Contains("own comma-separated segment", err);
        }

        [Test]
        public void Direction_Malformed_FailsInContext()
        {
            Assert.IsFalse(ColorParser.TrySplitGradient("to up, #fff, #000", out _, out var err));
            StringAssert.Contains("to <side or corner>", err);
        }

        [Test]
        public void Direction_WithHintAndStops_AllSurvive()
        {
            Assert.IsTrue(ColorParser.TrySplitGradient("to bottom right, a 10%, 30%, b 50%, c", out var g, out _));
            Assert.AreEqual(GradientDirection.Kinds.Corner, g.Direction.Kind);
            Assert.AreEqual(3, g.Count);
            Assert.AreEqual(0.1f, g.Stops[0].Value, 1e-5f);
            Assert.AreEqual(0.3f, g.Hints[0].Value, 1e-5f);
            Assert.AreEqual(0.5f, g.Stops[1].Value, 1e-5f);
            Assert.IsFalse(g.Hints[1].HasValue);
            Assert.IsFalse(g.Stops[2].HasValue);
        }
    }
}
