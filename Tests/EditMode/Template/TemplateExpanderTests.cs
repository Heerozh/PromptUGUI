using NUnit.Framework;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Template {
    public class TemplateExpanderTests {
        [Test]
        public void Pass_through_screen_with_no_template_invocation() {
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Screen name='X'>
                    <VStack id='v'>
                        <Image id='a'/>
                    </VStack>
                </Screen></UI>");

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
            var doc = UIDocumentParser.Parse(@"<UI version='1'>
                <Template name='Box'><Frame/></Template>
                <Screen name='X'/>
            </UI>");

            var expanded = TemplateExpander.Expand(doc);
            Assert.AreEqual(1, expanded.Screens.Count);
        }
    }
}
