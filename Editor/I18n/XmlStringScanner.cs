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
        // Tags whose textContent and "text" attr are translatable: built-ins that render
        // their element content / "text" attr as a static, user-facing label (defaultTextAttr
        // "text"). Deliberately NOT every control with a default-text attr — InputField's
        // "text" is the editable value (its i18n target is the "placeholder" attr) and
        // Markdown's "text" is dynamic source content, so neither is harvested here.
        private static readonly HashSet<string> TextHostingTags = new() { "Text", "Btn", "Toggle", "Tab" };

        /// <summary>A single self-contained document: no Import closure, no common libraries.</summary>
        public static IEnumerable<ExtractedString> Scan(string xmlSource, string localePartition)
        {
            UIDocument doc;
            try { doc = UIDocumentParser.Parse(xmlSource); }
            catch (ParseException) { return Enumerable.Empty<ExtractedString>(); }   // unparseable file → skip
            catch (System.Xml.XmlException) { return Enumerable.Empty<ExtractedString>(); }
            return Scan(doc, localePartition, "<scan>", imports: null, commons: null);
        }

        /// <summary>
        /// Screens are walked EXPANDED — template invocations inlined — so a parameter value
        /// flowing into a Text/Btn slot shows up as a msgid. Expansion goes through
        /// <see cref="DocumentAssembler.AssembleWithCommons"/>, the same Import closure +
        /// <c>&lt;Style&gt;</c> + common-library merge the runtime and the linter use: a
        /// hand-built subset of it fails on the first <c>class=</c> it cannot resolve, and every
        /// invocation-site value in that document goes with it.
        /// </summary>
        /// <param name="src">The entry's own resolver key, i.e. what <paramref name="imports"/> is asked for it.</param>
        /// <param name="imports">
        /// Resolves every <c>&lt;Import src&gt;</c> (and every row of <paramref name="commons"/>) to its
        /// parsed document. Null when the caller could not produce the closure: a document with
        /// imports or commons is then not expanded at all — against half its names it would only
        /// fail on phantom unknowns — and the raw tree is walked instead.
        /// </param>
        /// <param name="commons">The project's common libraries (<c>PromptUGUISettings.commonLibraries</c>).</param>
        /// <param name="onExpansionFailed">
        /// Called with the reason when expansion throws — a document <c>UI.Open</c> would reject
        /// too. The raw tree is still walked, so text written directly in the Screen survives.
        /// </param>
        internal static IEnumerable<ExtractedString> Scan(
            UIDocument doc, string localePartition, string src,
            System.Func<string, UIDocument> imports, IReadOnlyList<ImportRef> commons,
            System.Action<string> onExpansionFailed = null)
        {
            ICollection<string> templateTags = null;
            var expanded = doc.Screens.Count == 0
                ? doc
                : TryExpand(doc, src, imports, commons, onExpansionFailed, out templateTags);
            // Only a successful expansion stamps call sites; the raw fallback has none to name.
            var callSiteNodes = templateTags == null ? null : IndexCallSites(doc, templateTags);

            foreach (var screen in expanded.Screens)
            {
                foreach (var es in WalkNode(screen.Root, screen.Name, parentSiblings: null, localePartition,
                                            enclosing: null, callSiteNodes))
                    yield return es;
            }
            // Walk original (unexpanded) Template bodies so that static template text and
            // format-string text are extracted exactly once regardless of invocation count.
            foreach (var t in doc.Templates.Values)
            {
                foreach (var es in WalkNode(t.Body, $"Template:{t.Name}", parentSiblings: null, localePartition,
                                            enclosing: null, callSiteNodes: null))
                    yield return es;
            }
        }

        /// <summary>
        /// The template invocations written in the entry's own screens, keyed by the <c>file:line</c>
        /// expansion records as <see cref="ElementNode.InvocationSite"/> — so a value that came from
        /// template arguments can show the call that supplied it, as the author wrote it. Invocations
        /// inside template bodies are left out on purpose: the literal an argument carries was written
        /// at a screen-level call (a nested one only passes <c>{{param}}</c> through).
        /// </summary>
        private static Dictionary<string, ElementNode> IndexCallSites(UIDocument doc, ICollection<string> templateTags)
        {
            var index = new Dictionary<string, ElementNode>();

            void Visit(ElementNode n)
            {
                var tag = n.Namespace == null ? n.Tag : $"{n.Namespace}.{n.Tag}";
                if (n.OriginSrc != null && n.Line > 0 && templateTags.Contains(tag))
                {
                    var site = $"{n.OriginSrc}:{n.Line}";
                    if (!index.ContainsKey(site)) index[site] = n;
                }
                foreach (var c in n.Children) Visit(c);
            }

            foreach (var screen in doc.Screens)
            {
                Visit(screen.Root);
                foreach (var block in screen.Variants)
                    foreach (var add in block.Adds)
                        foreach (var c in add.Children)
                            Visit(c);
            }
            return index;
        }

        /// <summary>The start tag as written — attributes in source order, <c>id</c> first — on one line.</summary>
        private static string StartTag(ElementNode node)
        {
            var sb = new System.Text.StringBuilder("<");
            if (node.Namespace != null) sb.Append(node.Namespace).Append('.');
            sb.Append(node.Tag);
            if (!string.IsNullOrEmpty(node.Id)) AppendAttribute(sb, "id", node.Id);
            foreach (var kv in node.Attributes) AppendAttribute(sb, kv.Key, kv.Value);
            foreach (var kv in node.VariantOverrides)
                foreach (var (variant, value) in kv.Value)
                    AppendAttribute(sb, $"{kv.Key}.{variant}", value);
            return sb.Append(" />").ToString();
        }

        private static void AppendAttribute(System.Text.StringBuilder sb, string name, string value) =>
            sb.Append(' ').Append(name).Append("=\"")
              .Append((value ?? "").Replace("\"", "&quot;").Replace('\r', ' ').Replace('\n', ' '))
              .Append('"');

        /// <summary>
        /// The template call sites enclosing a node of the expanded tree, innermost first — each link
        /// one instance root's <see cref="ElementNode.InvocationComments"/> and
        /// <see cref="ElementNode.InvocationSite"/>. Immutable and threaded down the lazy
        /// <see cref="WalkNode"/> recursion, so nothing ever has to be popped.
        /// </summary>
        private sealed class CallSiteScope
        {
            public readonly IReadOnlyList<string> Comments;
            public readonly string Site;
            public readonly CallSiteScope Outer;

            public CallSiteScope(IReadOnlyList<string> comments, string site, CallSiteScope outer)
            {
                Comments = comments;
                Site = site;
                Outer = outer;
            }
        }

        /// <param name="templateTags">
        /// On success, every template the document can invoke, spelled as a tag (<c>Name</c> /
        /// <c>ns.Name</c>); null when nothing was expanded.
        /// </param>
        private static UIDocument TryExpand(
            UIDocument doc, string src,
            System.Func<string, UIDocument> imports, IReadOnlyList<ImportRef> commons,
            System.Action<string> onExpansionFailed, out ICollection<string> templateTags)
        {
            templateTags = null;
            var hasCommons = commons != null && commons.Count > 0;
            if (imports == null && (doc.Imports.Count > 0 || hasCommons)) return doc;

            try
            {
                System.Func<string, UIDocument> lookup = s => s == src ? doc : imports?.Invoke(s);
                var loaded = DocumentAssembler.AssembleWithCommons(src, lookup, commons);
                var expanded = TemplateExpander.Expand(loaded);
                templateTags = new HashSet<string>(loaded.Templates.Keys.Select(k => k.ToString()));
                return expanded;
            }
            catch (System.Exception ex) when (ex is TemplateException || ex is ParseException)
            {
                onExpansionFailed?.Invoke(ex.Message);
                return doc;
            }
        }

        private static IEnumerable<ExtractedString> WalkNode(
            ElementNode node, string screenOrTemplateName,
            List<string> parentSiblings, string localePartition, CallSiteScope enclosing,
            IReadOnlyDictionary<string, ElementNode> callSiteNodes)
        {
            // Entering an instance root adds its call site(s) BEFORE its own text is harvested: a template
            // body may be nothing but <Btn>{{label}}</Btn>, and that label is the call site's argument.
            var scope = node.InvocationComments != null || node.InvocationSite != null
                ? new CallSiteScope(node.InvocationComments, node.InvocationSite, enclosing)
                : enclosing;

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

                // Call-site comments go only to a value that came from template arguments: a template's
                // static text is one msgid for every instance, and slot content has its own comments.
                var bodyMsgid = PickMsgid(node, out var bodyFromArgs);
                if (!string.IsNullOrEmpty(bodyMsgid))
                    yield return Build(bodyMsgid, ctx, screenOrTemplateName, node, parentSiblings, "text",
                                       localePartition, bodyFromArgs ? scope : null, callSiteNodes);

                var attrMsgid = PickTextAttrMsgid(node, out var attrFromArgs);
                if (!string.IsNullOrEmpty(attrMsgid))
                    yield return Build(attrMsgid, ctx, screenOrTemplateName, node, parentSiblings, "text-attr",
                                       localePartition, attrFromArgs ? scope : null, callSiteNodes);
            }

            // Recurse into children, passing childSiblings as the sibling context.
            foreach (var child in node.Children)
            {
                foreach (var es in WalkNode(child, screenOrTemplateName, childSiblings, localePartition, scope,
                                            callSiteNodes))
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
        /// <paramref name="fromArgs"/> is true exactly for the first case: the value is a template
        /// argument, so the call sites enclosing it describe it.
        /// </summary>
        private static string PickMsgid(ElementNode node, out bool fromArgs)
        {
            fromArgs = false;
            var raw = GetRawText(node);
            if (string.IsNullOrEmpty(raw)) return null;
            var fromTemplate = node.TextArgs != null && node.TextArgs.Count > 0;
            if (IsPureBraces(raw))
            {
                fromArgs = fromTemplate;
                return fromTemplate ? node.TextContent : null;
            }
            return fromTemplate ? null : raw;
        }

        private static string PickMsgid(ElementNode node) => PickMsgid(node, out _);

        /// <summary>
        /// Same rules as <see cref="PickMsgid(ElementNode, out bool)"/> but for the "text" attribute.
        /// AttributesRaw holds the pre-substitution form when present.
        /// </summary>
        private static string PickTextAttrMsgid(ElementNode node, out bool fromArgs)
        {
            fromArgs = false;
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
                fromArgs = true;
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

        /// <param name="callSites">
        /// The enclosing call sites when the value is a template argument, else null (spec
        /// 2026-09-30-i18n-xml-comments §3.3).
        /// </param>
        /// <param name="callSiteNodes">The entry's screen-level invocations (<see cref="IndexCallSites"/>), or null.</param>
        private static ExtractedString Build(
            string msgid, string ctx, string screenOrTemplateName,
            ElementNode node, List<string> parentSiblings, string attrSlot,
            string localePartition, CallSiteScope callSites,
            IReadOnlyDictionary<string, ElementNode> callSiteNodes)
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

            // Which argument this is: the call that supplied it, as written — the innermost enclosing call
            // written in the entry's screens. A call-site comment like "label is …, caption is …" means
            // nothing to a translator who cannot see that this value was the caption.
            if (callSiteNodes != null)
            {
                for (var link = callSites; link != null; link = link.Outer)
                {
                    if (link.Site == null || !callSiteNodes.TryGetValue(link.Site, out var written)) continue;
                    es.ExtractedComments.Add("call site: " + StartTag(written));
                    break;
                }
            }

            // Author comments: the ones above the element where the text is written (for an argument,
            // that is inside the template), then the enclosing call sites, innermost first.
            if (node.LeadingComments != null)
                es.ExtractedComments.AddRange(node.LeadingComments);
            for (var link = callSites; link != null; link = link.Outer)
            {
                if (link.Comments != null)
                    es.ExtractedComments.AddRange(link.Comments);
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
