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

        private static readonly HashSet<string> StateTriggerTags =
            new HashSet<string> { "Trigger", "Animation", "Show" };

        private static readonly HashSet<string> BareStateValues = new HashSet<string>
        {
            "state-normal", "state-hover", "state-pressed", "state-selected", "state-disabled"
        };

        /// <summary>
        /// Yields <see cref="NoSourceCode"/> when <paramref name="n"/> is a state-* trigger
        /// with a bare (no-<c>@id</c>) state value and no <c>&lt;Btn&gt;</c> ancestor.
        /// The caller supplies <paramref name="hasBtnAncestor"/> and is responsible for the
        /// Template-body / instance-root exemptions.
        /// </summary>
        public static IEnumerable<LintIssue> CheckStateSource(ElementNode n, bool hasBtnAncestor)
        {
            if (hasBtnAncestor) yield break;
            if (!StateTriggerTags.Contains(n.Tag)) yield break;
            if (!n.Attributes.TryGetValue("on", out var on)) yield break;
            // @id forms resolve against ScopedIds at runtime — can't be checked statically.
            if (!BareStateValues.Contains(on)) yield break;

            yield return new LintIssue(
                NoSourceCode, n.Tag, n.Id,
                $"<{n.Tag} on=\"{on}\">: no <Btn> ancestor. state-* resolves upward to the " +
                "nearest <Btn> — place it inside a <Btn>, or use state-...@<id>.");
        }
    }
}
