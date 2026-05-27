using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint rules for the &lt;Progress&gt; control's attribute family.
    /// Consumed by both <c>IRWalker</c> (UIXmlLint CLI) and <c>ScreenInstantiator</c>
    /// (runtime warnings). Single source of truth — mirrors MaskAttributeRules.
    /// </summary>
    public static class ProgressAttributeRules
    {
        public const string ValueRangeCode = "PUI-PROG-VALUE-RANGE";
        public const string ModeCode = "PUI-PROG-MODE";
        public const string DirectionCode = "PUI-PROG-DIRECTION";
        public const string ChildrenCode = "PUI-PROG-CHILDREN";
        public const string MaskVariantCode = "PUI-PROG-MASK-VARIANT";
        public const string NoFillCode = "PUI-PROG-NO-FILL";

        private static readonly HashSet<string> ValidModes = new HashSet<string> { "scale", "fill" };
        private static readonly HashSet<string> ValidDirections = new HashSet<string>
        {
            "horizontal", "vertical", "reverse-horizontal", "reverse-vertical"
        };

        public static IEnumerable<LintIssue> CheckProgress(ElementNode n)
        {
            // value range (literal only — dynamic bindings parse as non-numeric and skip)
            if (n.Attributes.TryGetValue("value", out var rawValue)
                && float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && (v < 0f || v > 1f))
            {
                yield return new LintIssue(
                    ValueRangeCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: value='{rawValue}' is outside [0..1] and will be clamped. " +
                    "Adjust the literal, or ignore this if you bind value dynamically.");
            }

            // mode
            if (n.Attributes.TryGetValue("mode", out var mode) && !ValidModes.Contains(mode))
            {
                yield return new LintIssue(
                    ModeCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: mode='{mode}' is invalid. Valid: scale, fill.");
            }

            // direction
            if (n.Attributes.TryGetValue("direction", out var dir) && !ValidDirections.Contains(dir))
            {
                yield return new LintIssue(
                    DirectionCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: direction='{dir}' is invalid. " +
                    "Valid: horizontal, vertical, reverse-horizontal, reverse-vertical.");
            }

            // children
            if (n.Children.Count > 0)
            {
                yield return new LintIssue(
                    ChildrenCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: Progress is a leaf control and does not accept child elements. " +
                    "Use the frame / mask / bg / fill attributes to compose the visual layers.");
            }

            // mask variant override
            if (n.VariantOverrides.ContainsKey("mask"))
            {
                yield return new LintIssue(
                    MaskVariantCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: mask cannot be overridden per Variant (would require " +
                    "AddComponent/Destroy at runtime). Fix mask in the base declaration; other attrs " +
                    "(value / fill / bg / mode / direction) are safe in variants.");
            }

            // no fill (warning when value is set but no fill or fillColor)
            if (n.Attributes.ContainsKey("value")
                && !n.Attributes.ContainsKey("fill")
                && !n.Attributes.ContainsKey("fillColor"))
            {
                yield return new LintIssue(
                    NoFillCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: value is set but neither fill nor fillColor — " +
                    "nothing will be visibly filled.");
            }
        }
    }
}
