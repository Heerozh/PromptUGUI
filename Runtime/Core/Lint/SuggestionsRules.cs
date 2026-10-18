using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>&lt;Suggestions&gt;</c> — the suggestion list an <c>&lt;InputField&gt;</c> takes as a part
    /// element (spec 2026-10-18 §7). Placement rules need the parent and so live in the child loop
    /// (<see cref="CheckOutside"/> / <see cref="CheckDuplicate"/>); <c>ScreenInstantiator</c> warns
    /// with the same issues at runtime.
    /// </summary>
    public static class SuggestionsRules
    {
        public const string OutsideCode = "PUI-SUGGEST-OUTSIDE";
        public const string DuplicateCode = "PUI-SUGGEST-DUPLICATE";
        public const string ValueCode = "PUI-SUGGEST-VALUE";
        public const string RowSelectableCode = "PUI-SUGGEST-ROW-SELECTABLE";

        public const string Tag = "Suggestions";

        /// <summary>The one tag that adopts it.</summary>
        public const string HostTag = "InputField";

        /// <summary>A <c>&lt;Suggestions&gt;</c> anywhere but directly under an <c>&lt;InputField&gt;</c>.</summary>
        /// <remarks>
        /// A parent the CLI cannot name — a Template invocation — is skipped: its <c>&lt;Slot/&gt;</c>
        /// may sit right inside a field, and the expanded pass sees the truth.
        /// </remarks>
        public static IEnumerable<LintIssue> CheckOutside(ElementNode parent, ElementNode child)
        {
            if (child.Tag != Tag || parent.Tag == HostTag) yield break;
            if (!BuiltinTags.IsBuiltin(parent.Tag)) yield break;
            yield return new LintIssue(OutsideCode, child.Tag, child.Id, OutsideMessage(child.Id, parent.Tag));
        }

        /// <summary>Two lists under one field: the first is the list, the rest are parked.</summary>
        public static IEnumerable<LintIssue> CheckDuplicate(ElementNode host)
        {
            if (host.Tag != HostTag) yield break;
            var seen = false;
            foreach (var c in host.Children)
            {
                if (c.Tag != Tag) continue;
                if (!seen) { seen = true; continue; }
                yield return new LintIssue(DuplicateCode, c.Tag, c.Id, DuplicateMessage(host.Id, c.Id))
                    .WithSource(c.OriginSrc, c.Line, c.InvokedAt);
            }
        }

        public static string OutsideMessage(string id, string parentTag) =>
            $"<Suggestions id='{id}'> is only meaningful as a direct child of <InputField>; found under " +
            $"<{parentTag}>, where no field opens it — the list stays closed.";

        public static string DuplicateMessage(string hostId, string id) =>
            $"<InputField id='{hostId}'>: a second <Suggestions> (id='{id}') — a field takes one list; " +
            "the first in document order is used and this one is parked inactive.";
    }
}
