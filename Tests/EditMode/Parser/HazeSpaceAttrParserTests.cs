using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    /// <summary>
    /// <c>hazeSpace</c> (spec 2026-09-17 haze H-D9): where the fog's noise is sampled — the canvas
    /// (the default: the panel is a window onto one shared sky) or the panel itself (the fog rides
    /// along when the panel moves). Pure C#, shared by the runtime setters and the CLI.
    /// </summary>
    public class HazeSpaceAttrParserTests
    {
        [TestCase("")]
        [TestCase("   ")]
        [TestCase(null)]
        public void Empty_IsCanvas(string value)
        {
            // A Variant can only overwrite a value, never remove the attribute, so "" has to be
            // the way back to the default.
            Assert.IsTrue(HazeSpaceAttrParser.TryParse(value, out var self, out var error));
            Assert.IsNull(error);
            Assert.IsFalse(self);
        }

        [TestCase("canvas", false)]
        [TestCase("self", true)]
        [TestCase(" self ", true)]
        public void Keywords_Parse(string value, bool expected)
        {
            Assert.IsTrue(HazeSpaceAttrParser.TryParse(value, out var self, out var error), error);
            Assert.AreEqual(expected, self);
        }

        [TestCase("local")]
        [TestCase("Self")]
        [TestCase("true")]
        [TestCase("parent")]
        public void AnythingElse_IsRejected(string value)
        {
            // Not a silent canvas: hazeSpace="local" looks like it works and would leave the author
            // watching the fog slide across a scrolling row with no idea why.
            Assert.IsFalse(HazeSpaceAttrParser.TryParse(value, out _, out var error));
            StringAssert.Contains("hazeSpace", error);
            StringAssert.Contains("\"canvas\"", error);
            StringAssert.Contains("\"self\"", error);
        }

        [Test]
        public void Parse_ThrowsTheSameMessage()
        {
            var ex = Assert.Throws<ParseException>(() => HazeSpaceAttrParser.Parse("local"));
            HazeSpaceAttrParser.TryParse("local", out _, out var error);
            Assert.AreEqual(error, ex.Message);
        }
    }
}
