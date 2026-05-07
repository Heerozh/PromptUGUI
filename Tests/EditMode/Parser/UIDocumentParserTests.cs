using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser {
    public class UIDocumentParserTests {
        [Test]
        public void Parses_minimal_document_with_one_screen() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <UI version='1'>
                    <Screen name='MainMenu' />
                </UI>";

            var doc = UIDocumentParser.Parse(xml);

            Assert.AreEqual(1, doc.Version);
            Assert.AreEqual(1, doc.Screens.Count);
            Assert.AreEqual("MainMenu", doc.Screens[0].Name);
            Assert.IsNotNull(doc.Screens[0].Root);
        }
    }
}
