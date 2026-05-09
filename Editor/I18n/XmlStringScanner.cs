using System.Collections.Generic;
using System.Linq;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Editor.I18n {
    /// <summary>
    /// Walks a parsed UIDocument and yields one ExtractedString per translatable string found
    /// in Text/Btn element content or "text" attributes.
    /// </summary>
    internal static class XmlStringScanner {
        // Tags whose textContent and "text" attr are translatable.
        static readonly HashSet<string> TextHostingTags = new() { "Text", "Btn" };

        public static IEnumerable<ExtractedString> Scan(string xmlSource, string localePartition) {
            UIDocument doc;
            try { doc = UIDocumentParser.Parse(xmlSource); }
            catch (ParseException) { yield break; }   // unparseable file → skip

            foreach (var screen in doc.Screens) {
                foreach (var es in WalkNode(screen.Root, screen.Name, parentSiblings: null, localePartition))
                    yield return es;
            }
            foreach (var t in doc.Templates.Values) {
                foreach (var es in WalkNode(t.Body, $"Template:{t.Name}", parentSiblings: null, localePartition))
                    yield return es;
            }
        }

        static IEnumerable<ExtractedString> WalkNode(
            ElementNode node, string screenOrTemplateName,
            List<string> parentSiblings, string localePartition) {

            // Pre-compute siblings text list for ambient context of THIS node's children.
            // We build it from the children of the current node whose text will be harvested,
            // so when we recurse into each child we can pass the sibling list.
            var childSiblings = new List<string>();
            foreach (var c in node.Children) {
                if (!TextHostingTags.Contains(c.Tag) || IsTrFalse(c)) continue;
                var raw = GetRawText(c);
                if (!string.IsNullOrEmpty(raw) && !IsPureBraces(raw))
                    childSiblings.Add(raw.Trim());
            }

            // Harvest this node if it hosts translatable text.
            if (TextHostingTags.Contains(node.Tag) && !IsTrFalse(node)) {
                node.Attributes.TryGetValue("ctx", out var ctx);

                // textContent (prefer raw over post-substitution)
                var rawText = GetRawText(node);
                if (!string.IsNullOrEmpty(rawText) && !IsPureBraces(rawText)) {
                    yield return Build(rawText, ctx, screenOrTemplateName, node, parentSiblings, "text", localePartition);
                }

                // "text" attribute
                string textAttrRaw = null;
                if (node.AttributesRaw.TryGetValue("text", out var ra))
                    textAttrRaw = ra;
                else if (node.Attributes.TryGetValue("text", out var v))
                    textAttrRaw = v;
                if (!string.IsNullOrEmpty(textAttrRaw) && !IsPureBraces(textAttrRaw)) {
                    yield return Build(textAttrRaw, ctx, screenOrTemplateName, node, parentSiblings, "text-attr", localePartition);
                }
            }

            // Recurse into children, passing childSiblings as the sibling context.
            foreach (var child in node.Children) {
                foreach (var es in WalkNode(child, screenOrTemplateName, childSiblings, localePartition))
                    yield return es;
            }
        }

        static string GetRawText(ElementNode node) =>
            string.IsNullOrEmpty(node.TextContentRaw) ? node.TextContent : node.TextContentRaw;

        static bool IsTrFalse(ElementNode node) =>
            node.Attributes.TryGetValue("tr", out var v) && v == "false";

        static bool IsPureBraces(string s) {
            var t = s.Trim();
            // Exactly one {{...}} placeholder and nothing else.
            return t.StartsWith("{{") && t.EndsWith("}}") &&
                   t.Count(c => c == '{') == 2 && t.Count(c => c == '}') == 2;
        }

        static ExtractedString Build(
            string msgid, string ctx, string screenOrTemplateName,
            ElementNode node, List<string> parentSiblings, string attrSlot,
            string localePartition) {

            var es = new ExtractedString {
                Msgid = msgid,
                Msgctxt = ctx,
                LocalePartition = localePartition,
            };

            var who = string.IsNullOrEmpty(node.Id) ? node.Tag : $"{node.Tag}#{node.Id}";
            es.ExtractedComments.Add($"{screenOrTemplateName} screen, {who} {attrSlot}");

            // Ambient sibling context: list nearby sibling strings so translators know which
            // button/label is which when strings are short or duplicated.
            if (parentSiblings != null && parentSiblings.Count > 0) {
                var sibs = string.Join(", ", parentSiblings.Where(s => s != msgid).Take(3));
                if (!string.IsNullOrEmpty(sibs))
                    es.ExtractedComments.Add($"sibling: {sibs}");
            }

            if (TmpRichTextDetector.HasTmpTags(msgid)) {
                es.ExtractedComments.Add(
                    "Contains TMP rich text tags. Preserve tags and attribute values verbatim.");
            }

            return es;
        }
    }
}
