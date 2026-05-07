using NUnit.Framework;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Template {
    public class TemplateExpanderTests {
        [Test]
        public void Pass_through_screen_with_no_template_invocation() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Screen name='X'>
                    <VStack id='v'>
                        <Image id='a'/>
                    </VStack>
                </Screen></PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);

            Assert.AreEqual(1, expanded.Screens.Count);
            var screen = expanded.Screens[0];
            Assert.AreEqual("X", screen.Name);
            Assert.AreEqual(1, screen.Root.Children.Count);
            var v = screen.Root.Children[0];
            Assert.AreEqual("VStack", v.Tag);
            Assert.AreEqual("v", v.Id);
            Assert.AreEqual(1, v.Children.Count);
            Assert.AreEqual("a", v.Children[0].Id);
        }

        [Test]
        public void Templates_dictionary_carries_through() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'><Frame/></Template>
                <Screen name='X'/>
            </PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);
            Assert.AreEqual(1, expanded.Screens.Count);
        }

        [Test]
        public void Expands_template_invocation_with_params() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Greet'>
                    <Param name='who'/>
                    <Text>Hello {{who}}</Text>
                </Template>
                <Screen name='S'>
                    <Greet who='World'/>
                </Screen></PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);
            var screen = expanded.Screens[0];
            Assert.AreEqual(1, screen.Root.Children.Count);
            var text = screen.Root.Children[0];
            Assert.AreEqual("Text", text.Tag);
            Assert.AreEqual("Hello World", text.TextContent);
        }

        [Test]
        public void Param_default_used_when_invocation_omits_attr() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Param name='label' default='默认'/>
                    <Text>{{label}}</Text>
                </Template>
                <Screen name='S'>
                    <Box/>
                </Screen></PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);
            Assert.AreEqual("默认", expanded.Screens[0].Root.Children[0].TextContent);
        }

        [Test]
        public void Required_param_missing_throws() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Param name='must'/>
                    <Text>{{must}}</Text>
                </Template>
                <Screen name='S'><Box/></Screen></PromptUGUI>");
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(doc));
        }

        [Test]
        public void Unknown_param_passed_throws() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Param name='a'/>
                    <Text>{{a}}</Text>
                </Template>
                <Screen name='S'>
                    <Box a='1' b='2'/>
                </Screen></PromptUGUI>");
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(doc));
        }

        [Test]
        public void Slot_receives_invocation_children() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Slot/>
                    </Frame>
                </Template>
                <Screen name='S'>
                    <Box>
                        <Image id='inside'/>
                    </Box>
                </Screen></PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);
            var box = expanded.Screens[0].Root.Children[0];
            Assert.AreEqual("Frame", box.Tag);
            Assert.AreEqual(1, box.Children.Count);
            Assert.AreEqual("inside", box.Children[0].Id);
        }

        [Test]
        public void If_drops_element_when_falsy() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Param name='show' default='false'/>
                    <Frame>
                        <Image if='{{show}}' id='maybe'/>
                    </Frame>
                </Template>
                <Screen name='S'>
                    <Box/>
                </Screen></PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);
            var frame = expanded.Screens[0].Root.Children[0];
            Assert.AreEqual(0, frame.Children.Count);
        }

        [Test]
        public void Invocation_id_transfers_to_instance_root() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame>
                        <Image id='inside'/>
                    </Frame>
                </Template>
                <Screen name='S'>
                    <Box id='outer'/>
                </Screen></PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);
            var box = expanded.Screens[0].Root.Children[0];
            Assert.AreEqual("Frame", box.Tag);
            Assert.AreEqual("outer", box.Id);
            Assert.IsTrue(box.IsTemplateInstanceRoot);
            Assert.AreEqual("inside", box.Children[0].Id);
        }

        [Test]
        public void Invocation_attributes_other_than_params_passthrough_to_root() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'>
                    <Frame/>
                </Template>
                <Screen name='S'>
                    <Box anchor='center' size='100x100'/>
                </Screen></PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);
            var box = expanded.Screens[0].Root.Children[0];
            Assert.AreEqual("center", box.Attributes["anchor"]);
            Assert.AreEqual("100x100", box.Attributes["size"]);
        }

        [Test]
        public void Nested_template_invocation_expands() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Inner'>
                    <Param name='msg'/>
                    <Text>{{msg}}</Text>
                </Template>
                <Template name='Outer'>
                    <Frame>
                        <Inner msg='from outer'/>
                    </Frame>
                </Template>
                <Screen name='S'>
                    <Outer/>
                </Screen></PromptUGUI>");

            var expanded = TemplateExpander.Expand(doc);
            var frame = expanded.Screens[0].Root.Children[0];
            var text = frame.Children[0];
            Assert.AreEqual("Text", text.Tag);
            Assert.AreEqual("from outer", text.TextContent);
        }

        [Test]
        public void Cyclic_template_reference_throws() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='A'><B/></Template>
                <Template name='B'><A/></Template>
                <Screen name='S'><A/></Screen></PromptUGUI>");
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(doc));
        }

        [Test]
        public void Slot_in_Screen_body_throws() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Screen name='S'>
                    <Slot/>
                </Screen></PromptUGUI>");
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(doc));
        }

        [Test]
        public void Two_slots_in_template_body_throws() {
            var doc = UIDocumentParser.Parse(@"<PromptUGUI version='1'>
                <Template name='Box'>
                    <VStack>
                        <Slot/>
                        <Slot/>
                    </VStack>
                </Template>
                <Screen name='S'><Box/></Screen></PromptUGUI>");
            Assert.Throws<TemplateException>(() => TemplateExpander.Expand(doc));
        }
    }
}
