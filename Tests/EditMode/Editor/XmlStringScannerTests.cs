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
        public void Scan_ToggleContent_ExtractsMsgid()
        {
            // <Toggle> renders its element content as a visible TMP label
            // (defaultTextAttr "text"), exactly like <Btn>. The label is a real
            // user-facing string and must be extracted.
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<Toggle id='muteAudio'>静音</Toggle></Screen></PromptUGUI>";
            var found = XmlStringScanner.Scan(xml, "screens/X").ToList();
            Assert.AreEqual(1, found.Count);
            Assert.AreEqual("静音", found[0].Msgid);
        }

        [Test]
        public void Scan_TabTextAttribute_ExtractsMsgid()
        {
            // <Tab text='...'> is the tab's visible label (defaultTextAttr "text").
            var xml = "<PromptUGUI version='1'><Screen name='X'>" +
                      "<TabBar><Tab id='t' text='表单输入'/></TabBar></Screen></PromptUGUI>";
            var msgids = XmlStringScanner.Scan(xml, "screens/X")
                .Select(e => e.Msgid).ToList();
            Assert.Contains("表单输入", msgids);
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

        // ── expansion sees what the runtime sees: Import closure, <Style>, commons ──────────────

        private static UIDocument Doc(string src, string body) =>
            UIDocumentParser.Parse("<PromptUGUI version='1'>" + body + "</PromptUGUI>", src);

        private static List<string> ScanEntry(
            string entry, Dictionary<string, UIDocument> files,
            IReadOnlyList<ImportRef> commons = null, List<string> failures = null) =>
            XmlStringScanner.Scan(files[entry], "p", entry,
                                  s => files.TryGetValue(s, out var d) ? d : null,
                                  commons, msg => failures?.Add(msg))
                .Select(e => e.Msgid).ToList();

        [Test]
        public void Scan_CrossFileTemplateInvocation_ExtractsParamValue()
        {
            // Template lives in a separate file the Screen <Import>s — the param value
            // passed at the invocation site must still end up as a msgid.
            var files = new Dictionary<string, UIDocument>
            {
                ["lib.ui"] = Doc("lib.ui",
                    "<Template name='Hint'><Param name='msg'/><Text>{{msg}}</Text></Template>"),
                ["main.ui"] = Doc("main.ui",
                    "<Import src='lib.ui'/><Screen name='Main'><Hint msg='Welcome!'/></Screen>"),
            };
            Assert.Contains("Welcome!", ScanEntry("main.ui", files));
        }

        [Test]
        public void Scan_TemplateBodyUsesClassFromSameFile_ExtractsParamValue()
        {
            // Expansion merges class= packs; a scan that hands the expander no <Style>
            // table fails on the first class= and loses every invocation-site value.
            var xml = @"<PromptUGUI version='1'>
                <Style name='skin' color='#223344'/>
                <Template name='MenuBtn'>
                    <Param name='label'/>
                    <Btn class='skin'><Text>{{label}}</Text></Btn>
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
        public void Scan_StyledTemplateFromImport_ExtractsParamValues()
        {
            // The shape that went missing from a real .po: a tab-card template whose body is
            // skinned by class=, style + template in an imported library, label / caption
            // passed at each invocation.
            var files = new Dictionary<string, UIDocument>
            {
                ["tabs.ui"] = Doc("tabs.ui",
                    "<Style name='flag' color='#101820'/>" +
                    "<Style name='flag-label' fontSize='14'/>" +
                    "<Template name='FlagTab'><Param name='label'/><Param name='caption'/>" +
                    "<Tab id='tab' class='flag'>" +
                    "<Text class='flag-label'>{{label}}</Text><Text>{{caption}}</Text>" +
                    "</Tab></Template>"),
                ["round.ui"] = Doc("round.ui",
                    "<Import src='tabs.ui'/><Screen name='Round'><TabBar>" +
                    "<FlagTab id='flagDesign' label='战舰设计' caption='DESIGN'/>" +
                    "<FlagTab id='flagResearch' label='研究' caption='RESEARCH'/>" +
                    "</TabBar></Screen>"),
            };
            var failures = new List<string>();
            var msgids = ScanEntry("round.ui", files, failures: failures);

            CollectionAssert.IsEmpty(failures);
            CollectionAssert.IsSupersetOf(msgids, new[] { "战舰设计", "DESIGN", "研究", "RESEARCH" });
        }

        [Test]
        public void Scan_NamespacedImport_ExtractsParamValue()
        {
            // <Import as='ui'> makes the template reachable only as <ui.MenuBtn/>.
            var files = new Dictionary<string, UIDocument>
            {
                ["lib.ui"] = Doc("lib.ui",
                    "<Template name='MenuBtn'><Param name='label'/><Btn><Text>{{label}}</Text></Btn></Template>"),
                ["main.ui"] = Doc("main.ui",
                    "<Import src='lib.ui' as='ui'/><Screen name='Main'><ui.MenuBtn label='开始'/></Screen>"),
            };
            Assert.Contains("开始", ScanEntry("main.ui", files));
        }

        [Test]
        public void Scan_StyleOnlyInCommonLibrary_ExtractsParamValue()
        {
            // A common library (PromptUGUISettings.commonLibraries) is the <Import> every
            // document implicitly has — a class= that lives only there must resolve.
            var files = new Dictionary<string, UIDocument>
            {
                ["theme.ui"] = Doc("theme.ui", "<Style name='skin' color='#223344'/>"),
                ["main.ui"] = Doc("main.ui",
                    "<Template name='MenuBtn'><Param name='label'/><Btn class='skin'><Text>{{label}}</Text></Btn></Template>" +
                    "<Screen name='Main'><MenuBtn label='开始'/></Screen>"),
            };
            var failures = new List<string>();
            var msgids = ScanEntry("main.ui", files, new[] { new ImportRef("theme.ui", null) }, failures);

            CollectionAssert.IsEmpty(failures);
            Assert.Contains("开始", msgids);
        }

        [Test]
        public void Scan_ExpansionFails_ReportsReason_AndStillExtractsRawText()
        {
            // A document the runtime could not open either: say why instead of silently
            // dropping every invocation-site value, and keep what the raw tree still offers.
            var files = new Dictionary<string, UIDocument>
            {
                ["main.ui"] = Doc("main.ui",
                    "<Screen name='Main'><Text>标题</Text><Frame class='nope'/></Screen>"),
            };
            var failures = new List<string>();
            var msgids = ScanEntry("main.ui", files, failures: failures);

            Assert.AreEqual(1, failures.Count);
            StringAssert.Contains("unknown style 'nope'", failures[0]);
            Assert.Contains("标题", msgids);
        }

        [Test]
        public void Scan_NoImportClosure_SkipsExpansionWithoutReporting()
        {
            // imports == null means the caller could not produce the closure (and says so
            // itself). Expanding against half the document would only report phantom
            // unknown names, so the scan walks the raw tree quietly.
            var doc = Doc("main.ui",
                "<Import src='lib.ui'/><Screen name='Main'><Text>标题</Text><Hint msg='x'/></Screen>");
            var failures = new List<string>();
            var msgids = XmlStringScanner.Scan(doc, "p", "main.ui", imports: null, commons: null,
                                               onExpansionFailed: failures.Add)
                .Select(e => e.Msgid).ToList();

            CollectionAssert.IsEmpty(failures);
            Assert.Contains("标题", msgids);
        }

        [Test]
        public void Scan_RealWorldTopIconBtn_ExtractsLabel()
        {
            // Mirrors a real authoring shape the user hit: Template body has a
            // multi-line <Text>{{label}}\n  </Text> nested inside VStack>Btn/Text,
            // invocation is in a Screen. Verify the substituted label value
            // surfaces as a msgid.
            var xml = @"<PromptUGUI version='1'>
                <Template name='TopIconBtn'>
                    <Param name='icon'/>
                    <Param name='label'/>
                    <VStack width='128' height='184' spacing='15'>
                        <Btn id='btn' size='96x96' color='#1F3A5FCC'>
                            <Icon name='{{icon}}' anchor='center' size='64x64' color='#D6E1F0'/>
                        </Btn>
                        <Text height='33'
                              fontSize='28' color='#C5D2E5' align='center'>{{label}}
                        </Text>
                    </VStack>
                </Template>
                <Screen name='Main'>
                    <TopIconBtn id='accountBtn'
                                icon='Solar32Bold:Users/User Circle'
                                label='切换账号'/>
                </Screen>
            </PromptUGUI>";
            var msgids = XmlStringScanner.Scan(xml, "screens/Main")
                .Select(e => e.Msgid).ToList();
            Assert.Contains("切换账号", msgids);
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

        // ── author comments → #. translator notes (spec 2026-09-30-i18n-xml-comments §3.3) ─────────
        // Multi-line fixtures: a comment only counts when it opens its own line.

        private static string ML(params string[] lines) =>
            "<PromptUGUI version='1'>\n" + string.Join("\n", lines) + "\n</PromptUGUI>";

        private static List<string> CommentsOf(IEnumerable<ExtractedString> found, string msgid) =>
            found.Where(e => e.Msgid == msgid).SelectMany(e => e.ExtractedComments).ToList();

        private const string Tmp = "Contains TMP rich text tags. Preserve tags and attribute values verbatim.";

        [Test]
        public void Scan_CommentAboveText_BecomesTranslatorNote()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Screen name='Main'>",
                "  <!-- 主菜单的开始按钮 -->",
                "  <Btn id='play'>开始</Btn>",
                "</Screen>"), "p").ToList();
            Assert.Contains("主菜单的开始按钮", CommentsOf(found, "开始"));
        }

        [Test]
        public void Scan_CallSiteComment_ReachesEveryArgumentOfThatInvocation()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Template name='FlagTab'>",
                "  <Param name='label'/>",
                "  <Param name='caption'/>",
                "  <Tab id='tab'>",
                "    <Text>{{label}}</Text>",
                "    <Text>{{caption}}</Text>",
                "  </Tab>",
                "</Template>",
                "<Screen name='Round'>",
                "  <TabBar>",
                "    <!-- label是按钮位置，caption是装饰文字 -->",
                "    <FlagTab id='flagDesign' label='战舰设计' caption='DESIGN'/>",
                "  </TabBar>",
                "</Screen>"), "p").ToList();
            Assert.Contains("label是按钮位置，caption是装饰文字", CommentsOf(found, "战舰设计"));
            Assert.Contains("label是按钮位置，caption是装饰文字", CommentsOf(found, "DESIGN"));
        }

        [Test]
        public void Scan_SlotCommentInImportedTemplate_ComesBeforeTheCallSiteComment()
        {
            var files = new Dictionary<string, UIDocument>
            {
                ["lib.ui"] = UIDocumentParser.Parse(ML(
                    "<Template name='FlagTab'>",
                    "  <Param name='label'/>",
                    "  <Param name='caption'/>",
                    "  <Tab id='tab'>",
                    "    <Text>{{label}}</Text>",
                    "    <!-- 装饰小字 -->",
                    "    <Text>{{caption}}</Text>",
                    "  </Tab>",
                    "</Template>"), "lib.ui"),
                ["main.ui"] = UIDocumentParser.Parse(ML(
                    "<Import src='lib.ui'/>",
                    "<Screen name='Round'>",
                    "  <TabBar>",
                    "    <!-- 调用点说明 -->",
                    "    <FlagTab id='flagDesign' label='战舰设计' caption='DESIGN'/>",
                    "  </TabBar>",
                    "</Screen>"), "main.ui"),
            };
            var found = XmlStringScanner.Scan(files["main.ui"], "p", "main.ui",
                                              s => files.TryGetValue(s, out var d) ? d : null, commons: null).ToList();

            var design = CommentsOf(found, "DESIGN");
            Assert.Less(design.IndexOf("装饰小字"), design.IndexOf("调用点说明"));
            Assert.GreaterOrEqual(design.IndexOf("装饰小字"), 0);

            var label = CommentsOf(found, "战舰设计");
            Assert.Contains("调用点说明", label);
            CollectionAssert.DoesNotContain(label, "装饰小字");
        }

        [Test]
        public void Scan_StaticTemplateText_KeepsItsOwnComment_NotTheCallSites()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Template name='Dialog'>",
                "  <Param name='title'/>",
                "  <Frame>",
                "    <Text>{{title}}</Text>",
                "    <!-- 确认按钮 -->",
                "    <Btn>确定</Btn>",
                "  </Frame>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 购买弹窗 -->",
                "  <Dialog title='购买'/>",
                "</Screen>"), "p").ToList();

            var ok = CommentsOf(found, "确定");
            Assert.Contains("确认按钮", ok);
            CollectionAssert.DoesNotContain(ok, "购买弹窗");
            Assert.Contains("购买弹窗", CommentsOf(found, "购买"));
        }

        [Test]
        public void Scan_StaticSlotContent_DoesNotTakeTheCallSiteComment()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Template name='Panel'>",
                "  <Param name='title'/>",
                "  <Frame>",
                "    <Text>{{title}}</Text>",
                "    <Slot/>",
                "  </Frame>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 购买面板 -->",
                "  <Panel title='购买'>",
                "    <Text>确定花费吗？</Text>",
                "  </Panel>",
                "</Screen>"), "p").ToList();

            CollectionAssert.DoesNotContain(CommentsOf(found, "确定花费吗？"), "购买面板");
            Assert.Contains("购买面板", CommentsOf(found, "购买"));
        }

        [Test]
        public void Scan_NestedInvocations_BothCallSites_InnerFirst()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Template name='Inner'>",
                "  <Param name='label'/>",
                "  <Btn>{{label}}</Btn>",
                "</Template>",
                "<Template name='Outer'>",
                "  <Param name='label'/>",
                "  <Frame>",
                "    <!-- 内层调用点 -->",
                "    <Inner label='{{label}}'/>",
                "  </Frame>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 外层调用点 -->",
                "  <Outer label='出发'/>",
                "</Screen>"), "p").ToList();

            var go = CommentsOf(found, "出发");
            Assert.GreaterOrEqual(go.IndexOf("内层调用点"), 0);
            Assert.Less(go.IndexOf("内层调用点"), go.IndexOf("外层调用点"));
        }

        [Test]
        public void Scan_ContainerComment_DoesNotReachTheTextInside()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Screen name='S'>",
                "  <!-- 顶栏 -->",
                "  <Frame>",
                "    <Text>标题</Text>",
                "  </Frame>",
                "</Screen>"), "p").ToList();
            CollectionAssert.DoesNotContain(CommentsOf(found, "标题"), "顶栏");
        }

        [Test]
        public void Scan_TrailingComment_DoesNotReachTheNextLine()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Screen name='S'>",
                "  <HStack>",
                "    <Frame width='stretch'/> <!-- spacer：吃掉中间剩余空间 -->",
                "    <Btn id='apply'>应用</Btn>",
                "  </HStack>",
                "</Screen>"), "p").ToList();
            Assert.IsFalse(CommentsOf(found, "应用").Any(c => c.Contains("spacer")));
        }

        [Test]
        public void Scan_TemplateRootHostsTheText_StillTakesTheCallSiteComment()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Template name='MenuBtn'>",
                "  <Param name='label'/>",
                "  <Btn>{{label}}</Btn>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 主菜单 -->",
                "  <MenuBtn label='开始'/>",
                "</Screen>"), "p").ToList();
            Assert.Contains("主菜单", CommentsOf(found, "开始"));
        }

        [Test]
        public void Scan_TextAttributeSlot_TakesBothComments()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Template name='MenuBtn'>",
                "  <Param name='label'/>",
                "  <Frame>",
                "    <!-- 按钮文字 -->",
                "    <Btn text='{{label}}'/>",
                "  </Frame>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 主菜单 -->",
                "  <MenuBtn label='开始'/>",
                "</Screen>"), "p").ToList();
            CollectionAssert.IsSupersetOf(CommentsOf(found, "开始"), new[] { "按钮文字", "主菜单" });
        }

        [Test]
        public void Scan_ParamlessTemplate_TextKeepsItsComment_NotTheCallSites()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Template name='Hint'>",
                "  <!-- 提示语 -->",
                "  <Text>请稍候</Text>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 调用点 -->",
                "  <Hint/>",
                "</Screen>"), "p").ToList();
            var wait = CommentsOf(found, "请稍候");
            Assert.Contains("提示语", wait);
            CollectionAssert.DoesNotContain(wait, "调用点");
        }

        [Test]
        public void Scan_CommentOrder_AmbientSiblingSlotCallSiteThenTmp()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Template name='Card'>",
                "  <Param name='label'/>",
                "  <Param name='sub'/>",
                "  <Frame>",
                "    <Text>{{sub}}</Text>",
                "    <!-- 槽位 -->",
                "    <Text>{{label}}</Text>",
                "  </Frame>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 调用点 -->",
                "  <Card label='&lt;color=#ff0&gt;警告&lt;/color&gt;' sub='副标题'/>",
                "</Screen>"), "p").ToList();
            var entry = found.Single(e => e.Msgid == "<color=#ff0>警告</color>");
            CollectionAssert.AreEqual(
                new[] { "S screen, Text text", "sibling: 副标题", "槽位", "调用点", Tmp },
                entry.ExtractedComments);
        }

        [Test]
        public void Scan_ExpansionFallsBackToRawTree_DirectTextKeepsItsComment()
        {
            var found = XmlStringScanner.Scan(ML(
                "<Screen name='Main'>",
                "  <!-- 标题说明 -->",
                "  <Text>标题</Text>",
                "  <Frame class='nope'/>",
                "</Screen>"), "p").ToList();
            Assert.Contains("标题说明", CommentsOf(found, "标题"));
        }
    }
}
