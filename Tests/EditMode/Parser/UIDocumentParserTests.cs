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

        [Test]
        public void Lifts_id_attribute_to_dedicated_field() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <UI version='1'>
                    <Screen name='X'>
                        <Image id='bg' sprite='main' />
                    </Screen>
                </UI>";

            var doc = UIDocumentParser.Parse(xml);
            var img = doc.Screens[0].Root.Children[0];

            Assert.AreEqual("bg", img.Id);
            Assert.IsFalse(img.Attributes.ContainsKey("id"),
                "id should be lifted, not stored in Attributes dict");
        }

        [Test]
        public void Throws_on_duplicate_id_within_same_screen() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <UI version='1'>
                    <Screen name='X'>
                        <Image id='dup' />
                        <Frame>
                            <Image id='dup' />
                        </Frame>
                    </Screen>
                </UI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_duplicate_screen_name() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <UI version='1'>
                    <Screen name='Same' />
                    <Screen name='Same' />
                </UI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_missing_root_UI() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse("<Screen name='X' />"));
        }

        [Test]
        public void Throws_on_missing_UI_version() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse("<UI><Screen name='X' /></UI>"));
        }

        [Test]
        public void Throws_on_screen_without_name() {
            const string xml = "<UI version='1'><Screen /></UI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_invalid_xml() {
            Assert.Throws<System.Xml.XmlException>(() =>
                UIDocumentParser.Parse("<UI version='1'><Screen></UI>"));
        }

        [Test]
        public void Text_shorthand_works_when_only_text_child() {
            var doc = UIDocumentParser.Parse(
                "<UI version='1'><Screen name='X'><Text>Hello</Text></Screen></UI>");
            Assert.AreEqual("Hello", doc.Screens[0].Root.Children[0].TextContent);
        }

        [Test]
        public void Text_shorthand_with_whitespace_is_trimmed() {
            var doc = UIDocumentParser.Parse(
                "<UI version='1'><Screen name='X'><Text>  Hello  </Text></Screen></UI>");
            Assert.AreEqual("Hello", doc.Screens[0].Root.Children[0].TextContent);
        }

        [Test]
        public void Text_shorthand_disallowed_when_mixed_with_elements() {
            const string xml = @"<UI version='1'>
                <Screen name='X'>
                    <Btn>Hello <Image /></Btn>
                </Screen></UI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parses_template_with_typed_params() {
            const string xml = @"<UI version='1'>
                <Template name='TitledPanel'>
                    <Param name='title'/>
                    <Param name='closable' default='true'/>
                    <VStack padding='16'>
                        <Text>{{title}}</Text>
                    </VStack>
                </Template>
            </UI>";

            var doc = UIDocumentParser.Parse(xml);

            Assert.AreEqual(1, doc.Templates.Count);
            var tpl = doc.Templates["TitledPanel"];
            Assert.AreEqual("TitledPanel", tpl.Name);
            Assert.AreEqual(2, tpl.Params.Count);

            Assert.AreEqual("title",    tpl.Params[0].Name);
            Assert.IsFalse(tpl.Params[0].HasDefault);

            Assert.AreEqual("closable", tpl.Params[1].Name);
            Assert.IsTrue(tpl.Params[1].HasDefault);
            Assert.AreEqual("true", tpl.Params[1].DefaultValue);

            Assert.IsNotNull(tpl.Body);
            Assert.AreEqual("VStack", tpl.Body.Tag);
        }

        [Test]
        public void Throws_on_template_without_name() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse("<UI version='1'><Template><VStack/></Template></UI>"));
        }

        [Test]
        public void Throws_on_template_with_zero_root_elements() {
            const string xml = @"<UI version='1'>
                <Template name='Empty'>
                    <Param name='x'/>
                </Template>
            </UI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_template_with_multiple_root_elements() {
            const string xml = @"<UI version='1'>
                <Template name='Two'>
                    <VStack/>
                    <HStack/>
                </Template>
            </UI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_duplicate_template_name() {
            const string xml = @"<UI version='1'>
                <Template name='Same'><Frame/></Template>
                <Template name='Same'><Frame/></Template>
            </UI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_param_after_first_body_element() {
            const string xml = @"<UI version='1'>
                <Template name='Bad'>
                    <Frame/>
                    <Param name='late'/>
                </Template>
            </UI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }
    }
}
