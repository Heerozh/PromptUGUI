using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor.I18n;
using PromptUGUI.IR;
using PromptUGUI.Parser;

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

        [Test]
        public void Scan_TemplateInvocationParamFlowsIntoTextBody_ExtractsValue()
        {
            // The Template body's text is *purely* a placeholder, so the
            // user-visible string is whatever each invocation passes for `label`.
            // Those param values must end up in the .po file.
            var xml = @"<PromptUGUI version='1'>
                <Template name='MenuBtn'>
                    <Param name='label'/>
                    <Btn><Text>{{label}}</Text></Btn>
                </Template>
                <Screen name='Main'>
                    <MenuBtn label='开始'/>
                    <MenuBtn label='退出'/>
                </Screen>
            </PromptUGUI>";
            var msgids = XmlStringScanner.Scan(xml, "screens/Main")
                .Select(e => e.Msgid).ToList();
            Assert.Contains("开始", msgids);
            Assert.Contains("退出", msgids);
        }

        [Test]
        public void Scan_TemplateInvocationParamFlowsIntoTextAttr_ExtractsValue()
        {
            // Same expectation when the param flows into a `text` attribute
            // instead of element body.
            var xml = @"<PromptUGUI version='1'>
                <Template name='MenuBtn'>
                    <Param name='label'/>
                    <Btn text='{{label}}'/>
                </Template>
                <Screen name='Main'>
                    <MenuBtn label='开始'/>
                </Screen>
            </PromptUGUI>";
            var msgids = XmlStringScanner.Scan(xml, "screens/Main")
                .Select(e => e.Msgid).ToList();
            Assert.Contains("开始", msgids);
        }

        [Test]
        public void Scan_CrossFileTemplateInvocation_ExtractsParamValue()
        {
            // Template lives in a separate file; the Screen file invokes it. The
            // external-template pool is what StringExtractor builds in its first
            // pass — verify the scanner consults it so cross-file param values
            // still end up as msgids.
            var commonsXml = @"<PromptUGUI version='1'>
                <Template name='Hint'>
                    <Param name='msg'/>
                    <Text>{{msg}}</Text>
                </Template>
            </PromptUGUI>";
            var commonsDoc = UIDocumentParser.Parse(commonsXml);
            var pool = new Dictionary<string, TemplateDef>();
            foreach (var kv in commonsDoc.Templates) pool[kv.Key] = kv.Value;

            var screenXml = @"<PromptUGUI version='1'>
                <Screen name='Main'>
                    <Hint msg='Welcome!'/>
                </Screen>
            </PromptUGUI>";
            var msgids = XmlStringScanner.Scan(screenXml, "screens/Main", pool)
                .Select(e => e.Msgid).ToList();
            Assert.Contains("Welcome!", msgids);
        }

        [Test]
        public void Scan_TemplateBodyFormatString_StillExtractedOnce()
        {
            // A format-string body like "Hello {{label}}" stays a single msgid
            // (the format string itself), not one per invocation — the runtime
            // translates the format and substitutes per call. Verify no
            // duplicate substituted-form msgids leak in.
            var xml = @"<PromptUGUI version='1'>
                <Template name='Greet'>
                    <Param name='label'/>
                    <Text>Hello {{label}}</Text>
                </Template>
                <Screen name='Main'>
                    <Greet label='World'/>
                    <Greet label='Friend'/>
                </Screen>
            </PromptUGUI>";
            var msgids = XmlStringScanner.Scan(xml, "screens/Main")
                .Select(e => e.Msgid).ToList();
            Assert.Contains("Hello {{label}}", msgids);
            CollectionAssert.DoesNotContain(msgids, "Hello World");
            CollectionAssert.DoesNotContain(msgids, "Hello Friend");
        }
    }
}
