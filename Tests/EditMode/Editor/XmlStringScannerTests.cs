using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;

namespace PromptUGUI.Tests.Editor
{
    public class XmlStringScannerTests
    {
        [Test]
        public void Scan_SimpleTextElement_ExtractsMsgid()
        {
            var xml = "<PromptUGUI version='1'><Screen name='Main'>" +
                      "<Text>开始游戏</Text></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/Main").ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("开始游戏", found[0].Msgid);
            Assert.IsNull(found[0].Msgctxt);
        }

        [Test]
        public void Scan_BtnContent_ExtractsMsgid()
        {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Btn id='b'>设置</Btn></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("设置", found[0].Msgid);
        }

        [Test]
        public void Scan_TextAttribute_AlsoExtracts()
        {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text text='Hello' fontSize='32'/></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.IsTrue(found.Any(e => e.Msgid == "Hello"));
        }

        [Test]
        public void Scan_TrFalse_Skips()
        {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text tr='false'>hardcoded</Text></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.IsEmpty(found);
        }

        [Test]
        public void Scan_ExplicitCtx_PopulatesMsgctxt()
        {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text ctx='door'>Open</Text></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.AreEqual("door", found[0].Msgctxt);
            Assert.AreEqual("Open", found[0].Msgid);
        }

        [Test]
        public void Scan_PureBracePlaceholder_SkippedWithWarning()
        {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text>{{playerName}}</Text></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.IsEmpty(found);   // pure {{x}} has no translation value
        }

        [Test]
        public void Scan_TextWithBraceAndStatic_Extracted()
        {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text>金币: {{n}}</Text></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.AreEqual("金币: {{n}}", found[0].Msgid);
        }

        [Test]
        public void Scan_AmbientCtx_AddedAsExtractedComment()
        {
            var xml = "<PromptUGUI version='1'><Screen name='Main'>" +
                      "<Btn id='play'>开始</Btn>" +
                      "<Btn id='cfg'>设置</Btn>" +
                      "</Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/Main").ToList();
            var play = found.First(e => e.Msgid == "开始");
            // ambient hint should reference Main + Btn#play
            Assert.IsTrue(play.ExtractedComments.Any(c => c.Contains("Main") && c.Contains("play")));
            // sibling list should mention "设置"
            Assert.IsTrue(play.ExtractedComments.Any(c => c.Contains("设置")));
        }

        [Test]
        public void Scan_CDataWithRichText_ExtractsAndFlags()
        {
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Text><![CDATA[<color=#ff0>警告</color>]]></Text>" +
                      "</Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.AreEqual("<color=#ff0>警告</color>", found[0].Msgid);
            Assert.IsTrue(found[0].ExtractedComments.Any(c => c.Contains("Preserve tags")));
        }
    }
}
