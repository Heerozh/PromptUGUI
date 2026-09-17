using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    /// <summary>
    /// <c>hazeDensity</c> (spec 2026-09-17 haze H-D7): the one knob between sparse light patches
    /// (0) and the raw cloud field (1). Pure C#, shared by the runtime setters and the CLI, so the
    /// accepted range and the wording of each error can never drift between the two.
    /// </summary>
    public class HazeDensityAttrParserTests
    {
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void Empty_IsTheDefault(string value)
        {
            // A Variant can only overwrite a value, never remove the attribute, so "" has to be
            // the way back to the default density.
            Assert.IsTrue(HazeDensityAttrParser.TryParse(value, out var d, out var error));
            Assert.IsNull(error);
            Assert.AreEqual(HazeDensityAttrParser.Default, d, 0.0001f);
            Assert.AreEqual(0.5f, HazeDensityAttrParser.Default, 0.0001f,
                "the default is the reference's thin mist, halfway between patches and the raw field");
        }

        [TestCase("0", 0f)]
        [TestCase("1", 1f)]
        [TestCase(" 0.5 ", 0.5f)]
        [TestCase("0.25", 0.25f)]
        [TestCase("1e-1", 0.1f)]
        public void Numbers_Parse_InvariantCulture(string value, float expected)
        {
            Assert.IsTrue(HazeDensityAttrParser.TryParse(value, out var d, out var error), error);
            Assert.AreEqual(expected, d, 0.0001f);
        }

        [TestCase("soft")]
        [TestCase("50%")]
        [TestCase("0,5")]
        public void NotANumber_IsRejected(string value)
        {
            Assert.IsFalse(HazeDensityAttrParser.TryParse(value, out _, out var error));
            StringAssert.Contains("hazeDensity", error);
            StringAssert.Contains("expected a number", error);
        }

        [TestCase("NaN")]
        [TestCase("Infinity")]
        public void NonFinite_IsRejected(string value)
        {
            Assert.IsFalse(HazeDensityAttrParser.TryParse(value, out _, out var error));
            StringAssert.Contains("finite", error);
        }

        [TestCase("-0.1")]
        [TestCase("1.5")]
        [TestCase("2")]
        public void OutsideZeroToOne_IsRejected(string value)
        {
            Assert.IsFalse(HazeDensityAttrParser.TryParse(value, out _, out var error));
            StringAssert.Contains("0", error);
            StringAssert.Contains("1", error);
        }

        [Test]
        public void Parse_ThrowsTheSameMessage()
        {
            var ex = Assert.Throws<ParseException>(() => HazeDensityAttrParser.Parse("2"));
            HazeDensityAttrParser.TryParse("2", out _, out var error);
            Assert.AreEqual(error, ex.Message);
        }
    }
}
