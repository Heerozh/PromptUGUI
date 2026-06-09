using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Markdig;
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
                    return NewText(RenderInline(h.Inline),
                        _style.HeadingSizes[Mathf.Clamp(h.Level, 1, 6) - 1], bold: true);
                case ParagraphBlock p:
                    return NewText(RenderInline(p.Inline), _style.BodySize);
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
            n.TextContent = bold ? $"<b>{richText}</b>" : richText;
            return n;
        }

        private static string Escape(string s) =>
            s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string ToHex(string colorToken)
        {
            var c = PromptUGUI.Application.UI.Theme.Resolve(colorToken);
            return "#" + ColorUtility.ToHtmlStringRGBA(c);
        }
    }
}
