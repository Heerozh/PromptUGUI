using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Nav-override lint rules for <c>nav*</c>/<c>focus</c> attributes.
    ///
    /// <para>
    /// <b>PUI-NAV-ON-NON-SELECTABLE</b>: <c>nav*</c> or <c>focus</c> is written on an element
    /// whose tag has no <c>Selectable</c> component at runtime (only Btn, Tab, Toggle, Slider,
    /// Dropdown, InputField, ScrollList do). The runtime silently ignores such attributes;
    /// this rule surfaces the mistake statically.
    /// Shared by <c>IRWalker</c> (CLI) and <c>ScreenInstantiator</c> (runtime warning) — a
    /// universal per-node check, like <c>ColorLiteralRules</c>.
    /// </para>
    ///
    /// <para>
    /// <b>PUI-NAV-UNKNOWN-TARGET</b>: a <c>navUp/navDown/navLeft/navRight</c> value that
    /// does not match any <c>id</c> in the same Screen (best-effort pre-expansion id set).
    /// CLI-only — the runtime already hard-throws (<c>KeyNotFoundException</c> in
    /// <c>ExplicitNavigationResolver</c>) for missing ids; no ScreenInstantiator mirror needed.
    /// </para>
    /// </summary>
    public static class NavTargetRules
    {
        public const string NonSelectableCode = "PUI-NAV-ON-NON-SELECTABLE";
        public const string UnknownTargetCode = "PUI-NAV-UNKNOWN-TARGET";

        /// <summary>Tags that have a uGUI <c>Selectable</c> component and therefore accept nav* attrs.</summary>
        private static readonly HashSet<string> SelectableTags = new HashSet<string>
        {
            "Btn", "Tab", "Toggle", "Slider", "Dropdown", "InputField", "ScrollList"
        };

        /// <summary>All attributes that must appear only on selectable tags.</summary>
        private static readonly string[] AllNavAttrs =
        {
            "nav", "navUp", "navDown", "navLeft", "navRight", "focus"
        };

        /// <summary>Directional attributes whose values must be resolvable ids.</summary>
        private static readonly string[] DirectionalNavAttrs =
        {
            "navUp", "navDown", "navLeft", "navRight"
        };

        /// <summary>
        /// Runtime + CLI: yields <see cref="NonSelectableCode"/> when <paramref name="n"/>
        /// carries any <c>nav*</c>/<c>focus</c> attribute but its tag is not selectable.
        /// At most one issue per node (first offending attribute is reported).
        /// </summary>
        public static IEnumerable<LintIssue> CheckNav(ElementNode n)
        {
            if (SelectableTags.Contains(n.Tag)) yield break;

            string offender = null;
            foreach (var attr in AllNavAttrs)
            {
                if (n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr))
                {
                    offender = attr;
                    break;
                }
            }

            if (offender == null) yield break;

            yield return new LintIssue(
                NonSelectableCode, n.Tag, n.Id,
                $"<{n.Tag} id='{n.Id}'>: \"{offender}\" has no effect on a non-selectable tag. " +
                "nav*/focus only work on Btn, Tab, Toggle, Slider, Dropdown, InputField, ScrollList.");
        }

        /// <summary>
        /// CLI-only: yields <see cref="UnknownTargetCode"/> for each
        /// <c>navUp/navDown/navLeft/navRight</c> value that is not present in
        /// <paramref name="screenIds"/> (best-effort pre-expansion same-Screen id set).
        /// Skipped when <paramref name="screenIds"/> is null or empty.
        /// </summary>
        public static IEnumerable<LintIssue> CheckNavTarget(ElementNode n, HashSet<string> screenIds)
        {
            if (screenIds == null || screenIds.Count == 0) yield break;

            foreach (var attr in DirectionalNavAttrs)
            {
                if (!n.Attributes.TryGetValue(attr, out var value)) continue;
                if (string.IsNullOrEmpty(value)) continue;
                if (screenIds.Contains(value)) continue;

                yield return new LintIssue(
                    UnknownTargetCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: {attr}=\"{value}\" — no element with id \"{value}\" found " +
                    "in the same Screen. Check for typos; ids are case-sensitive.");
            }
        }
    }
}
