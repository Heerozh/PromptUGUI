using System.Linq;
using System.Xml;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.Parser
{
    /// <summary>
    /// Author comments directly above an element become <see cref="ElementNode.LeadingComments"/> — the
    /// raw material of i18n translator notes (spec 2026-09-30-i18n-xml-comments §3.1). Every fixture is
    /// multi-line on purpose: "starts its own line" is part of the rule, and a one-line fixture puts
    /// <c>&lt;Screen …&gt;</c> in front of every comment.
    /// </summary>
    public class LeadingCommentParseTests
    {
        private static UIDocument Parse(string nl, params string[] lines) =>
            UIDocumentParser.Parse(string.Join(nl, lines));

        private static ElementNode ScreenRoot(params string[] body) =>
            Parse("\n", new[] { "<PromptUGUI version='1'>", "<Screen name='S'>" }
                .Concat(body)
                .Concat(new[] { "</Screen>", "</PromptUGUI>" })
                .ToArray()).Screens[0].Root;

        private static ElementNode Child(ElementNode root, string id) =>
            root.Children.Single(c => c.Id == id);

        [Test]
        public void CommentDirectlyAbove_IsLeadingComment()
        {
            var root = ScreenRoot(
                "  <!-- 开始按钮 -->",
                "  <Btn id='b'>开始</Btn>");
            CollectionAssert.AreEqual(new[] { "开始按钮" }, Child(root, "b").LeadingComments);
        }

        [Test]
        public void MultiLineComment_OneEntryPerNonEmptyTrimmedLine()
        {
            var root = ScreenRoot(
                "  <!-- 第一行",
                "",
                "       第二行   -->",
                "  <Text id='t'>x</Text>");
            CollectionAssert.AreEqual(new[] { "第一行", "第二行" }, Child(root, "t").LeadingComments);
        }

        [Test]
        public void ConsecutiveComments_AllCollected_InSourceOrder()
        {
            var root = ScreenRoot(
                "  <!-- a -->",
                "  <!-- b -->",
                "  <Text id='t'>x</Text>");
            CollectionAssert.AreEqual(new[] { "a", "b" }, Child(root, "t").LeadingComments);
        }

        [Test]
        public void CommentBelongsOnlyToTheElementRightBelowIt()
        {
            var root = ScreenRoot(
                "  <!-- a -->",
                "  <Text id='t1'>x</Text>",
                "  <Text id='t2'>y</Text>");
            CollectionAssert.AreEqual(new[] { "a" }, Child(root, "t1").LeadingComments);
            Assert.IsNull(Child(root, "t2").LeadingComments);
        }

        [Test]
        public void TrailingComment_IsNotLeadingCommentOfNextLine()
        {
            var root = ScreenRoot(
                "  <Frame id='f'/> <!-- spacer -->",
                "  <Text id='t'>x</Text>");
            Assert.IsNull(Child(root, "t").LeadingComments);
        }

        [Test]
        public void TrailingComment_StopsTheBlock_OwnLineCommentsAfterItStillCount()
        {
            var root = ScreenRoot(
                "  <Frame id='f'/> <!-- t -->",
                "  <!-- o -->",
                "  <Text id='t'>x</Text>");
            CollectionAssert.AreEqual(new[] { "o" }, Child(root, "t").LeadingComments);
        }

        [Test]
        public void BlankLineBetweenCommentAndElement_StillAttached()
        {
            var root = ScreenRoot(
                "  <!-- a -->",
                "",
                "  <Text id='t'>x</Text>");
            CollectionAssert.AreEqual(new[] { "a" }, Child(root, "t").LeadingComments);
        }

        [Test]
        public void TemplateBodyRoot_TakesCommentAfterParams_NotTheOneAboveAParam()
        {
            var doc = Parse("\n",
                "<PromptUGUI version='1'>",
                "  <Template name='T'>",
                "    <!-- about p -->",
                "    <Param name='p'/>",
                "    <!-- root note -->",
                "    <Btn>{{p}}</Btn>",
                "  </Template>",
                "</PromptUGUI>");
            CollectionAssert.AreEqual(new[] { "root note" }, doc.Templates["T"].Body.LeadingComments);
        }

        [Test]
        public void VariantAddChild_TakesItsComment()
        {
            var doc = Parse("\n",
                "<PromptUGUI version='1'>",
                "<Screen name='S'>",
                "  <Frame id='host'/>",
                "  <Variant when='portrait'>",
                "    <Add into='host'>",
                "      <!-- 竖屏提示 -->",
                "      <Text id='hint'>x</Text>",
                "    </Add>",
                "  </Variant>",
                "</Screen>",
                "</PromptUGUI>");
            var hint = doc.Screens[0].Variants[0].Adds[0].Children[0];
            CollectionAssert.AreEqual(new[] { "竖屏提示" }, hint.LeadingComments);
        }

        [Test]
        public void NoComment_IsNull()
        {
            var root = ScreenRoot("  <Text id='t'>x</Text>");
            Assert.IsNull(Child(root, "t").LeadingComments);
        }

        [Test]
        public void CommentInsideText_StaysOutOfContent_AndIsNoLeadingComment()
        {
            var root = ScreenRoot("  <Text id='t'>a<!-- x -->b</Text>");
            var t = Child(root, "t");
            Assert.AreEqual("ab", t.TextContent);
            Assert.IsNull(t.LeadingComments);
        }

        [TestCase("\r\n")]
        [TestCase("\r")]
        public void OtherLineBreaks_SameResultAsLf(string nl)
        {
            var root = Parse(nl,
                "<PromptUGUI version='1'>",
                "<Screen name='S'>",
                "  <!-- own -->",
                "  <Text id='t1'>x</Text>",
                "  <Frame id='f'/> <!-- trailing -->",
                "  <Text id='t2'>y</Text>",
                "</Screen>",
                "</PromptUGUI>").Screens[0].Root;
            CollectionAssert.AreEqual(new[] { "own" }, Child(root, "t1").LeadingComments);
            Assert.IsNull(Child(root, "t2").LeadingComments);
        }

        [Test]
        public void TwoCommentsOnOneOwnLine_BothCount()
        {
            var root = ScreenRoot(
                "  <!-- a --> <!-- b -->",
                "  <Text id='t'>x</Text>");
            CollectionAssert.AreEqual(new[] { "a", "b" }, Child(root, "t").LeadingComments);
        }

        [Test]
        public void CommentAfterContainerOpenTag_IsNotLeadingCommentOfFirstChild()
        {
            var root = ScreenRoot(
                "  <Frame id='f'> <!-- c -->",
                "    <Text id='t'>x</Text>",
                "  </Frame>");
            Assert.IsNull(Child(root, "f").Children.Single().LeadingComments);
        }

        [Test]
        public void EmptyComment_YieldsNothing()
        {
            var root = ScreenRoot(
                "  <!-- -->",
                "  <Text id='t'>x</Text>");
            Assert.IsNull(Child(root, "t").LeadingComments);
        }

        [Test]
        public void PreservedWhitespace_BetweenCommentAndElement_IsSkipped()
        {
            var root = ScreenRoot(
                "  <Frame id='f' xml:space='preserve'>",
                "    <!-- a -->",
                "    <Text id='t'>x</Text>",
                "  </Frame>");
            CollectionAssert.AreEqual(new[] { "a" }, Child(root, "f").Children.Single().LeadingComments);
        }

        [Test]
        public void LineInfoComment_CarriesPosition_AndWhetherItStartsItsLine()
        {
            var doc = (LineInfoXmlDocument)LineInfoXmlDocument.Parse(string.Join("\n",
                "<R>",
                "  <A/> <!-- trailing -->",
                "    <!-- own -->",
                "</R>"));
            var comments = doc.SelectNodes("//comment()").Cast<XmlNode>().Cast<LineInfoComment>().ToList();

            Assert.IsTrue(doc.HasComments);
            Assert.AreEqual(2, comments.Count);
            Assert.AreEqual(2, comments[0].Line);
            Assert.AreEqual(12, comments[0].Column);   // first char after "<!--"
            Assert.IsFalse(comments[0].StartsLine);
            Assert.AreEqual(3, comments[1].Line);
            Assert.AreEqual(9, comments[1].Column);
            Assert.IsTrue(comments[1].StartsLine);
        }

        [Test]
        public void DocumentWithoutComments_HasCommentsIsFalse()
        {
            var doc = (LineInfoXmlDocument)LineInfoXmlDocument.Parse("<R>\n  <A/>\n</R>");
            Assert.IsFalse(doc.HasComments);
        }
    }
}
