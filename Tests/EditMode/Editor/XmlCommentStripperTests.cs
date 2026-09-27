using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using NUnit.Framework;
using PromptUGUI.Editor;
using PromptUGUI.Parser;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace PromptUGUI.Tests.Editor
{
    /// <summary>
    /// spec 2026-09-27-ui-xml-comment-stripping §3.2 / §5-1 ~ 10。「树相同」= 原文与输出各
    /// <see cref="LineInfoXmlDocument.Parse"/> 一次，逐元素比名字、属性、直接文本、<c>Line</c>——
    /// <c>UIDocumentParser</c> 的 IR 只由这几样派生。
    /// </summary>
    public class XmlCommentStripperTests
    {
        [Test]
        public void Comments_between_elements_go_and_every_element_keeps_its_line()
        {
            var xml = X(
                "<PromptUGUI version=\"1\">",
                "  <!-- one line -->",
                "  <Screen name=\"A\">",
                "    <!-- two",
                "         lines -->",
                "    <Frame id=\"f\"/>",
                "    <!--",
                "      three",
                "      lines",
                "    -->",
                "    <Text id=\"t\">hi</Text>",
                "  </Screen>",
                "</PromptUGUI>");

            var stripped = XmlCommentStripper.Strip(xml, out var why);

            Assert.IsNull(why);
            StringAssert.DoesNotContain("<!--", stripped);
            Assert.AreEqual(LineCount(xml), LineCount(stripped));
            AssertSameTree(xml, stripped);
        }

        [Test]
        public void The_header_comment_before_the_root_and_a_comment_after_it_go_too()
        {
            var xml = X(
                "<?xml version=\"1.0\" encoding=\"utf-8\"?>",
                "<!--",
                "  PromptUGUI's `.ui.xml` File",
                "-->",
                "<PromptUGUI version=\"1\">",
                "  <Screen name=\"A\"/>",
                "</PromptUGUI>",
                "<!-- trailing -->",
                "");

            var stripped = XmlCommentStripper.Strip(xml, out var why);

            Assert.IsNull(why);
            StringAssert.StartsWith("<?xml version=\"1.0\" encoding=\"utf-8\"?>\n", stripped);
            StringAssert.DoesNotContain("<!--", stripped);
            AssertSameTree(xml, stripped);
        }

        [Test]
        public void Comment_markers_inside_CDATA_or_a_processing_instruction_are_left_alone()
        {
            var xml = X(
                "<PromptUGUI version=\"1\">",
                "  <?note <!-- not a comment --> ?>",
                "  <Screen name=\"A\">",
                "    <Text id=\"t\"><![CDATA[a <!-- b --> c]]></Text>",
                "  </Screen>",
                "  <!-- real -->",
                "</PromptUGUI>");

            var stripped = XmlCommentStripper.Strip(xml, out var why);

            Assert.IsNull(why);
            StringAssert.Contains("<?note <!-- not a comment --> ?>", stripped);
            StringAssert.Contains("<![CDATA[a <!-- b --> c]]>", stripped);
            StringAssert.DoesNotContain("<!-- real -->", stripped);
            AssertSameTree(xml, stripped);
        }

        [Test]
        public void A_quoted_greater_than_sign_does_not_end_the_tag()
        {
            // 把 `if="a >` 当成标签结尾的扫描器会把后面那段当成文字，注释的换行就被挪到 g 之后。
            var xml = X(
                "<PromptUGUI version=\"1\">",
                "  <Screen name=\"A\">",
                "    <Frame id=\"f\" if=\"a > b\" title='x > \"y\"'/><!-- after",
                "      the tag -->",
                "    <Frame id=\"g\"/>",
                "  </Screen>",
                "</PromptUGUI>");

            var stripped = XmlCommentStripper.Strip(xml, out var why);

            Assert.IsNull(why);
            StringAssert.DoesNotContain("<!--", stripped);
            AssertSameTree(xml, stripped);
        }

        [Test]
        public void A_comment_inside_text_leaves_the_text_as_it_was_and_later_lines_where_they_were()
        {
            var xml = X(
                "<PromptUGUI version=\"1\">",
                "  <Screen name=\"A\">",
                "    <Text id=\"a\">abc<!-- one --> def</Text>",
                "    <Text id=\"b\">abc<!-- two",
                "      lines -->def</Text>",
                "    <Text id=\"c\">abc<!-- x -->",
                "      <!-- y -->def</Text>",
                "    <Text id=\"d\">",
                "      line1",
                "      <!-- note",
                "      -->",
                "      line2",
                "    </Text>",
                "    <Frame id=\"after\"/>",
                "  </Screen>",
                "</PromptUGUI>");

            var stripped = XmlCommentStripper.Strip(xml, out var why);

            Assert.IsNull(why);
            StringAssert.DoesNotContain("<!--", stripped);
            AssertSameTree(xml, stripped);
            // 两段注释之间的空白原文里是被丢弃的 Whitespace 节点；注释删了它也不能并进文字
            Assert.AreEqual("abcdef", TextOf(stripped, "c"));
        }

        [TestCase("<A><!-- never closed</A>", "comment")]
        [TestCase("<A><!-- a -- b --></A>", "comment")]
        [TestCase("<A><!--->x</A>", "comment")]
        [TestCase("<A><![CDATA[x</A><!-- c -->", "CDATA")]
        [TestCase("<?pi never closed <A><!-- c --></A>", "processing instruction")]
        [TestCase("<A title=\"x><!-- c --></A>", "tag")]
        [TestCase("<!DOCTYPE A><A><!-- c --></A>", "DOCTYPE")]
        [TestCase("<!-- c --><?xml version=\"1.0\"?><A/>", "XML declaration")]
        public void Malformed_input_comes_back_as_the_same_string(string xml, string reason)
        {
            var result = XmlCommentStripper.Strip(xml, out var why);

            Assert.AreSame(xml, result);
            Assert.IsNotNull(why);
            StringAssert.Contains(reason, why);
        }

        [Test]
        public void The_XML_declaration_after_a_comment_is_an_error_the_stripper_must_not_fix()
        {
            // 上一条用例的依据：原文本来就解析失败
            Assert.Throws<XmlException>(() => LineInfoXmlDocument.Parse("<!-- c --><?xml version=\"1.0\"?><A/>"));
        }

        [Test]
        public void Line_endings_stay_CRLF()
        {
            var xml = X(
                "<PromptUGUI version=\"1\">",
                "  <!-- two",
                "       lines -->",
                "  <Screen name=\"A\"/>",
                "</PromptUGUI>").Replace("\n", "\r\n");

            var stripped = XmlCommentStripper.Strip(xml, out var why);

            Assert.IsNull(why);
            StringAssert.DoesNotContain("<!--", stripped);
            Assert.AreEqual(LineCount(xml), LineCount(stripped));
            Assert.IsFalse(Regex.IsMatch(stripped, "(?<!\r)\n"), "a bare LF crept in:\n" + stripped);
            AssertSameTree(xml, stripped);
        }

        [Test]
        public void A_file_that_uses_xml_space_is_left_as_written()
        {
            var xml = X(
                "<PromptUGUI version=\"1\">",
                "  <Screen name=\"A\">",
                "    <Text id=\"t\" xml:space=\"preserve\">a<!-- c -->",
                "    <!-- d --> b</Text>",
                "  </Screen>",
                "</PromptUGUI>");

            var result = XmlCommentStripper.Strip(xml, out var why);

            Assert.AreSame(xml, result);
            Assert.IsNotNull(why);
            StringAssert.Contains("xml:space", why);
        }

        [Test]
        public void Nothing_to_strip_returns_the_same_string()
        {
            var plain = X("<PromptUGUI version=\"1\">", "  <Screen name=\"A\"/>", "</PromptUGUI>");
            var onlyInCData = X(
                "<PromptUGUI version=\"1\">",
                "  <Screen name=\"A\"><Text><![CDATA[<!-- x -->]]></Text></Screen>",
                "</PromptUGUI>");

            Assert.AreSame(plain, XmlCommentStripper.Strip(plain, out var whyPlain));
            Assert.IsNull(whyPlain);
            Assert.AreSame(onlyInCData, XmlCommentStripper.Strip(onlyInCData, out var whyCData));
            Assert.IsNull(whyCData);
        }

        [Test]
        public void Every_ui_xml_shipped_with_the_package_strips_cleanly()
        {
            var root = PackageInfo.FindForAssembly(typeof(XmlCommentStripper).Assembly).resolvedPath;
            var files = new[] { "Runtime/Resources", "Samples~" }
                .Select(dir => Path.Combine(root, dir))
                .Where(Directory.Exists)
                .SelectMany(dir => Directory.GetFiles(dir, "*.ui.xml", SearchOption.AllDirectories))
                .ToList();
            Assert.That(files.Count, Is.GreaterThan(10), "corpus not found under " + root);

            foreach (var file in files)
            {
                var xml = File.ReadAllText(file);
                var stripped = XmlCommentStripper.Strip(xml, out var why);

                Assert.IsNull(why, file);
                AssertSameTree(xml, stripped, file);
                Assert.DoesNotThrow(() => UIDocumentParser.Parse(stripped), file);
            }
        }

        // ── helpers ────────────────────────────────────────────────────────────────────────────

        // 显式 "\n" 拼行：测试源文件自己的行尾（autocrlf）不能影响用例
        private static string X(params string[] lines) => string.Join("\n", lines);

        private static int LineCount(string s) => s.Split('\n').Length;

        private static void AssertSameTree(string original, string stripped, string context = "")
        {
            var before = LineInfoXmlDocument.Parse(original);
            var after = LineInfoXmlDocument.Parse(stripped);
            Assert.AreEqual(0, after.SelectNodes("//comment()").Count, $"{context}: comments left in\n{stripped}");
            AssertSameElement(before.DocumentElement, after.DocumentElement, context);
        }

        private static void AssertSameElement(XmlElement a, XmlElement b, string context)
        {
            var where = $"{context} <{a.Name}> at line {Line(a)}";
            Assert.AreEqual(a.Name, b.Name, where);
            Assert.AreEqual(Line(a), Line(b), $"{where}: line");
            Assert.AreEqual(Attributes(a), Attributes(b), $"{where}: attributes");
            Assert.AreEqual(DirectText(a), DirectText(b), $"{where}: text");
            var childrenA = a.ChildNodes.OfType<XmlElement>().ToList();
            var childrenB = b.ChildNodes.OfType<XmlElement>().ToList();
            Assert.AreEqual(childrenA.Count, childrenB.Count, $"{where}: child elements");
            for (var i = 0; i < childrenA.Count; i++) AssertSameElement(childrenA[i], childrenB[i], context);
        }

        private static int Line(XmlElement e) => ((LineInfoElement)e).Line;

        private static string Attributes(XmlElement e) =>
            string.Join("\n", e.Attributes.Cast<XmlAttribute>().Select(a => $"{a.Name}={a.Value}"));

        // 解析器读的是 InnerText（文本 / CDATA / 有意义的空白），注释从来不算
        private static string DirectText(XmlElement e) =>
            string.Concat(e.ChildNodes.OfType<XmlCharacterData>().Where(n => !(n is XmlComment)).Select(n => n.Value));

        private static string TextOf(string xml, string id)
        {
            var doc = LineInfoXmlDocument.Parse(xml);
            return DirectText((XmlElement)doc.SelectSingleNode($"//*[@id='{id}']"));
        }
    }
}
