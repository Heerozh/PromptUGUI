using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Structural checks for <c>&lt;TabMenu&gt;</c> — the popup-shaped tab group. Its rows are
    /// <c>&lt;Tab&gt;</c>s, exactly as in a <c>&lt;TabBar&gt;</c>, so <see cref="TabRules"/> covers
    /// what they have in common; what differs is that the rows span the panel and cannot be sized
    /// across.
    /// </summary>
    public static class TabMenuRules
    {
        public const string ChildCode = "PUI-TABMENU-CHILD";
        public const string ItemWidthCode = "PUI-TABMENU-ITEM-WIDTH";

        public const string Tag = "TabMenu";

        public static IEnumerable<LintIssue> CheckTabMenu(ElementNode n)
        {
            foreach (var c in n.Children)
            {
                // <Decor> is a legitimate non-row child: it decorates the panel itself and is
                // excluded from the layout, so it never becomes a menu row.
                if (c.Tag == "Decor") continue;

                if (!TabRules.SubtreeMayResolveToTab(c))
                {
                    yield return new LintIssue(
                        ChildCode, n.Tag, n.Id,
                        $"<TabMenu id='{n.Id}'>: expected <Tab> rows; found <{c.Tag}>. " +
                        "It will still be laid out in the popup, but no tab semantics apply to it.");
                    continue;
                }

                foreach (var issue in CheckRowWidth(n, c))
                    yield return issue;
            }
        }

        /// <summary>
        /// A menu row always spans the panel (the popup's layout group force-expands its children
        /// across), so a <c>width</c> anywhere on the row is silently overridden.
        /// </summary>
        private static IEnumerable<LintIssue> CheckRowWidth(ElementNode menu, ElementNode row)
        {
            var carrier = FindWidthCarrier(row);
            if (carrier == null) yield break;

            yield return new LintIssue(
                ItemWidthCode, carrier.Tag, carrier.Id,
                $"<{carrier.Tag} id='{carrier.Id}'> inside <TabMenu id='{menu.Id}'>: 'width' is ignored — " +
                "menu rows always span the panel. Fix: set 'popupWidth' on the <TabMenu> to size the " +
                "menu, and remove 'width' from the row.");
        }

        // The row root or, for a Template-style wrapper, the <Tab> inside it — whichever declares a
        // width (including a variant-only one, which is just as ignored).
        private static ElementNode FindWidthCarrier(ElementNode node)
        {
            if (DeclaresWidth(node)) return node;
            foreach (var c in node.Children)
            {
                var found = FindWidthCarrier(c);
                if (found != null) return found;
            }
            return null;
        }

        private static bool DeclaresWidth(ElementNode n)
            => n.Attributes.ContainsKey("width") || n.VariantOverrides.ContainsKey("width");
    }
}
