using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    /// <summary>
    /// <c>float.TryParse</c> under InvariantCulture happily accepts "NaN" and "Infinity", and every
    /// range check written as <c>value &lt; min || value &gt; max</c> is false for NaN — so a typo
    /// sails through the validation layer built to catch exactly that and lands in a shader uniform,
    /// where it produces undefined output with no diagnostic anywhere.
    /// </summary>
    public class NumericGuardTests
    {
        [TestCase("NaN")]
        [TestCase("Infinity")]
        [TestCase("-Infinity")]
        public void Radius_RejectsNonFiniteValues(string value)
        {
            Assert.IsFalse(RadiusParser.TryParse(value, out _, out var error));
            Assert.IsNotNull(error);
        }

        [Test]
        public void Radius_RejectsNonFiniteCornerSegment()
        {
            Assert.IsFalse(RadiusParser.TryParse("4,NaN,4,4", out _, out var error));
            StringAssert.Contains("top-right", error);
        }

        [TestCase(GlassAttrParser.Frost, "NaN")]
        [TestCase(GlassAttrParser.Frost, "Infinity")]
        [TestCase(GlassAttrParser.Depth, "NaN")]
        [TestCase(GlassAttrParser.Depth, "Infinity")]
        [TestCase(GlassAttrParser.LightAngle, "NaN")]
        [TestCase(GlassAttrParser.Saturation, "Infinity")]
        [TestCase(GlassAttrParser.Weld, "NaN")]
        [TestCase(GlassAttrParser.Seam, "NaN")]
        [TestCase(GlassAttrParser.Seam, "Infinity")]
        public void GlassAttrs_RejectNonFiniteValues(string attr, string value)
        {
            Assert.IsFalse(GlassAttrParser.TryParseValue(attr, value, out _, out var error),
                $"{attr}=\"{value}\" must not reach the shader");
            Assert.IsNotNull(error);
        }

        [Test]
        public void GlassAttrs_StillAcceptOrdinaryValues()
        {
            Assert.IsTrue(GlassAttrParser.TryParseValue(GlassAttrParser.Frost, "0.5", out var f, out _));
            Assert.AreEqual(0.5f, f, 0.0001f);
            // An angle is cyclic, so large magnitudes stay legal — only non-finite is rejected.
            Assert.IsTrue(GlassAttrParser.TryParseValue(GlassAttrParser.LightAngle, "-720", out _, out _));
            // A Variant can only overwrite a value, never remove the attribute, so "" has to be the
            // way back to the default — for seam as for every other glass number.
            Assert.IsTrue(GlassAttrParser.TryParseValue(GlassAttrParser.Seam, "", out var seam, out _));
            Assert.AreEqual(GlassAttrParser.DefaultSeam, seam, 0.0001f);
            Assert.IsTrue(GlassAttrParser.TryParseValue(GlassAttrParser.Seam, "0", out var sharp, out _));
            Assert.AreEqual(0f, sharp, 0.0001f);
            // Negative is the inward step: the ramp lives inside the raised block instead of
            // spilling outside it. Sign is a direction here, not an error.
            Assert.IsTrue(GlassAttrParser.TryParseValue(GlassAttrParser.Seam, "-6", out var inward, out _));
            Assert.AreEqual(-6f, inward, 0.0001f);
        }
    }
}
