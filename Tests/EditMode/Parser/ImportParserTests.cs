using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser {
    public class ImportParserTests {
        const string Header = @"<?xml version='1.0'?><PromptUGUI version='1'>";
        const string Footer = @"</PromptUGUI>";

        [Test]
        public void TopLevel_Import_collected_in_doc() {
            var xml = Header + @"<Import src='common/Buttons'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual(1, doc.Imports.Count);
            Assert.AreEqual("common/Buttons", doc.Imports[0].Src);
            Assert.IsNull(doc.Imports[0].Namespace);
        }

        [Test]
        public void Import_inside_Screen_throws() {
            var xml = Header + @"<Screen name='X'><Import src='y'/></Screen>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Import_inside_Template_throws() {
            var xml = Header + @"<Template name='T'><Import src='y'/><Frame/></Template>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Duplicate_src_in_same_file_throws() {
            var xml = Header + @"<Import src='a'/><Import src='a'/>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("duplicate", ex.Message.ToLowerInvariant());
        }
    }
}
