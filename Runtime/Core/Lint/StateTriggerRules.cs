using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint rule for sourceless <c>state-*</c> triggers.
    ///
    /// <para>
    /// A bare (no-<c>@id</c>) <c>on="state-normal|hover|pressed|disabled"</c> on a
    /// <c>&lt;Trigger&gt;</c> / <c>&lt;Animation&gt;</c> / <c>&lt;Show&gt;</c> resolves
    /// <b>upward</b> to the nearest <c>&lt;Btn&gt;</c> ancestor at runtime
    /// (<c>TriggerSourceResolver.FindStateSource</c>). With no such ancestor the runtime
    /// hard-throws; this rule surfaces it statically in the UIXmlLint CLI.
    /// </para>
    ///
    /// <para>
    /// CLI-only: the runtime path already throws via <c>FindStateSource</c>, so it has no
    /// ScreenInstantiator mirror. The ancestor / Template-body exemptions are applied by the
    /// caller (<see cref="IRWalker"/>), mirroring how <c>PUI-TAB-PARENT</c> keeps its
    /// structural context in the walker. <c>@id</c> forms can't be resolved statically and
    /// are deferred to runtime ScopedIds resolution.
    /// </para>
    /// </summary>
    public static class StateTriggerRules
    {
        public const string NoSourceCode = "PUI-STATE-NO-SOURCE";
        public const string NoMenuCode = "PUI-EXPAND-NO-SOURCE";
        public const string NoToggleSourceCode = "PUI-CHECKED-NO-SOURCE";
        public const string NoListRowCode = "PUI-LIFT-NO-SOURCE";
        public const string ShowExpandSelfCode = "PUI-SHOW-EXPAND-SELF";

        private static readonly HashSet<string> BareListRowValues =
            new HashSet<string> { "lift", "drop" };

        /// <summary>True if <paramref name="tag"/> hosts reorderable rows (a <c>lift</c> / <c>drop</c> source).</summary>
        public static bool IsListRowHostTag(string tag) => tag == "ScrollList";

        /// <summary>
        /// Yields <see cref="NoListRowCode"/> when <paramref name="n"/> is a bare (no-<c>@id</c>)
        /// <c>lift</c> / <c>drop</c> trigger with no <c>&lt;ScrollList&gt;</c> ancestor — the row it
        /// would resolve upward to does not exist, and the runtime hard-throws
        /// (<c>TriggerSourceResolver.FindReorderRow</c>). Same exemptions as the other upward
        /// rules: Template bodies and <c>@id</c> forms are left to runtime.
        /// </summary>
        public static IEnumerable<LintIssue> CheckListRowSource(ElementNode n, bool hasListAncestor)
        {
            if (hasListAncestor) yield break;
            if (!StateTriggerTags.Contains(n.Tag)) yield break;
            if (!n.Attributes.TryGetValue("on", out var on)) yield break;
            if (!BareListRowValues.Contains(on)) yield break;

            yield return new LintIssue(
                NoListRowCode, n.Tag, n.Id,
                $"<{n.Tag} on=\"{on}\">: no <ScrollList> ancestor. {on} is the event of a list row being " +
                "dragged and resolves upward to the row — place it inside a <ScrollList> child or the " +
                $"list's itemTemplate, or use {on}@<id>.");
        }

        private static readonly HashSet<string> StateTriggerTags =
            new HashSet<string> { "Trigger", "Animation", "Show" };

        private static readonly HashSet<string> BareStateValues = new HashSet<string>
        {
            "state-normal", "state-hover", "state-pressed", "state-selected", "state-disabled"
        };

        private static readonly HashSet<string> StateSourceTags =
            new HashSet<string> { "Btn", "Tab", "Toggle" };

        private static readonly HashSet<string> BareMenuValues =
            new HashSet<string> { "expand", "collapse" };

        /// <summary>True if <paramref name="tag"/> instantiates an IStateSource-backed control
        /// (broadcasts InteractState). Extend this set when a new clickable opts in.</summary>
        public static bool IsStateSourceTag(string tag) => StateSourceTags.Contains(tag);

        /// <summary>
        /// Yields <see cref="NoSourceCode"/> when <paramref name="n"/> is a state-* trigger
        /// with a bare (no-<c>@id</c>) state value and no <c>&lt;Btn&gt;</c>/<c>&lt;Tab&gt;</c>/<c>&lt;Toggle&gt;</c> ancestor.
        /// The caller supplies <paramref name="hasStateSourceAncestor"/> and is responsible for the
        /// Template-body / instance-root exemptions.
        /// </summary>
        public static IEnumerable<LintIssue> CheckStateSource(ElementNode n, bool hasStateSourceAncestor)
        {
            if (hasStateSourceAncestor) yield break;
            if (!StateTriggerTags.Contains(n.Tag)) yield break;
            if (!n.Attributes.TryGetValue("on", out var on)) yield break;
            // @id forms resolve against ScopedIds at runtime — can't be checked statically.
            if (!BareStateValues.Contains(on)) yield break;

            yield return new LintIssue(
                NoSourceCode, n.Tag, n.Id,
                $"<{n.Tag} on=\"{on}\">: no <Btn>/<Tab>/<Toggle> ancestor. state-* resolves upward to the " +
                "nearest clickable — place it inside a <Btn>/<Tab>/<Toggle>, or use state-...@<id>.");
        }

        private static readonly HashSet<string> BareCheckedValues =
            new HashSet<string> { "checked", "unchecked" };

        private static readonly HashSet<string> ToggleSourceTags =
            new HashSet<string> { "Tab", "Toggle" };

        /// <summary>True if <paramref name="tag"/> instantiates a control with a persistent on/off
        /// state — the source a bare <c>checked</c> / <c>unchecked</c> trigger resolves upward to.
        /// <c>&lt;Btn&gt;</c> is deliberately absent: it has no such state.</summary>
        public static bool IsToggleSourceTag(string tag) => ToggleSourceTags.Contains(tag);

        /// <summary>
        /// Yields <see cref="NoToggleSourceCode"/> for a bare (no-<c>@id</c>) <c>checked</c> /
        /// <c>unchecked</c> trigger with no <c>&lt;Toggle&gt;</c> / <c>&lt;Tab&gt;</c> ancestor —
        /// same upward rule and same runtime hard error as <c>state-*</c>.
        /// </summary>
        public static IEnumerable<LintIssue> CheckCheckedSource(ElementNode n, bool hasToggleAncestor)
        {
            if (hasToggleAncestor) yield break;
            if (!StateTriggerTags.Contains(n.Tag)) yield break;
            if (!n.Attributes.TryGetValue("on", out var on)) yield break;
            if (!BareCheckedValues.Contains(on)) yield break;

            yield return new LintIssue(
                NoToggleSourceCode, n.Tag, n.Id,
                $"<{n.Tag} on=\"{on}\">: no <Toggle>/<Tab> ancestor. {on} follows a persistent on/off " +
                $"state, which only those two have — place it inside one, or use {on}@<id>. " +
                "(A <Btn> has no checked state; for press feedback use state-pressed.)");
        }

        private static readonly HashSet<string> MenuSourceTags =
            new HashSet<string> { "TabMenu", "Collapsible", "Popover" };

        /// <summary>True if <paramref name="tag"/> instantiates something that opens and closes —
        /// a <c>&lt;TabMenu&gt;</c> popup, a <c>&lt;Collapsible&gt;</c> fold or a <c>&lt;Popover&gt;</c> — the source a bare
        /// <c>expand</c> / <c>collapse</c> trigger resolves upward to.</summary>
        public static bool IsMenuSourceTag(string tag) => MenuSourceTags.Contains(tag);

        /// <summary>
        /// Yields <see cref="NoMenuCode"/> when <paramref name="n"/> is a bare (no-<c>@id</c>)
        /// <c>expand</c> / <c>collapse</c> trigger with no <c>&lt;TabMenu&gt;</c> /
        /// <c>&lt;Collapsible&gt;</c> ancestor — the same upward-resolution rule <c>state-*</c>
        /// follows, and the same runtime hard error.
        /// </summary>
        public static IEnumerable<LintIssue> CheckMenuSource(ElementNode n, bool hasMenuAncestor)
        {
            if (hasMenuAncestor) yield break;
            if (!StateTriggerTags.Contains(n.Tag)) yield break;
            if (!n.Attributes.TryGetValue("on", out var on)) yield break;
            if (!BareMenuValues.Contains(on)) yield break;

            yield return new LintIssue(
                NoMenuCode, n.Tag, n.Id,
                $"<{n.Tag} on=\"{on}\">: no <TabMenu>/<Collapsible>/<Popover> ancestor. {on} resolves upward " +
                $"to the nearest one — place it inside one, or use {on}@<id>.");
        }

        /// <summary>
        /// <c>PUI-SHOW-EXPAND-SELF</c> (spec 2026-10-19 §7): a bare <c>&lt;Show on="expand|collapse"&gt;</c>
        /// inside the panel of the source it resolves to — a <c>&lt;Popover&gt;</c>, a <c>&lt;TabMenu&gt;</c>, a
        /// <c>&lt;Collapsible&gt;</c>'s body. That panel only shows while open, so an expand block is always on
        /// when seen and a collapse block never is. Judged by the NEAREST source: the state is set afresh at
        /// each one (a <c>&lt;Header&gt;</c> is outside its collapsible's panel) and dropped at a template
        /// invocation — the expanded pass sees what it holds. Stamps its own source.
        /// </summary>
        public static IEnumerable<LintIssue> CheckShowExpandSelf(ElementNode root)
        {
            var found = new List<LintIssue>();
            if (root != null) VisitShowExpand(root, inOwnPanel: false, found);
            return found;
        }

        private static void VisitShowExpand(ElementNode n, bool inOwnPanel, List<LintIssue> found)
        {
            if (inOwnPanel && n.Tag == "Show" && n.Namespace == null
                && n.Attributes.TryGetValue("on", out var on) && BareMenuValues.Contains(on))
                found.Add(new LintIssue(
                        ShowExpandSelfCode, n.Tag, n.Id,
                        $"<Show on=\"{on}\"> inside the panel it follows: the panel is only seen while open, so " +
                        (on == "expand" ? "this block is always shown when seen" : "this block is never seen") +
                        $". Put it outside the panel — on the trigger, or in a <Collapsible>'s <Header> — and point " +
                        $"at the source with {on}@<id>.")
                    .WithSource(n.OriginSrc, n.Line, n.InvokedAt));

            var isSource = n.Namespace == null && MenuSourceTags.Contains(n.Tag);
            var isInvocation = n.Namespace != null || (!BuiltinTags.IsBuiltin(n.Tag) && n.Tag != "Header");
            foreach (var child in n.Children)
            {
                var state = inOwnPanel;
                if (isInvocation) state = false;
                else if (isSource) state = !(n.Tag == "Collapsible" && child.Tag == "Header");
                VisitShowExpand(child, state, found);
            }
        }
    }
}
