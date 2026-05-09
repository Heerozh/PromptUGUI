using NUnit.Framework;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Template
{
    public class TemplateArgsPropagationTests
    {
        [Test]
        public void Expand_PassesArgsToTextArgsOnExpandedNodes()
        {
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Gold'>
    <Param name='n'/>
    <Text>金币: {{n}}</Text>
  </Template>
  <Screen name='S'>
    <Gold n='123'/>
  </Screen>
</PromptUGUI>";
            var raw = UIDocumentParser.Parse(xml);
            var doc = TemplateExpander.Expand(raw);
            var text = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("Text", text.Tag);
            // textContent post-expand is substituted (existing behavior)
            Assert.AreEqual("金币: 123", text.TextContent);
            // raw is preserved
            Assert.AreEqual("金币: {{n}}", text.TextContentRaw);
            // args carry the substitution map
            Assert.IsNotNull(text.TextArgs);
            Assert.AreEqual("123", text.TextArgs["n"]);
        }

        [Test]
        public void Expand_PreservesAttributesRawOnExpansion()
        {
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Gold'>
    <Param name='n'/>
    <Text text='金币: {{n}}'/>
  </Template>
  <Screen name='S'>
    <Gold n='5'/>
  </Screen>
</PromptUGUI>";
            var doc = TemplateExpander.Expand(UIDocumentParser.Parse(xml));
            var text = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("金币: 5", text.Attributes["text"]);
            Assert.AreEqual("金币: {{n}}", text.AttributesRaw["text"]);
            Assert.AreEqual("5", text.TextArgs["n"]);
        }
    }
}
