using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.MarkdigBackend
{
    public sealed class MarkdigRenderer : IMarkdownRenderer
    {
        private static readonly MarkdownPipeline Pipeline =
            new MarkdownPipelineBuilder().UseAdvancedExtensions().Build();

        private MarkdownStyle _style;
        private List<ImageRequest> _images;
        // Sequence counter for generated block-image node ids; consumed when image blocks are rendered.
        private int _imageSeq;

        public MarkdownRenderResult Render(string markdown, MarkdownStyle style)
        {
            _style = style ?? MarkdownStyle.CreateDefault();
            _images = new List<ImageRequest>();
            _imageSeq = 0;

            var root = NewVStack(_style.BlockSpacing);
            root.Attributes["anchor"] = "top-stretch";
            root.Attributes["pivot"] = "0.5,1";

            var doc = Markdig.Markdown.Parse(markdown ?? "", Pipeline);
            foreach (var block in doc)
            {
                var node = RenderBlock(block);
                if (node != null) root.Children.Add(node);
            }
            return new MarkdownRenderResult { Root = root, Images = _images };
        }

        // ---- block dispatch (extended in later tasks) ----
        private ElementNode RenderBlock(Block block)
        {
            switch (block)
            {
                case HeadingBlock h:
                    {
                        // Headings render at BodySize and magnify via RectTransform.localScale (the `scale`
                        // attribute) — the font is never resized, so bitmap/pixel fonts stay crisp. The
                        // V/HStack scale-wrapper + ScaledTextLayoutBridge reserve the visual (scaled) size.
                        var hn = NewText(RenderInline(h.Inline), _style.BodySize, bold: true);
                        var scale = _style.HeadingScales[Mathf.Clamp(h.Level, 1, 6) - 1];
                        if (!Mathf.Approximately(scale, 1f))   // scale 1 → no wrapper needed
                            hn.Attributes["scale"] = scale.ToString(CultureInfo.InvariantCulture);
                        return hn;
                    }
                case ParagraphBlock p:
                    if (IsLoneImage(p, out var blockImg)) return RenderBlockImage(blockImg);
                    return NewText(RenderInline(p.Inline), _style.BodySize);
                case ListBlock list:
                    return RenderList(list, 0);
                case QuoteBlock q:
                    return RenderQuote(q);
                case FencedCodeBlock fc:
                    return RenderCode(fc);
                case CodeBlock cb:
                    return RenderCode(cb);
                case ThematicBreakBlock _:
                    return RenderHr();
                case Table table:
                    return RenderTable(table);
                default:
                    return null; // HtmlBlock etc dropped (MD-D17)
            }
        }

        // ---- inline engine ----
        private string RenderInline(ContainerInline container)
        {
            if (container == null) return "";
            var sb = new StringBuilder();
            foreach (var inline in container) AppendInline(sb, inline);
            return sb.ToString();
        }

        private void AppendInline(StringBuilder sb, Inline inline)
        {
            switch (inline)
            {
                case LiteralInline lit:
                    sb.Append(Escape(lit.Content.ToString()));
                    break;
                case CodeInline code:
                    sb.Append("<mark=").Append(ToHex(_style.CodeBackground)).Append("><font=\"")
                      .Append(_style.CodeFont).Append("\">").Append(Escape(code.Content))
                      .Append("</font></mark>");
                    break;
                case EmphasisInline em:
                    {
                        string tag = em.DelimiterChar == '~' ? "s" : (em.DelimiterCount >= 2 ? "b" : "i");
                        sb.Append('<').Append(tag).Append('>');
                        foreach (var child in em) AppendInline(sb, child);
                        sb.Append("</").Append(tag).Append('>');
                        break;
                    }
                case LineBreakInline _:
                    sb.Append('\n');
                    break;
                case TaskList _:
                    break;  // checkbox shown as the list marker, not inline
                case LinkInline img when img.IsImage:
                    AppendInlineImage(sb, img);
                    break;
                case LinkInline link when !link.IsImage:
                    sb.Append("<link=\"").Append(link.Url).Append("\"><color=")
                      .Append(ToHex(_style.LinkColor)).Append("><u>");
                    foreach (var child in link) AppendInline(sb, child);
                    sb.Append("</u></color></link>");
                    break;
                case ContainerInline cont:   // unknown container -> recurse
                    foreach (var child in cont) AppendInline(sb, child);
                    break;
                default:
                    // Links / images / html / autolinks are handled by later tasks; unknown
                    // inline types are intentionally dropped (see design MD-D17).
                    break;
            }
        }

        // ---- shared builders ----
        private ElementNode NewVStack(float spacing)
        {
            var n = new ElementNode("VStack");
            n.Attributes["spacing"] = spacing.ToString(CultureInfo.InvariantCulture);
            n.Attributes["childAlign"] = "upper-left";
            return n;
        }

        private ElementNode NewText(string richText, float size, bool bold = false)
        {
            var n = new ElementNode("Text");
            n.Attributes["width"] = "stretch";
            n.Attributes["fontSize"] = ((int)size).ToString(CultureInfo.InvariantCulture);
            n.Attributes["font"] = _style.BodyFont;
            n.Attributes["wrap"] = _style.ParagraphWrap ? "true" : "false";
            n.Attributes["align"] = "top-left";
            n.Attributes["tr"] = "false";
            // Body text color: when set, applied as the TMP vertex color on every generated Text node.
            // Inline <color=LinkColor> spans still override it per-link. Empty -> no color= -> the node
            // inherits ProceduralBuilders.DefaultLabelColor (the library-wide default ink color).
            if (!string.IsNullOrEmpty(_style.BodyColor)) n.Attributes["color"] = _style.BodyColor;
            n.TextContent = bold ? $"<b>{richText}</b>" : richText;
            return n;
        }

        private ElementNode RenderList(ListBlock list, int depth)
        {
            var v = NewVStack(_style.BlockSpacing * 0.5f);
            int number = ParseStart(list.OrderedStart);
            foreach (var item in list)
            {
                if (item is not ListItemBlock li) continue;

                string marker;
                var task = GetTaskState(li);
                if (task.HasValue) marker = task.Value ? _style.CheckedGlyph : _style.UncheckedGlyph;
                else if (list.IsOrdered) { marker = number + "."; number++; }
                else marker = _style.BulletGlyph;

                var row = new ElementNode("HStack");
                row.Attributes["width"] = "stretch";
                row.Attributes["spacing"] = "6";
                row.Attributes["childAlign"] = "upper-left";

                if (depth > 0)
                {
                    var spacer = new ElementNode("Frame");
                    spacer.Attributes["width"] = (_style.ListIndent * depth).ToString(CultureInfo.InvariantCulture);
                    spacer.Attributes["height"] = "1";
                    row.Children.Add(spacer);
                }

                var bullet = NewText(Escape(marker), _style.BodySize);
                bullet.Attributes["width"] = "24";
                row.Children.Add(bullet);

                var content = NewVStack(_style.BlockSpacing * 0.5f);
                content.Attributes["width"] = "stretch";
                foreach (var child in li)
                {
                    if (child is ListBlock nested) content.Children.Add(RenderList(nested, depth + 1));
                    else { var n = RenderBlock(child); if (n != null) content.Children.Add(n); }
                }
                row.Children.Add(content);
                v.Children.Add(row);
            }
            return v;
        }

        private static int ParseStart(string s) => int.TryParse(s, out var n) ? n : 1;

        private static bool? GetTaskState(ListItemBlock li)
        {
            if (li.Count > 0 && li[0] is ParagraphBlock p && p.Inline != null)
                foreach (var inline in p.Inline)
                    if (inline is TaskList tl) return tl.Checked;
                    else break;   // task marker is the first inline only
            return null;
        }

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string ToHex(string colorToken)
        {
            var c = PromptUGUI.Application.UI.Theme.Resolve(colorToken);
            return "#" + ColorUtility.ToHtmlStringRGBA(c);
        }

        private ElementNode RenderQuote(QuoteBlock q)
        {
            var row = new ElementNode("HStack");
            row.Attributes["width"] = "stretch";
            row.Attributes["spacing"] = "8";
            row.Attributes["childAlign"] = "upper-left";

            var bar = new ElementNode("Image");
            bar.Attributes["width"] = _style.HrThickness.ToString(CultureInfo.InvariantCulture);
            bar.Attributes["height"] = "stretch";
            bar.Attributes["color"] = _style.QuoteBarColor;
            row.Children.Add(bar);

            var content = NewVStack(_style.BlockSpacing);
            content.Attributes["width"] = "stretch";
            foreach (var child in q) { var n = RenderBlock(child); if (n != null) content.Children.Add(n); }
            row.Children.Add(content);
            return row;
        }

        private ElementNode RenderCode(LeafBlock code)
        {
            var n = NewText("", _style.BodySize);
            n.Attributes["font"] = _style.CodeFont;
            n.Attributes["wrap"] = "false";
            n.TextContent = "<mark=" + ToHex(_style.CodeBackground) + ">" + Escape(GetCodeText(code)) + "</mark>";
            return n;
        }

        private ElementNode RenderHr()
        {
            var img = new ElementNode("Image");
            img.Attributes["width"] = "stretch";
            img.Attributes["height"] = _style.HrThickness.ToString(CultureInfo.InvariantCulture);
            img.Attributes["color"] = _style.HrColor;
            return img;
        }

        private static string GetCodeText(LeafBlock leaf)
        {
            var lines = leaf.Lines;
            if (lines.Lines == null) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < lines.Count; i++)
            {
                sb.Append(lines.Lines[i].Slice.ToString());
                if (i < lines.Count - 1) sb.Append('\n');
            }
            return sb.ToString();
        }

        private ElementNode RenderTable(Table table)
        {
            int cols = 0;
            foreach (var rowObj in table)
                if (rowObj is TableRow r) cols = Mathf.Max(cols, r.Count);

            var grid = NewVStack(2f);
            grid.Attributes["width"] = "stretch";

            foreach (var rowObj in table)
            {
                if (rowObj is not TableRow row) continue;
                var hstack = new ElementNode("HStack");
                hstack.Attributes["width"] = "stretch";
                hstack.Attributes["spacing"] = "4";
                hstack.Attributes["childAlign"] = "upper-left";

                for (int c = 0; c < cols; c++)
                {
                    string text = "";
                    if (c < row.Count && row[c] is TableCell cell)
                        text = RenderCell(cell);
                    var t = NewText(text, _style.BodySize, bold: row.IsHeader);
                    t.Attributes["width"] = "stretch";   // equal columns
                    hstack.Children.Add(t);
                }
                grid.Children.Add(hstack);
            }
            return grid;
        }

        private string RenderCell(TableCell cell)
        {
            var sb = new StringBuilder();
            foreach (var block in cell)
                if (block is ParagraphBlock p) sb.Append(RenderInline(p.Inline));
            return sb.ToString();
        }

        private static bool IsLoneImage(ParagraphBlock p, out LinkInline img)
        {
            img = null;
            if (p.Inline == null) return false;
            LinkInline found = null;
            int count = 0;
            foreach (var inline in p.Inline)
            {
                if (inline is LiteralInline lit && string.IsNullOrWhiteSpace(lit.Content.ToString())) continue;
                if (inline is LinkInline l && l.IsImage) { found = l; count++; }
                else { count = 99; break; }
            }
            img = found;
            return count == 1 && found != null;
        }

        private ElementNode RenderBlockImage(LinkInline image)
        {
            string id = "mdimg" + (_imageSeq++);
            var n = new ElementNode("RawImage");
            n.Id = id;
            n.Attributes["type"] = "contain";
            n.Attributes["width"] = "stretch";
            n.Attributes["height"] = "120";
            _images.Add(new ImageRequest(id, image.Url ?? "", GetText(image)));
            return n;
        }

        private void AppendInlineImage(StringBuilder sb, LinkInline img)
        {
            string url = img.Url ?? "";
            if (url.Length > 0 && url.IndexOf('/') < 0 && url.IndexOf(':') < 0)
                sb.Append("<sprite name=\"").Append(url).Append("\">");
            else
                sb.Append(Escape(GetText(img)));
        }

        private static string GetText(ContainerInline c)
        {
            if (c == null) return "";
            var sb = new StringBuilder();
            foreach (var inline in c)
                if (inline is LiteralInline lit) sb.Append(lit.Content.ToString());
                else if (inline is ContainerInline cc) sb.Append(GetText(cc));
            return sb.ToString();
        }
    }
}
