using NUnit.Framework;
using PromptUGUI;
using PromptUGUI.IR;
using PromptUGUI.MarkdigBackend;

namespace PromptUGUI.Tests.Markdown
{
    public class MarkdigRendererTests
    {
        private static ElementNode Render(string md)
            => new MarkdigRenderer().Render(md, MarkdownStyle.CreateDefault()).Root;

        // depth-first find first node with tag whose TextContent contains `needle`
        private static ElementNode Find(ElementNode n, string tag, string needle)
        {
            if (n.Tag == tag && n.TextContent != null && n.TextContent.Contains(needle)) return n;
            foreach (var c in n.Children) { var r = Find(c, tag, needle); if (r != null) return r; }
            return null;
        }

        [Test]
        public void Empty_returns_vstack_root_no_children()
        {
            var root = Render("");
            Assert.AreEqual("VStack", root.Tag);
            Assert.AreEqual(0, root.Children.Count);
        }

        [Test]
        public void Heading_becomes_bold_text()
        {
            var root = Render("# Title");
            var t = Find(root, "Text", "Title");
            Assert.IsNotNull(t);
            StringAssert.Contains("<b>", t.TextContent);
        }

        [Test]
        public void Paragraph_inline_maps_to_tmp_tags_and_escapes()
        {
            var root = Render("a **b** _c_ ~~d~~ `e` 1<2 & 3");
            var t = Find(root, "Text", "b</b>");
            Assert.IsNotNull(t);
            StringAssert.Contains("<i>c</i>", t.TextContent);
            StringAssert.Contains("<s>d</s>", t.TextContent);
            StringAssert.Contains("<mark=", t.TextContent);    // inline code background
            StringAssert.Contains("&lt;", t.TextContent);       // escaped '<'
            StringAssert.Contains("&amp;", t.TextContent);      // escaped '&'
        }
    }
}
