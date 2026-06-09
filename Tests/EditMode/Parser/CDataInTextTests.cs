using NUnit.Framework;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser
{
    public class CDataInTextTests
    {
        [Test]
        public void Text_WithCData_ContainingTmpSprite_Parses()
        {
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
            StringAssert.Contains("{{n}}", text.TextContentRaw);
        }

        [Test]
        public void Markdown_WithIndentedMultilineCData_StripsCommonIndent()
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n"
                + "<PromptUGUI version='1'>\n  <Screen name='S'>\n"
                + "    <Markdown><![CDATA[\n    # Title\n\n    **bold** body\n    | A | B |\n    |---|---|\n    | 1 | 2 |\n]]></Markdown>\n"
                + "  </Screen>\n</PromptUGUI>";
            var doc = UIDocumentParser.Parse(xml);
            var md = doc.Screens[0].Root.Children[0];
            Assert.AreEqual("Markdown", md.Tag);
            // common 4-space indent stripped from ALL lines:
            StringAssert.StartsWith("# Title", md.TextContent);                 // line 1 flush (was already)
            StringAssert.Contains("\n**bold** body", md.TextContent);          // line 3 now flush (NOT "\n    **bold**")
            StringAssert.DoesNotContain("\n    ", md.TextContent);             // no 4-space-indented line remains
            StringAssert.Contains("| A | B |", md.TextContent);
        }

        [Test]
        public void Text_WithMixedTextAndCdata_StillForbidden()
        {
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
