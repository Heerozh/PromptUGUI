using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    /// <summary>
    /// <c>intensity</c> is the exposure knob of spec 2026-09-12: <c>1</c> is today's rendering,
    /// larger is brighter. The grammar mirrors the glass numbers (empty = default, NaN and Infinity
    /// refused) but lives in its own parser — <c>GlassRules</c> turns
    /// <see cref="GlassAttrParser.NumericAttrs"/> into <c>PUI-GLASS-PARAM-NO-GLASS</c>, and
    /// intensity is precisely the attribute glass does not take.
    /// </summary>
    public class IntensityAttrParserTests
    {
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void Empty_IsTheDefaultOne(string value)
        {
            // A Variant can only overwrite a value, never remove the attribute, so "" has to be
            // the way back to "not lit".
            Assert.IsTrue(IntensityAttrParser.TryParse(value, out var k, out var error));
            Assert.IsNull(error);
            Assert.AreEqual(IntensityAttrParser.Default, k, 0.0001f);
            Assert.AreEqual(1f, IntensityAttrParser.Default, 0.0001f, "1 must mean 'unchanged'");
        }

        [TestCase("1", 1f)]
        [TestCase("3", 3f)]
        [TestCase(" 2.5 ", 2.5f)]
        [TestCase("12", 12f)]
        [TestCase("1e3", 1000f)]
        public void Numbers_Parse_InvariantCulture(string value, float expected)
        {
            Assert.IsTrue(IntensityAttrParser.TryParse(value, out var k, out var error), error);
            Assert.AreEqual(expected, k, 0.0001f);
        }

        [TestCase("abc")]
        [TestCase("3px")]
        [TestCase("2,3")]
        public void NotANumber_IsRejected(string value)
        {
            Assert.IsFalse(IntensityAttrParser.TryParse(value, out var k, out var error));
            StringAssert.Contains($"intensity=\"{value}\": expected a number (e.g. \"3\")", error);
            Assert.AreEqual(IntensityAttrParser.Default, k, "a rejected value must fall back to the default");
        }

        [TestCase("0.5")]
        [TestCase("0")]
        [TestCase("-1")]
        [TestCase("0.999")]
        public void BelowOne_IsRejected(string value)
        {
            // Below 1 is under-exposure, which *Modulate already covers; the spec keeps it out of v1.
            Assert.IsFalse(IntensityAttrParser.TryParse(value, out _, out var error));
            StringAssert.Contains($"intensity=\"{value}\": must not be less than 1", error);
        }

        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("-Infinity")]
        public void NonFinite_IsRejected(string value)
        {
            Assert.IsFalse(IntensityAttrParser.TryParse(value, out _, out var error),
                $"intensity=\"{value}\" must not reach the shader");
            StringAssert.Contains("must be a finite number", error);
        }

        [Test]
        public void Parse_ThrowsWithTheSameWording()
        {
            var ex = Assert.Throws<ParseException>(() => IntensityAttrParser.Parse("0.5"));
            StringAssert.Contains("intensity=\"0.5\": must not be less than 1", ex.Message);
            Assert.AreEqual(4f, IntensityAttrParser.Parse("4"), 0.0001f);
        }

        [Test]
        public void IsNotAGlassNumber()
        {
            Assert.AreEqual("intensity", IntensityAttrParser.Name);
            Assert.IsFalse(GlassAttrParser.IsNumericAttr(IntensityAttrParser.Name),
                "intensity must stay out of GlassAttrParser.NumericAttrs, or GlassRules would " +
                "report it as a glass parameter written without glass");
        }
    }
}
