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
        public const string DirectionCode = "PUI-TABBAR-DIRECTION";

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
                // A direct <Tab>, a literal wrapper containing a <Tab> (TabBar.CollectStaticTabs
                // walks into it via FindTabIn so sprite push / auto-select / events still work),
                // OR a Template invocation whose body the CLI can't see — all may carry tab
                // semantics, so don't warn. Only a subtree built entirely from non-Tab builtins
                // is a genuine misuse. This matches runtime, which checks post-expansion.
                if (SubtreeMayResolveToTab(c)) continue;
                yield return new LintIssue(
                    TabBarChildCode, n.Tag, n.Id,
                    $"<TabBar id='{n.Id}'>: expected <Tab> children; found <{c.Tag}>. " +
                    "Layout will still render via LayoutGroup but tab semantics will not apply to non-Tab nodes.");
            }
        }

        /// <summary>
        /// True if this subtree may yield a <c>&lt;Tab&gt;</c> at runtime. A literal
        /// <c>&lt;Tab&gt;</c> obviously does. A non-builtin tag is a Template invocation whose
        /// body the CLI can't expand (<c>&lt;Import&gt;</c> isn't resolved at lint time), so it
        /// MAY expand to a <c>&lt;Tab&gt;</c> (e.g. an <c>itemTemplate</c> tab) — we suppress
        /// rather than false-positive. At runtime this runs POST-expansion, where every tag is a
        /// registered builtin, so the non-builtin branch never fires and behaviour is unchanged.
        /// </summary>
        private static bool SubtreeMayResolveToTab(ElementNode node)
        {
            if (node.Tag == "Tab") return true;
            if (!BuiltinTags.IsBuiltin(node.Tag)) return true; // Template invocation — unknown expansion
            foreach (var c in node.Children)
                if (SubtreeMayResolveToTab(c)) return true;
            return false;
        }
    }
}
