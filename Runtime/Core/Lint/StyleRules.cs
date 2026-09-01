using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Static checks for the <c>&lt;Style&gt;</c> / <c>class=</c> system and the procedural visual
    /// attribute values it most often carries.
    ///
    /// Note what is deliberately absent: "unknown class name". Commons libraries register their
    /// styles at runtime, so from the CLI's single-file view no name can ever be *proven* missing —
    /// the check would fire on every correct project that keeps its styles in a shared library.
    /// That one is left to the runtime <c>StyleMerger</c>, which does have the full pool.
    /// </summary>
    public static class StyleRules
    {
        public const string ClassEmptyCode = "PUI-CLASS-EMPTY";
        public const string ProceduralValueCode = "PUI-PROCEDURAL-VALUE";

        private static readonly char[] ClassSeparators = { ' ', '\t', '\n', '\r' };

        /// <summary>Attributes parsed as a plain non-negative pixel count.</summary>
        private static readonly string[] PixelAttrs = { "borderWidth", "glow", "innerGlow", "blur" };

        public static IEnumerable<LintIssue> Check(ElementNode n)
        {
            foreach (var issue in CheckClass(n)) yield return issue;

            foreach (var (name, value) in AllValues(n))
            {
                foreach (var issue in CheckValue($"<{n.Tag} id='{n.Id}'>", n.Tag, n.Id, name, value))
                    yield return issue;
            }
        }

        /// <summary>
        /// A <c>&lt;Style&gt;</c> is not an <see cref="ElementNode"/>, so <see cref="IRWalker"/>
        /// cannot reach its values through the tree walk — they get their own pass.
        /// </summary>
        public static IEnumerable<LintIssue> CheckStyle(StyleDef style)
        {
            var context = $"<Style name='{style.Name}'>";

            foreach (var kv in style.Attributes)
                foreach (var issue in CheckValue(context, "Style", style.Name, kv.Key, kv.Value))
                    yield return issue;

            foreach (var kv in style.VariantOverrides)
                foreach (var (variant, value) in kv.Value)
                    foreach (var issue in CheckValue(context, "Style", style.Name,
                                                     kv.Key + "." + variant, value))
                        yield return issue;
        }

        private static IEnumerable<LintIssue> CheckClass(ElementNode n)
        {
            if (!n.Attributes.TryGetValue("class", out var value)) yield break;
            if (value != null && value.Contains("{{")) yield break;   // resolved at expansion time

            var names = value == null
                ? System.Array.Empty<string>()
                : value.Split(ClassSeparators, System.StringSplitOptions.RemoveEmptyEntries);

            if (names.Length > 0) yield break;

            yield return new LintIssue(
                ClassEmptyCode, n.Tag, n.Id,
                $"<{n.Tag} id='{n.Id}'>: class=\"{value}\" names no style. " +
                "Write class=\"some-style\" or drop the attribute.");
        }

        private static IEnumerable<LintIssue> CheckValue(string context, string tag, string id,
                                                        string attrName, string value)
        {
            // `attr.variant` carries the same value grammar as its base name.
            var dot = attrName.IndexOf('.');
            var baseName = dot < 0 ? attrName : attrName.Substring(0, dot);

            // Template params are substituted after lint time; the final value is unknowable here.
            if (value != null && value.Contains("{{")) yield break;

            if (baseName == "radius")
            {
                if (!RadiusParser.TryParse(value, out _, out var error))
                    yield return new LintIssue(ProceduralValueCode, tag, id,
                        $"{context}: {error}");
                yield break;
            }

            foreach (var pixelAttr in PixelAttrs)
            {
                if (baseName != pixelAttr) continue;
                if (string.IsNullOrWhiteSpace(value)) yield break;
                var trimmed = value.Trim();
                if (!float.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var px))
                    yield return new LintIssue(ProceduralValueCode, tag, id,
                        $"{context}: {attrName}=\"{value}\" is not a number of pixels " +
                        "(e.g. \"1\", \"2.5\")");
                else if (px < 0f)
                    yield return new LintIssue(ProceduralValueCode, tag, id,
                        $"{context}: {attrName}=\"{value}\" must not be negative");
                yield break;
            }

            // Glass values share the runtime parser, so the CLI rejects exactly what the setter
            // would have thrown on — including each attribute's own range.
            if (GlassAttrParser.IsNumericAttr(baseName))
            {
                if (!GlassAttrParser.TryParseValue(baseName, value, out _, out var glassError))
                    yield return new LintIssue(ProceduralValueCode, tag, id,
                        $"{context}: {glassError}");
                yield break;
            }

            if (baseName == GlassAttrParser.Glass
                && !GlassAttrParser.TryParseFlag(baseName, value, out _, out var flagError))
                yield return new LintIssue(ProceduralValueCode, tag, id, $"{context}: {flagError}");
        }

        private static IEnumerable<(string Name, string Value)> AllValues(ElementNode n)
        {
            foreach (var kv in n.Attributes)
                yield return (kv.Key, kv.Value);
            foreach (var kv in n.VariantOverrides)
                foreach (var (variant, value) in kv.Value)
                    yield return (kv.Key + "." + variant, value);
        }
    }
}
