using System;
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>width="clamp(min, N%, max)"</c> / <c>height="clamp(...)"</c> (spec 2026-08-30-clamp-size-design
    /// §6.5). String-level detection only — <c>SizeSpec</c> (Core/Layout) is outside the CLI compile set,
    /// and "does this node declare a clamp" needs no parse.
    /// </summary>
    public static class ClampRules
    {
        public const string ClampScaleCode = "PUI-CLAMP-SCALE";

        private static readonly string[] SizeAxes = { "width", "height" };

        /// <summary>True when the raw attribute value is the clamp function form.</summary>
        public static bool IsClampValue(string value) =>
            value != null && value.TrimStart().StartsWith("clamp(", StringComparison.Ordinal);

        /// <summary>
        /// True when <c>width</c> or <c>height</c> carries a clamp in its base value or in ANY variant
        /// override. Declared, not resolved: the CLI resolves no variants, and the runtime check
        /// mirrors it so both see the same set of documents.
        /// </summary>
        public static bool HasClamp(ElementNode n)
        {
            foreach (var axis in SizeAxes)
            {
                if (n.Attributes.TryGetValue(axis, out var baseValue) && IsClampValue(baseValue))
                    return true;
                if (n.VariantOverrides.TryGetValue(axis, out var overrides))
                    foreach (var (_, value) in overrides)
                        if (IsClampValue(value)) return true;
            }
            return false;
        }

        /// <summary>
        /// <c>PUI-CLAMP-SCALE</c>: a clamped axis together with <c>scale</c> (any form, base or variant)
        /// on the same node. The fitter is the last writer on that axis and would drop the
        /// box-preserving inflation. CLI error; runtime <c>ParseException</c> (a hard error, not the
        /// usual warning channel — author decision, spec §10 decision 4).
        /// </summary>
        public static IEnumerable<LintIssue> CheckClampScale(ElementNode n)
        {
            if (!HasClamp(n)) yield break;
            if (!n.Attributes.ContainsKey("scale") && !n.VariantOverrides.ContainsKey("scale")) yield break;
            yield return new LintIssue(
                ClampScaleCode, n.Tag, n.Id,
                $"<{n.Tag} id='{n.Id}'>: width/height=\"clamp(...)\" and scale=\"...\" on the same node — " +
                "a clamped axis is owned by the layout pass, which would drop the box-preserving inflation " +
                "that scale applies. Fix: move scale to a child (wrap the content) or drop the clamp.");
        }
    }
}
