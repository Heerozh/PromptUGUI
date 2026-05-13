using System.Collections.Generic;
using System.Linq;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Template;

namespace PromptUGUI.Editor.I18n
{
    /// <summary>
    /// Walks a parsed UIDocument and yields one ExtractedString per translatable string found
    /// in Text/Btn element content or "text" attributes.
    /// </summary>
    internal static class XmlStringScanner
    {
        // Tags whose textContent and "text" attr are translatable.
        private static readonly HashSet<string> TextHostingTags = new() { "Text", "Btn" };

        public static IEnumerable<ExtractedString> Scan(string xmlSource, string localePartition)
            => Scan(xmlSource, localePartition, externalTemplates: null);

        /// <summary>
        /// Scan with an optional pool of templates defined in OTHER files (cross-file
        /// invocations). When provided, template invocations are inlined before
        /// extraction, so parameter values flowing into Text/Btn slots show up as
        /// msgids. The file's own templates take precedence on name collisions.
        /// </summary>
        public static IEnumerable<ExtractedString> Scan(
            string xmlSource, string localePartition,
            IReadOnlyDictionary<string, TemplateDef> externalTemplates)
        {
            UIDocument doc;
            try { doc = UIDocumentParser.Parse(xmlSource); }
            catch (ParseException) { yield break; }   // unparseable file → skip

            UIDocument expanded = TryExpand(doc, externalTemplates);

            foreach (var screen in expanded.Screens)
            {
                foreach (var es in WalkNode(screen.Root, screen.Name, parentSiblings: null, localePartition))
                    yield return es;
            }
            // Walk original (unexpanded) Template bodies so that static template text and
            // format-string text are extracted exactly once regardless of invocation count.
            foreach (var t in doc.Templates.Values)
            {
                foreach (var es in WalkNode(t.Body, $"Template:{t.Name}", parentSiblings: null, localePartition))
                    yield return es;
            }
        }

        private static UIDocument TryExpand(
            UIDocument doc,
            IReadOnlyDictionary<string, TemplateDef> externalTemplates)
        {
            try
            {
                var loaded = new DocumentLoader.LoadedDoc { EntrySrc = "<scan>" };
                foreach (var s in doc.Screens) loaded.Screens.Add(s);
                if (externalTemplates != null)
                {
                    foreach (var kv in externalTemplates)
                        loaded.Templates[new DocumentLoader.TemplateKey(null, kv.Key)] = kv.Value;
                }
                // File-local templates win over external when names collide — same precedence
                // the runtime loader applies for entry-file templates over commons.
                foreach (var kv in doc.Templates)
                    loaded.Templates[new DocumentLoader.TemplateKey(null, kv.Key)] = kv.Value;
                return TemplateExpander.Expand(loaded);
            }
            catch (TemplateException)
            {
                // Cross-file template invocation that we don't have the def for, or a
                // genuine template bug — fall back to walking the raw doc so the rest
                // of the file's strings still get extracted.
                return doc;
            }
        }

        private static IEnumerable<ExtractedString> WalkNode(
            ElementNode node, string screenOrTemplateName,
            List<string> parentSiblings, string localePartition)
        {

            // Pre-compute siblings text list for ambient context of THIS node's children.
            // We build it from the children of the current node whose text will be harvested,
            // so when we recurse into each child we can pass the sibling list.
            var childSiblings = new List<string>();
            foreach (var c in node.Children)
            {
                if (!TextHostingTags.Contains(c.Tag) || IsTrFalse(c)) continue;
                var sibText = PickMsgid(c);
                if (!string.IsNullOrEmpty(sibText))
                    childSiblings.Add(sibText);
            }

            // Harvest this node if it hosts translatable text.
            if (TextHostingTags.Contains(node.Tag) && !IsTrFalse(node))
            {
                node.Attributes.TryGetValue("ctx", out var ctx);

                var bodyMsgid = PickMsgid(node);
                if (!string.IsNullOrEmpty(bodyMsgid))
                    yield return Build(bodyMsgid, ctx, screenOrTemplateName, node, parentSiblings, "text", localePartition);

                var attrMsgid = PickTextAttrMsgid(node);
                if (!string.IsNullOrEmpty(attrMsgid))
                    yield return Build(attrMsgid, ctx, screenOrTemplateName, node, parentSiblings, "text-attr", localePartition);
            }

            // Recurse into children, passing childSiblings as the sibling context.
            foreach (var child in node.Children)
            {
                foreach (var es in WalkNode(child, screenOrTemplateName, childSiblings, localePartition))
                    yield return es;
            }
        }

        /// <summary>
        /// Decide what msgid (if any) to emit for this node's element-content text.
        /// Rules:
        /// - No text → nothing.
        /// - Raw is pure-braces (e.g. {{label}}):
        ///   - If the node came from template expansion (TextArgs set) → use the
        ///     substituted TextContent (the invocation-site value).
        ///   - Else (standalone placeholder in a Screen, or Template body itself) →
        ///     skip; there's no real string to translate.
        /// - Raw is static or format-string:
        ///   - If the node came from template expansion → skip; the same msgid is
        ///     extracted exactly once via the Template body walk.
        ///   - Else → emit the raw form.
        /// </summary>
        private static string PickMsgid(ElementNode node)
        {
            var raw = GetRawText(node);
            if (string.IsNullOrEmpty(raw)) return null;
            var fromTemplate = node.TextArgs != null && node.TextArgs.Count > 0;
            if (IsPureBraces(raw))
                return fromTemplate ? node.TextContent : null;
            return fromTemplate ? null : raw;
        }

        /// <summary>
        /// Same rules as <see cref="PickMsgid"/> but for the "text" attribute.
        /// AttributesRaw holds the pre-substitution form when present.
        /// </summary>
        private static string PickTextAttrMsgid(ElementNode node)
        {
            string raw = null;
            if (node.AttributesRaw != null && node.AttributesRaw.TryGetValue("text", out var ra))
                raw = ra;
            else if (node.Attributes.TryGetValue("text", out var v))
                raw = v;
            if (string.IsNullOrEmpty(raw)) return null;
            var fromTemplate = node.TextArgs != null && node.TextArgs.Count > 0;
            if (IsPureBraces(raw))
            {
                if (!fromTemplate) return null;
                return node.Attributes.TryGetValue("text", out var substituted) ? substituted : null;
            }
            return fromTemplate ? null : raw;
        }

        private static string GetRawText(ElementNode node) =>
            string.IsNullOrEmpty(node.TextContentRaw) ? node.TextContent : node.TextContentRaw;

        private static bool IsTrFalse(ElementNode node) =>
            node.Attributes.TryGetValue("tr", out var v) && v == "false";

        private static bool IsPureBraces(string s)
        {
            var t = s.Trim();
            // Exactly one {{...}} placeholder and nothing else.
            return t.StartsWith("{{") && t.EndsWith("}}") &&
                   t.Count(c => c == '{') == 2 && t.Count(c => c == '}') == 2;
        }

        private static ExtractedString Build(
            string msgid, string ctx, string screenOrTemplateName,
            ElementNode node, List<string> parentSiblings, string attrSlot,
            string localePartition)
        {

            var es = new ExtractedString
            {
                Msgid = msgid,
                Msgctxt = ctx,
                LocalePartition = localePartition,
            };

            var who = string.IsNullOrEmpty(node.Id) ? node.Tag : $"{node.Tag}#{node.Id}";
            es.ExtractedComments.Add($"{screenOrTemplateName} screen, {who} {attrSlot}");

            // Ambient sibling context: list nearby sibling strings so translators know which
            // button/label is which when strings are short or duplicated.
            if (parentSiblings != null && parentSiblings.Count > 0)
            {
                var sibs = string.Join(", ", parentSiblings.Where(s => s != msgid).Take(3));
                if (!string.IsNullOrEmpty(sibs))
                    es.ExtractedComments.Add($"sibling: {sibs}");
            }

            if (TmpRichTextDetector.HasTmpTags(msgid))
            {
                es.ExtractedComments.Add(
                    "Contains TMP rich text tags. Preserve tags and attribute values verbatim.");
            }

            return es;
        }
    }
}
