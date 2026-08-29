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

        /// <summary>True when the raw attribute value is the clamp function form.</summary>
        public static bool IsClampValue(string value) => false;

        /// <summary>
        /// True when <c>width</c> or <c>height</c> carries a clamp in its base value or in ANY variant
        /// override. Declared, not resolved: the CLI resolves no variants, and the runtime check
        /// mirrors it so both see the same set of documents.
        /// </summary>
        public static bool HasClamp(ElementNode n) => false;

        /// <summary>
        /// <c>PUI-CLAMP-SCALE</c>: a clamped axis together with <c>scale</c> (any form, base or variant)
        /// on the same node. The fitter is the last writer on that axis and would drop the
        /// box-preserving inflation. CLI error; runtime <c>ParseException</c> (a hard error, not the
        /// usual warning channel — author decision, spec §10 decision 4).
        /// </summary>
        public static IEnumerable<LintIssue> CheckClampScale(ElementNode n)
        {
            yield break;
        }
    }
}
