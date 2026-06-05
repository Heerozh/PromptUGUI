using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Static check on <c>color="..."</c> attribute values. Only flags hex literals
    /// that fail to parse (values starting with '#'). Bare words are deliberately not
    /// flagged — they may be tokens registered in a theme file not visible to this
    /// lint pass.
    ///
    /// Consumed by both <c>IRWalker</c> (UIXmlLint CLI, build-time errors) and
    /// <c>ScreenInstantiator</c> (runtime warnings). Single source of truth.
    /// </summary>
    public static class ColorLiteralRules
    {
        public const string ColorLiteralCode = "PUI-COLOR-LITERAL-INVALID";

        public static IEnumerable<LintIssue> Check(ElementNode node)
        {
            if (!node.Attributes.TryGetValue("color", out var value)) yield break;
            if (string.IsNullOrEmpty(value)) yield break;

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
