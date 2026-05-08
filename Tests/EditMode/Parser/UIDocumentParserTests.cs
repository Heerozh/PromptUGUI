using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser {
    public class UIDocumentParserTests {
        [Test]
        public void Parses_minimal_document_with_one_screen() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <PromptUGUI version='1'>
                    <Screen name='MainMenu' />
                </PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);

            Assert.AreEqual(1, doc.Version);
            Assert.AreEqual(1, doc.Screens.Count);
            Assert.AreEqual("MainMenu", doc.Screens[0].Name);
            Assert.IsNotNull(doc.Screens[0].Root);
        }

        [Test]
        public void Parses_nested_elements_with_attributes() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <PromptUGUI version='1'>
                    <Screen name='X'>
                        <VStack anchor='center' size='480x320' spacing='12'>
                            <Image sprite='bg' anchor='stretch' />
                            <Text>Hello</Text>
                        </VStack>
                    </Screen>
                </PromptUGUI>";

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
                <PromptUGUI version='1'>
                    <Screen name='X'>
                        <Image id='bg' sprite='main' />
                    </Screen>
                </PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            var img = doc.Screens[0].Root.Children[0];

            Assert.AreEqual("bg", img.Id);
            Assert.IsFalse(img.Attributes.ContainsKey("id"),
                "id should be lifted, not stored in Attributes dict");
        }

        [Test]
        public void Throws_on_duplicate_id_within_same_screen() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <PromptUGUI version='1'>
                    <Screen name='X'>
                        <Image id='dup' />
                        <Frame>
                            <Image id='dup' />
                        </Frame>
                    </Screen>
                </PromptUGUI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_duplicate_screen_name() {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
                <PromptUGUI version='1'>
                    <Screen name='Same' />
                    <Screen name='Same' />
                </PromptUGUI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_missing_root_PromptUGUI() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse("<Screen name='X' />"));
        }

        [Test]
        public void Throws_on_missing_PromptUGUI_version() {
            Assert.Throws<ParseException>(() =>
                UIDocumentParser.Parse("<PromptUGUI><Screen name='X' /></PromptUGUI>"));
        }

        [Test]
        public void Throws_on_screen_without_name() {
            const string xml = "<PromptUGUI version='1'><Screen /></PromptUGUI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_invalid_xml() {
            Assert.Throws<System.Xml.XmlException>(() =>
                UIDocumentParser.Parse("<PromptUGUI version='1'><Screen></PromptUGUI>"));
        }

        [Test]
        public void Text_shorthand_works_when_only_text_child() {
            var doc = UIDocumentParser.Parse(
                "<PromptUGUI version='1'><Screen name='X'><Text>Hello</Text></Screen></PromptUGUI>");
            Assert.AreEqual("Hello", doc.Screens[0].Root.Children[0].TextContent);
        }

        [Test]
        public void Text_shorthand_with_whitespace_is_trimmed() {
            var doc = UIDocumentParser.Parse(
                "<PromptUGUI version='1'><Screen name='X'><Text>  Hello  </Text></Screen></PromptUGUI>");
            Assert.AreEqual("Hello", doc.Screens[0].Root.Children[0].TextContent);
        }

        [Test]
        public void Text_shorthand_disallowed_when_mixed_with_elements() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <Btn>Hello <Image /></Btn>
                </Screen></PromptUGUI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parses_template_with_typed_params() {
            const string xml = @"<PromptUGUI version='1'>
                <Template name='TitledPanel'>
                    <Param name='title'/>
                    <Param name='closable' default='true'/>
                    <VStack padding='16'>
                        <Text>{{title}}</Text>
                    </VStack>
                </Template>
            </PromptUGUI>";

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
                UIDocumentParser.Parse("<PromptUGUI version='1'><Template><VStack/></Template></PromptUGUI>"));
        }

        [Test]
        public void Throws_on_template_with_zero_root_elements() {
            const string xml = @"<PromptUGUI version='1'>
                <Template name='Empty'>
                    <Param name='x'/>
                </Template>
            </PromptUGUI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_template_with_multiple_root_elements() {
            const string xml = @"<PromptUGUI version='1'>
                <Template name='Two'>
                    <VStack/>
                    <HStack/>
                </Template>
            </PromptUGUI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_duplicate_template_name() {
            const string xml = @"<PromptUGUI version='1'>
                <Template name='Same'><Frame/></Template>
                <Template name='Same'><Frame/></Template>
            </PromptUGUI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_param_after_first_body_element() {
            const string xml = @"<PromptUGUI version='1'>
                <Template name='Bad'>
                    <Frame/>
                    <Param name='late'/>
                </Template>
            </PromptUGUI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_duplicate_param_name_in_template() {
            const string xml = @"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Param name='x'/>
                    <Param name='x' default='y'/>
                    <Frame/>
                </Template>
            </PromptUGUI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Parses_Slot_as_ordinary_element_node() {
            const string xml = @"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Slot/>
                    </Frame>
                </Template>
            </PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            var body = doc.Templates["Box"].Body;
            Assert.AreEqual("Frame", body.Tag);
            Assert.AreEqual(1, body.Children.Count);
            Assert.AreEqual("Slot", body.Children[0].Tag);
        }

        [Test]
        public void Parses_attr_with_variant_suffix() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack id='v' anchor='center' anchor.mobile='bottom-stretch'/>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            var v = doc.Screens[0].Root.Children[0];

            Assert.AreEqual("center", v.Attributes["anchor"]);
            Assert.IsFalse(v.Attributes.ContainsKey("anchor.mobile"));
            Assert.IsTrue(v.VariantOverrides.ContainsKey("anchor"));
            var list = v.VariantOverrides["anchor"];
            Assert.AreEqual(1, list.Count);
            Assert.AreEqual("mobile", list[0].Variant);
            Assert.AreEqual("bottom-stretch", list[0].Value);
        }

        [Test]
        public void Multiple_variants_preserve_declaration_order() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack id='v' size='100x100'
                            size.mobile='200x200' size.tablet='150x150'/>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            var list = doc.Screens[0].Root.Children[0].VariantOverrides["size"];
            Assert.AreEqual(2, list.Count);
            Assert.AreEqual("mobile", list[0].Variant);
            Assert.AreEqual("tablet", list[1].Variant);
        }

        [Test]
        public void Variant_only_attr_without_base_is_allowed() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack id='v' margin.mobile='16'/>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            var v = doc.Screens[0].Root.Children[0];
            Assert.IsFalse(v.Attributes.ContainsKey("margin"));
            Assert.IsTrue(v.VariantOverrides.ContainsKey("margin"));
            Assert.AreEqual(1, v.VariantOverrides["margin"].Count);
        }

        [Test]
        public void Multiple_attrs_with_their_own_variants_dont_interfere() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack anchor='center' anchor.mobile='top-stretch'
                            margin='8'   margin.mobile='16'/>
                </Screen></PromptUGUI>";

            var doc = UIDocumentParser.Parse(xml);
            var v = doc.Screens[0].Root.Children[0];
            Assert.AreEqual(1, v.VariantOverrides["anchor"].Count);
            Assert.AreEqual("top-stretch", v.VariantOverrides["anchor"][0].Value);
            Assert.AreEqual(1, v.VariantOverrides["margin"].Count);
            Assert.AreEqual("16", v.VariantOverrides["margin"][0].Value);
        }

        [Test]
        public void Throws_on_id_with_variant_suffix() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack id='v' id.mobile='other'/>
                </Screen></PromptUGUI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_param_default_with_variant_suffix() {
            const string xml = @"<PromptUGUI version='1'>
                <Template name='T'>
                    <Param name='x' default='a' default.mobile='b'/>
                    <Frame/>
                </Template></PromptUGUI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_attr_with_empty_variant_after_dot() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack anchor.='top-left'/>
                </Screen></PromptUGUI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }

        [Test]
        public void Throws_on_dot_inside_variant_name() {
            const string xml = @"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack anchor.mobile.portrait='top-left'/>
                </Screen></PromptUGUI>";

            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }
    }
}
