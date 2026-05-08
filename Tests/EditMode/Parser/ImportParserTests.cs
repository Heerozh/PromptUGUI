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

        [Test]
        public void Namespaced_tag_split_into_ns_and_name() {
            var xml = Header +
                @"<Screen name='S'><ml.Foo id='x'/></Screen>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var foo = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("Foo", foo.Tag);
            Assert.AreEqual("ml", foo.Namespace);
        }

        [Test]
        public void Plain_tag_has_null_namespace() {
            var xml = Header + @"<Screen name='S'><Frame/></Screen>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            var frame = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("Frame", frame.Tag);
            Assert.IsNull(frame.Namespace);
        }

        [Test]
        public void Multiple_dots_in_tag_throws() {
            var xml = Header + @"<Screen name='S'><a.b.c/></Screen>" + Footer;
            var ex = Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
            StringAssert.Contains("namespace", ex.Message.ToLowerInvariant());
        }

        [Test]
        public void Import_with_as_recorded() {
            var xml = Header + @"<Import src='a' as='ns'/>" + Footer;
            var doc = UIDocumentParser.Parse(xml);
            Assert.AreEqual("ns", doc.Imports[0].Namespace);
        }

        [Test]
        public void Import_with_empty_as_throws() {
            var xml = Header + @"<Import src='a' as=''/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Import_with_dot_in_as_throws() {
            var xml = Header + @"<Import src='a' as='x.y'/>" + Footer;
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }
    }
}
