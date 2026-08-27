using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    /// <summary>
    /// The <c>&lt;Decor&gt;</c> value grammar (decor spec §4). Pure C#, so these run without a
    /// Screen — and the UIXmlLint CLI compiles the same parser, which is why every message here
    /// has to read well on its own.
    /// </summary>
    public class DecorParserTests
    {
        // ---- kind ----

        [TestCase("bracket", DecorKind.Bracket)]
        [TestCase("tick", DecorKind.Tick)]
        [TestCase("line", DecorKind.Line)]
        [TestCase("sprite", DecorKind.Sprite)]
        [TestCase("none", DecorKind.None)]
        public void Kind_ParsesEveryKeyword(string value, DecorKind expected)
        {
            Assert.AreEqual(expected, DecorParser.ParseKind(value));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void Kind_NullOrEmpty_IsNone_NotAnError(string value)
        {
            // Same doctrine as radius="": a Variant can only override a value, never remove the
            // attribute, so the empty string has to stay a legal way back to "draws nothing".
            Assert.AreEqual(DecorKind.None, DecorParser.ParseKind(value));
        }

        [TestCase("Bracket")]
        [TestCase("BRACKET")]
        [TestCase("brackets")]
        public void Kind_UnknownOrMiscased_ErrorsAndListsLegalValues(string value)
        {
            Assert.IsFalse(DecorParser.TryParseKind(value, out _, out var error));
            StringAssert.Contains("bracket", error);
            StringAssert.Contains("tick", error);
            StringAssert.Contains("line", error);
            StringAssert.Contains("sprite", error);
        }

        // ---- at ----

        [Test]
        public void At_SingleToken_Parses()
        {
            var slots = DecorParser.ParseAt("bottom");
            CollectionAssert.AreEqual(new[] { DecorSlot.Bottom }, slots);
        }

        [Test]
        public void At_CommaList_KeepsAuthorOrder_AndToleratesWhitespace()
        {
            var slots = DecorParser.ParseAt(" top-left , bottom-right ");
            CollectionAssert.AreEqual(new[] { DecorSlot.TopLeft, DecorSlot.BottomRight }, slots);
        }

        [TestCase("top-left", DecorSlot.TopLeft)]
        [TestCase("top-right", DecorSlot.TopRight)]
        [TestCase("bottom-right", DecorSlot.BottomRight)]
        [TestCase("bottom-left", DecorSlot.BottomLeft)]
        [TestCase("top", DecorSlot.Top)]
        [TestCase("bottom", DecorSlot.Bottom)]
        [TestCase("left", DecorSlot.Left)]
        [TestCase("right", DecorSlot.Right)]
        public void At_EveryTokenIsAnchorPresetVocabulary(string token, DecorSlot expected)
        {
            // Deliberately the same words as anchor= — one vocabulary for the author to remember,
            // not two (decor spec §10.5).
            CollectionAssert.AreEqual(new[] { expected }, DecorParser.ParseAt(token));
        }

        [TestCase(null)]
        [TestCase("")]
        public void At_NullOrEmpty_MeansDefaultForKind_NotAnError(string value)
        {
            Assert.IsTrue(DecorParser.TryParseAt(value, out var slots, out _));
            Assert.IsNull(slots, "empty at= must defer to the kind's default, not resolve to a list");
        }

        [Test]
        public void At_UnknownToken_Errors()
        {
            Assert.IsFalse(DecorParser.TryParseAt("middle", out _, out var error));
            StringAssert.Contains("middle", error);
            StringAssert.Contains("top-left", error);
        }

        [Test]
        public void At_DuplicateToken_Errors()
        {
            // Two instances in the same slot would z-fight and double the glow; always a mistake.
            Assert.IsFalse(DecorParser.TryParseAt("top-left,top-left", out _, out var error));
            StringAssert.Contains("top-left", error);
        }

        // ---- extent ----

        [Test]
        public void Extent_SingleValue_SquaresItself()
        {
            var extent = DecorParser.ParseExtent("12");
            Assert.AreEqual(12f, extent.Width);
            Assert.AreEqual(12f, extent.Height);
            Assert.IsFalse(extent.IsNative);
            Assert.IsFalse(extent.IsFraction);
        }

        [Test]
        public void Extent_WxH_ParsesBothAxes()
        {
            var extent = DecorParser.ParseExtent("10x6");
            Assert.AreEqual(10f, extent.Width);
            Assert.AreEqual(6f, extent.Height);
        }

        [Test]
        public void Extent_Percent_IsFractionOfEdge()
        {
            var extent = DecorParser.ParseExtent("60%");
            Assert.IsTrue(extent.IsFraction);
            Assert.AreEqual(0.6f, extent.Width, 1e-5f);
        }

        [Test]
        public void Extent_Native_IsSentinel()
        {
            var extent = DecorParser.ParseExtent("native");
            Assert.IsTrue(extent.IsNative);
        }

        [TestCase("12x")]
        [TestCase("x6")]
        [TestCase("1x2x3")]
        [TestCase("abc")]
        public void Extent_Malformed_Errors(string value)
        {
            Assert.IsFalse(DecorParser.TryParseExtent(value, out _, out var error));
            Assert.IsNotNull(error);
        }

        [TestCase("-4")]
        [TestCase("10x-2")]
        [TestCase("NaN")]
        [TestCase("Infinity")]
        public void Extent_NegativeOrNonFinite_Errors(string value)
        {
            Assert.IsFalse(DecorParser.TryParseExtent(value, out _, out var error));
            Assert.IsNotNull(error);
        }

        [TestCase(null)]
        [TestCase("")]
        public void Extent_NullOrEmpty_MeansDefaultForKind_NotAnError(string value)
        {
            Assert.IsTrue(DecorParser.TryParseExtent(value, out var extent, out _));
            Assert.IsFalse(extent.HasValue, "empty extent= must defer to the kind's default");
        }

        // ---- cross-attribute validation ----

        [Test]
        public void Validate_BracketOnAnEdge_Errors()
        {
            var ok = DecorParser.TryValidate(DecorKind.Bracket, new[] { DecorSlot.Bottom },
                                             default, out var error);
            Assert.IsFalse(ok);
            StringAssert.Contains("bracket", error);
            StringAssert.Contains("bottom", error);
        }

        [Test]
        public void Validate_TickOnACorner_Errors()
        {
            var ok = DecorParser.TryValidate(DecorKind.Tick, new[] { DecorSlot.TopLeft },
                                             default, out var error);
            Assert.IsFalse(ok);
            StringAssert.Contains("tick", error);
        }

        [Test]
        public void Validate_SpriteTakesCornersAndEdgesAlike()
        {
            Assert.IsTrue(DecorParser.TryValidate(
                DecorKind.Sprite, new[] { DecorSlot.TopLeft, DecorSlot.Bottom }, default, out _));
        }

        [Test]
        public void Validate_PercentOutsideLine_Errors()
        {
            var extent = DecorParser.ParseExtent("50%");
            Assert.IsFalse(DecorParser.TryValidate(DecorKind.Bracket, null, extent, out var error));
            StringAssert.Contains("%", error);
        }

        [Test]
        public void Validate_NativeOutsideSprite_Errors()
        {
            var extent = DecorParser.ParseExtent("native");
            Assert.IsFalse(DecorParser.TryValidate(DecorKind.Line, null, extent, out var error));
            StringAssert.Contains("native", error);
        }

        [Test]
        public void Validate_NativeOnSprite_IsFine()
        {
            var extent = DecorParser.ParseExtent("native");
            Assert.IsTrue(DecorParser.TryValidate(DecorKind.Sprite, null, extent, out _));
        }

        // ---- defaults ----

        [Test]
        public void DefaultSlots_BracketAndSprite_AreAllFourCorners()
        {
            CollectionAssert.AreEquivalent(
                new[] { DecorSlot.TopLeft, DecorSlot.TopRight, DecorSlot.BottomRight, DecorSlot.BottomLeft },
                DecorParser.DefaultSlots(DecorKind.Bracket));
            CollectionAssert.AreEquivalent(
                new[] { DecorSlot.TopLeft, DecorSlot.TopRight, DecorSlot.BottomRight, DecorSlot.BottomLeft },
                DecorParser.DefaultSlots(DecorKind.Sprite));
        }

        [Test]
        public void DefaultSlots_TickAndLine_AreBottom()
        {
            CollectionAssert.AreEqual(new[] { DecorSlot.Bottom }, DecorParser.DefaultSlots(DecorKind.Tick));
            CollectionAssert.AreEqual(new[] { DecorSlot.Bottom }, DecorParser.DefaultSlots(DecorKind.Line));
        }

        [Test]
        public void DefaultSize_MatchesTheSpecTable()
        {
            Assert.AreEqual(12f, DecorParser.DefaultExtent(DecorKind.Bracket).Width);
            Assert.AreEqual(10f, DecorParser.DefaultExtent(DecorKind.Tick).Width);
            Assert.AreEqual(6f, DecorParser.DefaultExtent(DecorKind.Tick).Height);
            Assert.IsTrue(DecorParser.DefaultExtent(DecorKind.Line).IsFraction);
            Assert.AreEqual(1f, DecorParser.DefaultExtent(DecorKind.Line).Width, 1e-5f);
            Assert.IsTrue(DecorParser.DefaultExtent(DecorKind.Sprite).IsNative);
        }

        [Test]
        public void IsCornerSlot_SeparatesCornersFromEdges()
        {
            Assert.IsTrue(DecorParser.IsCornerSlot(DecorSlot.TopLeft));
            Assert.IsTrue(DecorParser.IsCornerSlot(DecorSlot.BottomRight));
            Assert.IsFalse(DecorParser.IsCornerSlot(DecorSlot.Bottom));
            Assert.IsFalse(DecorParser.IsCornerSlot(DecorSlot.Left));
        }
    }
}
