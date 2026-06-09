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

        // count nodes matching a predicate
        private static int Count(ElementNode n, System.Func<ElementNode, bool> pred)
        {
            int c = pred(n) ? 1 : 0;
            foreach (var ch in n.Children) c += Count(ch, pred);
            return c;
        }

        [Test]
        public void Link_emits_link_tag()
        {
            var root = Render("see [docs](https://x.test)");
            var t = Find(root, "Text", "<link=\"https://x.test\">");
            Assert.IsNotNull(t);
        }

        [Test]
        public void Unordered_list_makes_a_row_per_item_with_bullets()
        {
            var root = Render("- one\n- two\n- three");
            var bullets = Count(root, n => n.Tag == "Text" && n.TextContent != null && n.TextContent.Contains("•"));
            Assert.AreEqual(3, bullets);
        }

        [Test]
        public void Ordered_list_numbers_items()
        {
            var root = Render("1. a\n2. b");
            Assert.IsNotNull(Find(root, "Text", "1."));
            Assert.IsNotNull(Find(root, "Text", "2."));
        }

        [Test]
        public void Task_list_uses_check_glyphs()
        {
            var root = Render("- [x] done\n- [ ] todo");
            Assert.IsNotNull(Find(root, "Text", "☑")); // ☑
            Assert.IsNotNull(Find(root, "Text", "☐")); // ☐
        }
    }

    public class MarkdigRendererBlockTests
    {
        private static ElementNode Render(string md)
            => new MarkdigRenderer().Render(md, MarkdownStyle.CreateDefault()).Root;
        private static ElementNode Find(ElementNode n, string tag, string needle)
        {
            if (n.Tag == tag && ((needle == null) || (n.TextContent != null && n.TextContent.Contains(needle)))) return n;
            foreach (var c in n.Children) { var r = Find(c, tag, needle); if (r != null) return r; }
            return null;
        }

        [Test]
        public void Code_fence_uses_mark_and_code_font()
        {
            var root = Render("```\nint x = 1;\n```");
            var t = Find(root, "Text", "int x = 1;");
            Assert.IsNotNull(t);
            StringAssert.Contains("<mark=", t.TextContent);
        }

        [Test]
        public void Blockquote_has_bar_image()
        {
            var root = Render("> quoted");
            Assert.IsNotNull(Find(root, "Image", null)); // the quote bar
            Assert.IsNotNull(Find(root, "Text", "quoted"));
        }

        [Test]
        public void Thematic_break_is_thin_image()
        {
            var root = Render("a\n\n---\n\nb");
            Assert.IsNotNull(Find(root, "Image", null));
        }

        private static int Count(ElementNode n, System.Func<ElementNode, bool> pred)
        {
            int c = pred(n) ? 1 : 0;
            foreach (var ch in n.Children) c += Count(ch, pred);
            return c;
        }

        [Test]
        public void Table_renders_rows_of_stretch_cells_with_bold_header()
        {
            var root = Render("| A | B |\n|---|---|\n| 1 | 2 |\n| 3 | 4 |");
            // header cells bold
            Assert.IsNotNull(Find(root, "Text", "<b>A</b>"));
            Assert.IsNotNull(Find(root, "Text", "<b>B</b>"));
            // body cells present
            Assert.IsNotNull(Find(root, "Text", "1"));
            Assert.IsNotNull(Find(root, "Text", "4"));
            // 3 rows -> 3 HStacks
            Assert.AreEqual(3, Count(root, n => n.Tag == "HStack"));
        }

        [Test]
        public void Block_image_emits_rawimage_and_image_request()
        {
            var renderer = new MarkdigRenderer();
            var result = renderer.Render("![alt text](https://x.test/a.png)", MarkdownStyle.CreateDefault());
            var raw = Find(result.Root, "RawImage", null);
            Assert.IsNotNull(raw);
            Assert.AreEqual(1, result.Images.Count);
            Assert.AreEqual("https://x.test/a.png", result.Images[0].Url);
            Assert.AreEqual(raw.Id, result.Images[0].NodeId);
        }

        [Test]
        public void Inline_image_bare_name_becomes_sprite_tag()
        {
            var root = Render("coin ![c](coin) here");
            Assert.IsNotNull(Find(root, "Text", "<sprite name=\"coin\">"));
        }

        [Test]
        public void Inline_image_url_falls_back_to_alt_text()
        {
            var root = Render("x ![pic](https://x.test/p.png) y");
            var t = Find(root, "Text", "pic");
            Assert.IsNotNull(t);
            Assert.IsFalse(t.TextContent.Contains("<sprite"));
        }
    }
}
