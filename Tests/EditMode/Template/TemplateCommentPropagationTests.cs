using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Tests.Template
{
    /// <summary>
    /// Expansion carries <see cref="ElementNode.LeadingComments"/> on every copy and records the
    /// comments above each template invocation on its instance root
    /// (<see cref="ElementNode.InvocationComments"/>, innermost first) — spec
    /// 2026-09-30-i18n-xml-comments §3.2. A missed copy site drops comments silently, so each one has
    /// a case here. Fixtures are multi-line: a comment only counts when it opens its own line.
    /// </summary>
    public class TemplateCommentPropagationTests
    {
        private static UIDocument Doc(params string[] lines) =>
            UIDocumentParser.Parse(
                "<PromptUGUI version='1'>\n" + string.Join("\n", lines) + "\n</PromptUGUI>");

        private static ElementNode ExpandedRoot(UIDocument doc) =>
            TemplateExpander.Expand(doc).Screens[0].Root;

        [Test]
        public void ScreenNode_KeepsItsComments()
        {
            var root = ExpandedRoot(Doc(
                "<Screen name='S'>",
                "  <!-- 标题 -->",
                "  <Text id='t'>x</Text>",
                "</Screen>"));
            CollectionAssert.AreEqual(new[] { "标题" }, root.Children[0].LeadingComments);
        }

        [Test]
        public void ScreenNodeWithClass_KeepsItsComments()
        {
            var root = ExpandedRoot(Doc(
                "<Style name='skin' color='#223344'/>",
                "<Screen name='S'>",
                "  <!-- 标题 -->",
                "  <Text id='t' class='skin'>x</Text>",
                "</Screen>"));
            CollectionAssert.AreEqual(new[] { "标题" }, root.Children[0].LeadingComments);
        }

        [Test]
        public void TemplateBodyNode_CommentReachesTheExpandedNode()
        {
            var root = ExpandedRoot(Doc(
                "<Template name='Card'>",
                "  <Param name='label'/>",
                "  <Frame>",
                "    <!-- 槽位说明 -->",
                "    <Text>{{label}}</Text>",
                "  </Frame>",
                "</Template>",
                "<Screen name='S'>",
                "  <Card id='c' label='x'/>",
                "</Screen>"));
            CollectionAssert.AreEqual(new[] { "槽位说明" }, root.Children[0].Children[0].LeadingComments);
        }

        [Test]
        public void InvocationComments_LandOnInstanceRoot_WhichKeepsTheTemplatesOwnComment()
        {
            var root = ExpandedRoot(Doc(
                "<Template name='Card'>",
                "  <Param name='label'/>",
                "  <!-- 模板根 -->",
                "  <Frame><Text>{{label}}</Text></Frame>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 调用点 -->",
                "  <Card id='c' label='x'/>",
                "</Screen>"));
            var instance = root.Children[0];
            Assert.IsTrue(instance.IsTemplateInstanceRoot);
            CollectionAssert.AreEqual(new[] { "调用点" }, instance.InvocationComments);
            CollectionAssert.AreEqual(new[] { "模板根" }, instance.LeadingComments);
        }

        [Test]
        public void BodyRootThatIsItselfAnInvocation_InnerCallSiteFirst()
        {
            var root = ExpandedRoot(Doc(
                "<Template name='Inner'>",
                "  <Param name='label'/>",
                "  <Frame><Text>{{label}}</Text></Frame>",
                "</Template>",
                "<Template name='Outer'>",
                "  <Param name='label'/>",
                "  <!-- 内层调用点 -->",
                "  <Inner label='{{label}}'/>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 外层调用点 -->",
                "  <Outer id='o' label='x'/>",
                "</Screen>"));
            CollectionAssert.AreEqual(new[] { "内层调用点", "外层调用点" }, root.Children[0].InvocationComments);
        }

        [Test]
        public void SlotContent_KeepsItsOwnComments()
        {
            var root = ExpandedRoot(Doc(
                "<Template name='Panel'><Frame><Slot/></Frame></Template>",
                "<Screen name='S'>",
                "  <Panel id='p'>",
                "    <!-- 槽内文字 -->",
                "    <Text id='t'>x</Text>",
                "  </Panel>",
                "</Screen>"));
            CollectionAssert.AreEqual(new[] { "槽内文字" }, root.Children[0].Children[0].LeadingComments);
        }

        [Test]
        public void SlotContentThatIsAnInstance_KeepsItsInvocationComments()
        {
            var root = ExpandedRoot(Doc(
                "<Template name='Panel'><Frame><Slot/></Frame></Template>",
                "<Template name='Card'>",
                "  <Param name='label'/>",
                "  <Frame><Text>{{label}}</Text></Frame>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 面板 -->",
                "  <Panel id='p'>",
                "    <!-- 卡片 -->",
                "    <Card id='c' label='x'/>",
                "  </Panel>",
                "</Screen>"));
            var panel = root.Children[0];
            CollectionAssert.AreEqual(new[] { "面板" }, panel.InvocationComments);
            CollectionAssert.AreEqual(new[] { "卡片" }, panel.Children[0].InvocationComments);
        }

        [Test]
        public void InvocationWithClass_CommentStillReachesInstanceRoot()
        {
            var root = ExpandedRoot(Doc(
                "<Style name='wide' width='100'/>",
                "<Template name='Card'>",
                "  <Param name='label'/>",
                "  <Frame><Text>{{label}}</Text></Frame>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 调用点 -->",
                "  <Card id='c' class='wide' label='x'/>",
                "</Screen>"));
            CollectionAssert.AreEqual(new[] { "调用点" }, root.Children[0].InvocationComments);
        }

        [Test]
        public void NestedInvocationBelowTheBodyRoot_GetsTheInnerCallSite()
        {
            var root = ExpandedRoot(Doc(
                "<Template name='Inner'>",
                "  <Param name='label'/>",
                "  <Text>{{label}}</Text>",
                "</Template>",
                "<Template name='Outer'>",
                "  <Param name='label'/>",
                "  <Frame>",
                "    <!-- 内层 -->",
                "    <Inner label='{{label}}'/>",
                "  </Frame>",
                "</Template>",
                "<Screen name='S'>",
                "  <!-- 外层 -->",
                "  <Outer id='o' label='x'/>",
                "</Screen>"));
            var outer = root.Children[0];
            CollectionAssert.AreEqual(new[] { "外层" }, outer.InvocationComments);
            CollectionAssert.AreEqual(new[] { "内层" }, outer.Children[0].InvocationComments);
        }

        [Test]
        public void SharedTemplate_ExpandedRepeatedly_NeverAccumulates()
        {
            // One parsed library shared by two documents — what ImportClosure.Cache and the commons
            // pool do. Appending in place would grow these lists on every expansion.
            var lib = Doc(
                "<Template name='Card'>",
                "  <Param name='label'/>",
                "  <!-- 模板根 -->",
                "  <Frame><Text>{{label}}</Text></Frame>",
                "</Template>");
            var card = lib.Templates["Card"];
            var bodyComments = card.Body.LeadingComments;

            UIDocument Screen(string name) => Doc(
                $"<Screen name='{name}'>",
                $"  <!-- {name} 调用点 -->",
                "  <Card id='c' label='x'/>",
                "</Screen>");
            var a = Screen("A");
            var b = Screen("B");
            a.Templates["Card"] = card;
            b.Templates["Card"] = card;

            for (var i = 0; i < 2; i++)
            {
                CollectionAssert.AreEqual(new[] { "A 调用点" }, ExpandedRoot(a).Children[0].InvocationComments);
                CollectionAssert.AreEqual(new[] { "B 调用点" }, ExpandedRoot(b).Children[0].InvocationComments);
            }
            Assert.AreSame(bodyComments, card.Body.LeadingComments);
            CollectionAssert.AreEqual(new[] { "模板根" }, card.Body.LeadingComments);
            CollectionAssert.AreEqual(new[] { "A 调用点" }, a.Screens[0].Root.Children[0].LeadingComments);
        }
    }
}
