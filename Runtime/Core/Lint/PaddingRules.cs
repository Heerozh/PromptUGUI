using System;
using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>padding</c> on a layout group is a uGUI <c>RectOffset</c> — whole pixels, 1 / 2 / 4 comma-separated
    /// integers (top,right,bottom,left; <c>_</c> or nothing = 0). A fraction or a wrong count could not be seen
    /// before Open: <c>&lt;VStack&gt;</c> / <c>&lt;HStack&gt;</c> / <c>&lt;Grid&gt;</c> / <c>&lt;ScrollList&gt;</c> /
    /// <c>&lt;InputField&gt;</c> then throw a ParseException (at Open, or on the hot reload that brought it in),
    /// and <c>&lt;TabBar&gt;</c> / <c>&lt;TabMenu&gt;</c> / <c>&lt;Collapsible&gt;</c> silently read 0.
    /// <c>&lt;Scrollbar padding&gt;</c> is a different grammar (<c>along,across</c> floats, ScrollbarRules).
    /// </summary>
    public static class PaddingRules
    {
        public const string Code = "PUI-PADDING-INT";

        public static readonly HashSet<string> Tags = new HashSet<string>(StringComparer.Ordinal)
        {
            "VStack", "HStack", "Grid", "ScrollList", "InputField", "TabBar", "TabMenu", "Collapsible", "Suggestions",
            "Popover",
        };

        public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles = null)
        {
            if (!Tags.Contains(n.Tag)) yield break;
            styles ??= StyleAttributeView.Empty;
            if (!styles.Declares(n, "padding")) yield break;

            styles.Resolve(n, "padding", out var baseValue, out var variants);
            foreach (var value in Values(baseValue, variants))
            {
                // A template parameter is checked where it is substituted, by the expanded pass.
                if (string.IsNullOrEmpty(value) || value.Contains("{{")) continue;
                if (IsWholePixels(value, out var bad)) continue;
                yield return new LintIssue(
                    Code, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: padding=\"{value}\" — padding is whole pixels: 1, 2 or 4 " +
                    "comma-separated integers (top,right,bottom,left; \"_\" = 0)" +
                    (bad != null ? $", and \"{bad}\" is not an integer" : $", not {value.Split(',').Length} values") +
                    ". A layout group's padding cannot hold anything else: <VStack> / <HStack> / <Grid> / " +
                    "<ScrollList> / <InputField> refuse to open, <TabBar> / <TabMenu> / <Collapsible> read it as 0.");
            }
        }

        /// <summary>The grammar <c>VStack.ParseTRBL</c> accepts; <paramref name="bad"/> is the first part that is not an integer.</summary>
        public static bool IsWholePixels(string value, out string bad)
        {
            bad = null;
            var parts = value.Split(',');
            foreach (var raw in parts)
            {
                var p = raw.Trim();
                if (p.Length == 0 || p == "_") continue;
                if (int.TryParse(p, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)) continue;
                bad = p;
                return false;
            }
            return parts.Length == 1 || parts.Length == 2 || parts.Length == 4;
        }

        private static IEnumerable<string> Values(
            string baseValue, IReadOnlyList<(string Variant, string Value)> variants)
        {
            yield return baseValue;
            foreach (var (_, value) in variants) yield return value;
        }
    }
}
