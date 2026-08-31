using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>&lt;Collapsible&gt;</c>'s structural rules (spec 2026-08-31-collapsible-design §4.8). Pure
    /// C# — string-level, like every rule the CLI shares with <c>ScreenInstantiator</c>.
    /// </summary>
    public static class CollapsibleRules
    {
        public const string HeightCode = "PUI-COLLAPSIBLE-HEIGHT";
        public const string HeaderFirstCode = "PUI-COLLAPSIBLE-HEADER-FIRST";
        public const string HeaderMultiCode = "PUI-COLLAPSIBLE-HEADER-MULTI";
        public const string HeaderConflictCode = "PUI-COLLAPSIBLE-HEADER-CONFLICT";
        public const string HeaderOutsideCode = "PUI-HEADER-OUTSIDE";
        public const string GroupMultiExpandedCode = "PUI-COLLAPSIBLE-GROUP-MULTI-EXPANDED";

        public const string Tag = "Collapsible";
        public const string HeaderTag = "Header";

        /// <summary>Attributes that fill the built-in caption, and therefore clash with a slot.</summary>
        private static readonly string[] CaptionAttrs =
        {
            "text", "icon", "iconColor", "font", "fontSize", "textColor",
        };

        private static readonly string[] SizeAttrs = { "height", "size" };

        public static IEnumerable<LintIssue> CheckCollapsible(ElementNode n) =>
            CheckCollapsible(n, StyleAttributeView.Empty);

        public static IEnumerable<LintIssue> CheckCollapsible(ElementNode n, StyleAttributeView styles)
        {
            if (n.Tag != Tag) yield break;
            styles ??= StyleAttributeView.Empty;

            // A panel is exactly its header plus its body, so a height is not the author's to give.
            foreach (var attr in SizeAttrs)
            {
                styles.Resolve(n, attr, out var baseValue, out var variants);
                var written = baseValue != null;
                if (!written)
                    foreach (var (_, value) in variants)
                        if (value != null) { written = true; break; }
                if (!written) continue;

                yield return new LintIssue(
                    HeightCode, n.Tag, n.Id,
                    $"<Collapsible id='{n.Id}'>: {attr}= is not allowed — a collapsible is exactly as "
                    + "tall as its header plus its body, and folding it is what changes that. Cap the "
                    + "body with maxHeight= (it scrolls past the cap), size the bar with "
                    + "headerHeight=, and give width= whatever you like.");
                break;   // one message per node is enough; height and size say the same thing
            }

            var headers = 0;
            for (var i = 0; i < n.Children.Count; i++)
            {
                if (n.Children[i].Tag != HeaderTag) continue;
                headers++;
                if (headers == 1 && i != 0)
                    yield return new LintIssue(
                        HeaderFirstCode, n.Tag, n.Id,
                        $"<Collapsible id='{n.Id}'>: <Header> must be the FIRST child — everything "
                        + "after it is the body, in order.");
            }

            if (headers > 1)
                yield return new LintIssue(
                    HeaderMultiCode, n.Tag, n.Id,
                    $"<Collapsible id='{n.Id}'>: {headers} <Header> elements; there is one header bar. "
                    + "Put the extra content inside the first one.");

            if (headers == 0) yield break;

            foreach (var attr in CaptionAttrs)
            {
                styles.Resolve(n, attr, out var baseValue, out _);
                if (baseValue == null) continue;
                yield return new LintIssue(
                    HeaderConflictCode, n.Tag, n.Id,
                    $"<Collapsible id='{n.Id}'>: {attr}= fills the built-in caption, but a <Header> "
                    + "replaces it — the attribute would never show. Drop one of the two "
                    + "(arrow= / arrowColor= / arrowSize= still apply: the caret is drawn either way).");
            }
        }

        /// <summary>A <c>&lt;Header&gt;</c> anywhere but directly inside a <c>&lt;Collapsible&gt;</c>.</summary>
        public static IEnumerable<LintIssue> CheckHeaderOutside(ElementNode parent, ElementNode child)
        {
            if (child.Tag != HeaderTag || parent.Tag == Tag) yield break;
            yield return new LintIssue(
                HeaderOutsideCode, child.Tag, child.Id,
                $"<Header> is only meaningful as the first child of a <Collapsible>; found under "
                + $"<{parent.Tag}>, where it is not a control and will not be instantiated.");
        }

        /// <summary>
        /// Several members of one accordion authored open. Only the first stays open at runtime
        /// (document order), so the others' <c>expanded="true"</c> is a lie about what will happen.
        /// </summary>
        public static IEnumerable<LintIssue> CheckGroups(ElementNode root)
        {
            var openPerGroup = new Dictionary<string, int>();
            var firstNode = new Dictionary<string, ElementNode>();
            Collect(root, openPerGroup, firstNode);

            foreach (var pair in openPerGroup)
            {
                if (pair.Value < 2) continue;
                var node = firstNode[pair.Key];
                yield return new LintIssue(
                    GroupMultiExpandedCode, Tag, node.Id,
                    $"group='{pair.Key}': {pair.Value} panels are authored open, but an accordion "
                    + "shows one at a time — the first in document order wins and the rest open "
                    + "closed. Write expanded='false' on those.");
            }
        }

        private static void Collect(ElementNode n, Dictionary<string, int> open,
                                    Dictionary<string, ElementNode> first)
        {
            if (n.Tag == Tag
                && n.Attributes.TryGetValue("group", out var group)
                && !string.IsNullOrEmpty(group))
            {
                // expanded defaults to true, so "not written" counts as open.
                var isOpen = !n.Attributes.TryGetValue("expanded", out var expanded)
                             || expanded != "false";
                if (isOpen)
                {
                    open.TryGetValue(group, out var count);
                    open[group] = count + 1;
                    if (!first.ContainsKey(group)) first[group] = n;
                }
            }

            foreach (var c in n.Children) Collect(c, open, first);
        }
    }
}
