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

        [Test]
        public void Parses_nested_elements_with_attributes() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <UI version='1'>
                    <Screen name='X'>
                        <VStack anchor='center' size='480x320' spacing='12'>
                            <Image sprite='bg' anchor='stretch' />
                            <Text>Hello</Text>
                        </VStack>
                    </Screen>
                </UI>";

            var doc = UIDocumentParser.Parse(xml);
            var root = doc.Screens[0].Root;

            Assert.AreEqual(1, root.Children.Count);
            var vstack = root.Children[0];
            Assert.AreEqual("VStack", vstack.Tag);
            Assert.AreEqual("center", vstack.Attributes["anchor"]);
            Assert.AreEqual("480x320", vstack.Attributes["size"]);
            Assert.AreEqual("12", vstack.Attributes["spacing"]);

            Assert.AreEqual(2, vstack.Children.Count);
            Assert.AreEqual("Image", vstack.Children[0].Tag);
            Assert.AreEqual("bg", vstack.Children[0].Attributes["sprite"]);

            Assert.AreEqual("Text", vstack.Children[1].Tag);
            Assert.AreEqual("Hello", vstack.Children[1].TextContent);
        }
    }
}
