using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Static check on <c>color="..."</c> attribute values. Checks gradient structure first
    /// (more than two segments, or an empty segment → <c>PUI-COLOR-GRADIENT-MALFORMED</c>),
    /// then validates each segment as a hex literal that must parse. Bare words (tokens) are
    /// deliberately not flagged — they may be tokens registered in a theme file not visible
    /// to this lint pass.
    ///
    /// Consumed by <c>IRWalker</c> (UIXmlLint CLI, build-time errors). CLI-only; not
    /// dispatched from <c>ScreenInstantiator</c> (the runtime already hard-throws on
    /// malformed gradients at apply time).
    /// </summary>
    public static class ColorLiteralRules
    {
        public const string ColorLiteralCode = "PUI-COLOR-LITERAL-INVALID";
        public const string GradientMalformedCode = "PUI-COLOR-GRADIENT-MALFORMED";

        public static IEnumerable<LintIssue> Check(ElementNode node)
        {
            if (!node.Attributes.TryGetValue("color", out var value)) yield break;
            if (string.IsNullOrEmpty(value)) yield break;

            // Gradient shape first: >2 segments or an empty segment is structurally invalid
            // regardless of whether segments are tokens or hex (tokens can't fix a bad shape).
            if (!ColorParser.TrySplitGradient(value, out var top, out var bottom, out var gErr))
            {
                yield return new LintIssue(GradientMalformedCode, node.Tag, node.Id,
                    $"<{node.Tag} id='{node.Id}'>: {gErr}");
                yield break;
            }

            foreach (var issue in CheckSegment(top, node)) yield return issue;
            if (bottom != null)
                foreach (var issue in CheckSegment(bottom, node)) yield return issue;
        }

        // Existing single-colour validation, now applied per gradient segment.
        private static IEnumerable<LintIssue> CheckSegment(string value, ElementNode node)
        {
            // Reference-site /alpha suffix ("black/0.5"): strip it before the hex check.
            // A malformed suffix on a hex literal is flagged at build time; on a bare word
            // it's left to the runtime resolver (tokens aren't statically checked anyway).
            if (!ColorParser.TrySplitAlpha(value, out var baseValue, out _, out var alphaErr))
            {
                if (value[0] == '#')
                    yield return new LintIssue(ColorLiteralCode, node.Tag, node.Id,
                        $"<{node.Tag} id='{node.Id}'>: {alphaErr}");
                yield break;
            }
            value = baseValue;

            // Bare words (tokens) are not checked statically.
            if (value[0] != '#') yield break;

            // Hex literal: must parse via ColorParser (shared with UIDocumentParser).
            if (ColorParser.TryParseHtmlString(value)) yield break;

            yield return new LintIssue(
                ColorLiteralCode, node.Tag, node.Id,
                $"<{node.Tag} id='{node.Id}'>: invalid color literal value=\"{value}\" — " +
                "invalid color format. " +
                "Valid formats: #RGB, #RRGGBB, #RGBA, #RRGGBBAA (hex digits only).");
        }
    }
}
