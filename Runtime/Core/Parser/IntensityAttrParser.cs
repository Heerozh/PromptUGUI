using System.Globalization;

namespace PromptUGUI.Parser
{
    /// <summary>
    /// Value grammar for <c>intensity</c> — the exposure knob of a procedural surface, a
    /// <c>&lt;Decor&gt;</c> or a sprite (spec 2026-09-12 §4). <c>1</c> is today's rendering, larger
    /// is brighter; there is no upper bound because the curve saturates gracefully, and nothing
    /// below <c>1</c> because under-exposure is what <c>*Modulate</c> already does.
    ///
    /// <para>Pure C# — no UnityEngine types — so the UIXmlLint CLI shares this exact implementation
    /// with the runtime setters and the two can never drift on the accepted range or the wording
    /// of the error. Deliberately NOT a <see cref="GlassAttrParser"/> entry: <c>GlassRules</c> turns
    /// that parser's numeric attributes into <c>PUI-GLASS-PARAM-NO-GLASS</c>, and intensity is
    /// precisely the attribute a glass surface does not take.</para>
    /// </summary>
    public static class IntensityAttrParser
    {
        public const string Name = "intensity";
        public const float Default = 1f;
        public const float Min = 1f;

        /// <summary>
        /// Empty / whitespace resolves to <see cref="Default"/> rather than erroring: a Variant can
        /// only override an attribute's value, never remove it, so <c>intensity.mobile=""</c> is the
        /// author's only way back to "not lit" (same rule as <see cref="GlassAttrParser"/>).
        /// </summary>
        public static bool TryParse(string value, out float result, out string error)
        {
            error = null;
            result = Default;

            if (string.IsNullOrWhiteSpace(value)) return true;

            var raw = value.Trim();
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                result = Default;
                error = $"{Name}=\"{value}\": expected a number (e.g. \"3\")";
                return false;
            }

            // InvariantCulture's float parser accepts "NaN" / "Infinity", and the range test below
            // is false for NaN whatever the bound is — so without this both walk straight into a
            // shader uniform and produce undefined output with no diagnostic anywhere.
            if (float.IsNaN(result) || float.IsInfinity(result))
            {
                result = Default;
                error = $"{Name}=\"{value}\": must be a finite number";
                return false;
            }

            if (result < Min)
            {
                result = Default;
                error = $"{Name}=\"{value}\": must not be less than {Min.ToString(CultureInfo.InvariantCulture)}";
                return false;
            }

            return true;
        }

        /// <summary>Throwing wrapper for the runtime attribute setters.</summary>
        public static float Parse(string value)
            => TryParse(value, out var v, out var error)
                ? v
                : throw new ParseException(error);
    }
}
