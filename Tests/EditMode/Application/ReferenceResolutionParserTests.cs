using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.Application
{
    public class ReferenceResolutionParserTests
    {
        [Test]
        public void Parse_null_returns_null()
        {
            Assert.IsNull(ReferenceResolutionParser.Parse(null, "ctx"));
        }

        [Test]
        public void Parse_empty_returns_null()
        {
            Assert.IsNull(ReferenceResolutionParser.Parse("", "ctx"));
        }

        [Test]
        public void Parse_valid_WxH_returns_vector()
        {
            var v = ReferenceResolutionParser.Parse("1920x1080", "ctx");
            Assert.IsTrue(v.HasValue);
            Assert.AreEqual(new Vector2(1920, 1080), v.Value);
        }

        [Test]
        public void Parse_floats_allowed()
        {
            var v = ReferenceResolutionParser.Parse("960.5x540.25", "ctx");
            Assert.IsTrue(v.HasValue);
            Assert.AreEqual(new Vector2(960.5f, 540.25f), v.Value);
        }

        [Test]
        public void Parse_missing_x_throws()
        {
            var ex = Assert.Throws<ParseException>(
                () => ReferenceResolutionParser.Parse("1920", "ctx"));
            StringAssert.Contains("WxH", ex.Message);
            StringAssert.Contains("ctx", ex.Message);
        }

        [Test]
        public void Parse_zero_width_throws()
        {
            var ex = Assert.Throws<ParseException>(
                () => ReferenceResolutionParser.Parse("0x1080", "ctx"));
            StringAssert.Contains("positive", ex.Message);
        }

        [Test]
        public void Parse_zero_height_throws()
        {
            var ex = Assert.Throws<ParseException>(
                () => ReferenceResolutionParser.Parse("1920x0", "ctx"));
            StringAssert.Contains("positive", ex.Message);
        }

        [Test]
        public void Parse_negative_throws()
        {
            Assert.Throws<ParseException>(
                () => ReferenceResolutionParser.Parse("-1x100", "ctx"));
            Assert.Throws<ParseException>(
                () => ReferenceResolutionParser.Parse("100x-1", "ctx"));
        }

        [Test]
        public void Parse_garbage_throws()
        {
            Assert.Throws<ParseException>(
                () => ReferenceResolutionParser.Parse("abc", "ctx"));
            Assert.Throws<ParseException>(
                () => ReferenceResolutionParser.Parse("axb", "ctx"));
            Assert.Throws<ParseException>(
                () => ReferenceResolutionParser.Parse("100x200x300", "ctx"));
        }
    }
}
