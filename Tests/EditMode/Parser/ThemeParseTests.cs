using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser
{
    public class ThemeParseTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";
        private const string Footer = "</PromptUGUI>";

        [Test]
        public void Single_Theme_Single_Color_Parses()
        {
            var doc = UIDocumentParser.Parse(Header +
                "<Theme name='light'><Color name='primary' value='#ff8800'/></Theme>" + Footer);
            Assert.AreEqual(1, doc.Themes.Count);
            Assert.AreEqual("light", doc.Themes[0].Name);
            Assert.IsNull(doc.Themes[0].BaseName);
            Assert.AreEqual(1, doc.Themes[0].Colors.Count);
            Assert.AreEqual("primary", doc.Themes[0].Colors[0].Name);
            Assert.AreEqual("#ff8800", doc.Themes[0].Colors[0].Value);
        }

        [Test]
        public void Theme_With_Base()
        {
            var doc = UIDocumentParser.Parse(Header +
                "<Theme name='light'><Color name='p' value='#ff0000'/></Theme>" +
                "<Theme name='dark' base='light'><Color name='p' value='#000000'/></Theme>" + Footer);
            Assert.AreEqual("light", doc.Themes[1].BaseName);
        }

        [Test]
        public void Theme_Missing_Name_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(
                Header + "<Theme><Color name='p' value='#ff0000'/></Theme>" + Footer));
            StringAssert.Contains("name", ex.Message);
        }

        [Test]
        public void Color_Missing_Name_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(
                Header + "<Theme name='l'><Color value='#ff0000'/></Theme>" + Footer));
            StringAssert.Contains("name", ex.Message);
        }

        [Test]
        public void Color_Missing_Value_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(
                Header + "<Theme name='l'><Color name='p'/></Theme>" + Footer));
            StringAssert.Contains("value", ex.Message);
        }

        [Test]
        public void Color_Invalid_Value_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(
                Header + "<Theme name='l'><Color name='p' value='#xyz'/></Theme>" + Footer));
            StringAssert.Contains("invalid color literal", ex.Message);
        }

        [Test]
        public void Token_Name_NonKebab_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(
                Header + "<Theme name='l'><Color name='Primary' value='#ff0000'/></Theme>" + Footer));
            StringAssert.Contains("kebab-case", ex.Message);
        }

        [Test]
        public void Duplicate_Color_Name_Within_Theme_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(Header +
                "<Theme name='l'><Color name='p' value='#ff0000'/>" +
                "<Color name='p' value='#000000'/></Theme>" + Footer));
            StringAssert.Contains("twice", ex.Message);
        }

        [Test]
        public void Duplicate_Theme_Name_Within_Doc_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(Header +
                "<Theme name='light'/><Theme name='light'/>" + Footer));
            StringAssert.Contains("light", ex.Message);
        }

        [Test]
        public void Non_Color_Child_Throws()
        {
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(Header +
                "<Theme name='l'><Frame/></Theme>" + Footer));
            StringAssert.Contains("Frame", ex.Message);
        }
    }
}
