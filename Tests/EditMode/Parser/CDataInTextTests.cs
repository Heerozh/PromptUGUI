using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser {
    public class CDataInTextTests {
        [Test] public void Text_WithCData_ContainingTmpSprite_Parses() {
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Text><![CDATA[gold: <sprite name=""coin""/>{{n}}]]></Text>
  </Screen>
</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var text = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("Text", text.Tag);
            StringAssert.Contains("<sprite", text.TextContentRaw);
            StringAssert.Contains("{{n}}",   text.TextContentRaw);
        }

        [Test] public void Text_WithMixedTextAndCdata_StillForbidden() {
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Screen name='S'>
    <Text>foo<sprite/></Text>
  </Screen>
</PromptUGUI>";
            Assert.Throws<ParseException>(() => UIDocumentParser.Parse(xml));
        }
    }
}
