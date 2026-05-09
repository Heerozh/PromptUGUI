using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser
{
    public class RawAttributesAndContentTests
    {
        [Test]
        public void TextContentRaw_PopulatedFromTextNode()
        {
            var xml = "<PromptUGUI version='1'><Screen name='S'><Text>hi {{n}}</Text></Screen></PromptUGUI>";
            var node = UIDocumentParser.Parse(xml).Screens[0].Root.Children[0];
            Assert.AreEqual("hi {{n}}", node.TextContentRaw);
        }

        [Test]
        public void AttributesRaw_PopulatedOnlyWhenValueContainsBraces()
        {
            var xml = "<PromptUGUI version='1'><Screen name='S'>" +
                      "<Text text='Gold: {{n}}' fontSize='32'/></Screen></PromptUGUI>";
            var node = UIDocumentParser.Parse(xml).Screens[0].Root.Children[0];
            Assert.IsTrue(node.AttributesRaw.ContainsKey("text"));
            Assert.AreEqual("Gold: {{n}}", node.AttributesRaw["text"]);
            Assert.IsFalse(node.AttributesRaw.ContainsKey("fontSize"));
        }
    }
}
