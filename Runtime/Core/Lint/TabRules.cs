using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint rules for &lt;Tab&gt; / &lt;TabBar&gt;.
    /// Consumed by both IRWalker (UIXmlLint CLI) and ScreenInstantiator (runtime warnings).
    /// PARENT check is parent-relative and lives inline in IRWalker / ScreenInstantiator —
    /// it cannot be expressed as a self-check.
    /// </summary>
    public static class TabRules
    {
        public const string TabParentCode = "PUI-TAB-PARENT";
        public const string TabBarChildCode = "PUI-TABBAR-CHILD";
        public const string TabChildrenCode = "PUI-TAB-CHILDREN";
        public const string DirectionCode = "PUI-TABBAR-DIRECTION";

        public static IEnumerable<LintIssue> CheckTab(ElementNode n)
        {
            if (n.Children.Count > 0)
                yield return new LintIssue(
                    TabChildrenCode, n.Tag, n.Id,
                    $"<Tab id='{n.Id}'>: Tab is a leaf control; nested children are not allowed. " +
                    "Use text / icon attributes to express content.");
        }

        public static IEnumerable<LintIssue> CheckTabBar(ElementNode n)
        {
            if (n.Attributes.TryGetValue("direction", out var dir)
                && dir != "horizontal" && dir != "vertical")
                yield return new LintIssue(
                    DirectionCode, n.Tag, n.Id,
                    $"<TabBar id='{n.Id}'>: direction='{dir}' is invalid. " +
                    "Valid: horizontal, vertical.");

            foreach (var c in n.Children)
            {
                if (c.Tag == "Tab") continue;
                // Template wrapper around a Tab is OK — TabBar.CollectStaticTabs walks
                // into the wrapper via FindTabIn so sprite push / auto-select / events
                // still work. Suppress the CHILD warning to match runtime behaviour.
                if (ContainsTabDescendant(c)) continue;
                yield return new LintIssue(
                    TabBarChildCode, n.Tag, n.Id,
                    $"<TabBar id='{n.Id}'>: expected <Tab> children; found <{c.Tag}>. " +
                    "Layout will still render via LayoutGroup but tab semantics will not apply to non-Tab nodes.");
            }
        }

        private static bool ContainsTabDescendant(ElementNode node)
        {
            foreach (var c in node.Children)
            {
                if (c.Tag == "Tab") return true;
                if (ContainsTabDescendant(c)) return true;
            }
            return false;
        }
    }
}
