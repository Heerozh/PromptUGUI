using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Parser
{
    public class StyleParsingTests
    {
        private const string Head = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>";
        private const string Tail = "</PromptUGUI>";

        private static PromptUGUI.IR.UIDocument Parse(string body)
            => UIDocumentParser.Parse(Head + body + Tail);

        [Test]
        public void Style_CapturesAttributesIntoPack()
        {
            var doc = Parse("<Style name='card' color='surface/0.85' radius='16' borderWidth='1'/>");
            var style = doc.Styles["card"];
            Assert.AreEqual("surface/0.85", style.Attributes["color"]);
            Assert.AreEqual("16", style.Attributes["radius"]);
            Assert.AreEqual("1", style.Attributes["borderWidth"]);
            Assert.IsFalse(style.Attributes.ContainsKey("name"), "'name' is the style's own id, not part of the pack");
        }

        [Test]
        public void Style_CapturesVariantSuffixes()
        {
            var doc = Parse("<Style name='card' radius='16' radius.mobile='8' radius.tv='24'/>");
            var style = doc.Styles["card"];
            Assert.AreEqual("16", style.Attributes["radius"]);
            var overrides = style.VariantOverrides["radius"];
            Assert.AreEqual(2, overrides.Count);
            Assert.AreEqual(("mobile", "8"), overrides[0]);
            Assert.AreEqual(("tv", "24"), overrides[1]);
        }

        [Test]
        public void Style_VariantOnlyAttribute_CountsAsDeclaredName()
        {
            var doc = Parse("<Style name='card' radius.mobile='8'/>");
            var style = doc.Styles["card"];
            CollectionAssert.AreEquivalent(new[] { "radius" }, style.DeclaredNames);
        }

        [Test]
        public void Style_MissingName_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => Parse("<Style color='#fff'/>"));
            StringAssert.Contains("name", ex.Message);
        }

        [Test]
        public void Style_NonKebabName_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => Parse("<Style name='Card' color='#fff'/>"));
            StringAssert.Contains("kebab-case", ex.Message);
        }

        [Test]
        public void Style_DuplicateName_Throws()
        {
            var ex = Assert.Throws<ParseException>(() =>
                Parse("<Style name='card' color='#fff'/><Style name='card' color='#000'/>"));
            StringAssert.Contains("duplicate", ex.Message.ToLowerInvariant());
        }

        [Test]
        public void Style_WithChildren_Throws()
        {
            var ex = Assert.Throws<ParseException>(() =>
                Parse("<Style name='card' color='#fff'><Frame/></Style>"));
            StringAssert.Contains("no children", ex.Message);
        }

        [Test]
        public void Style_NoAttributes_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => Parse("<Style name='card'/>"));
            StringAssert.Contains("no attributes", ex.Message);
        }

        [TestCase("id")]
        [TestCase("if")]
        [TestCase("class")]
        [TestCase("bind")]
        public void Style_StructuralAttribute_Throws(string attr)
        {
            var ex = Assert.Throws<ParseException>(() =>
                Parse($"<Style name='card' {attr}='x'/>"));
            StringAssert.Contains(attr, ex.Message);
        }

        [Test]
        public void Style_StructuralAttributeWithVariantSuffix_AlsoThrows()
        {
            Assert.Throws<ParseException>(() => Parse("<Style name='card' id.mobile='x'/>"));
        }

        [Test]
        public void Style_MalformedVariantSuffix_Throws()
        {
            var ex = Assert.Throws<ParseException>(() =>
                Parse("<Style name='card' radius.a.b='8'/>"));
            StringAssert.Contains("variant", ex.Message);
        }

        [Test]
        public void Screen_ClassAttribute_Throws()
        {
            var ex = Assert.Throws<ParseException>(() =>
                Parse("<Screen name='S' class='card'><Frame/></Screen>"));
            StringAssert.Contains("class", ex.Message);
        }

        [Test]
        public void ClassOnElement_ParsesAsPlainAttribute()
        {
            var doc = Parse("<Screen name='S'><Frame id='f' class='card'/></Screen>");
            var frame = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("card", frame.Attributes["class"]);
        }
    }
}
