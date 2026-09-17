using System.Globalization;

namespace PromptUGUI.Parser
{
    /// <summary>
    /// Value grammar for <c>hazeDensity</c> — how much of a procedural surface the noise fog covers
    /// (spec 2026-09-17 haze H-D7). <c>0</c> is sparse light patches with bare fill between them,
    /// <c>1</c> is the raw cloud field as-is (thin mist everywhere), the default <c>0.5</c> is the
    /// reference art's look. The knob moves the coverage map's black and white points and fades its
    /// S-curve into a straight line; see <c>PuguiHazeWeight</c> in UI-PanelSDF.cginc.
    ///
    /// <para>Pure C# — no UnityEngine types — so the UIXmlLint CLI shares this exact implementation
    /// with the runtime setters and the two can never drift on the accepted range or the wording
    /// of the error. Deliberately NOT a <see cref="GlassAttrParser"/> entry, for the same reason
    /// <see cref="IntensityAttrParser"/> is not: glass is precisely the surface this attribute does
    /// not apply to.</para>
    /// </summary>
    public static class HazeDensityAttrParser
    {
        public const string Name = "hazeDensity";
        public const float Default = 0.5f;
        public const float Min = 0f;
        public const float Max = 1f;

        /// <summary>
        /// Empty / whitespace resolves to <see cref="Default"/> rather than erroring: a Variant can
        /// only override an attribute's value, never remove it, so <c>hazeDensity.mobile=""</c> is
        /// the author's only way back to the default (same rule as <see cref="IntensityAttrParser"/>).
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
                error = $"{Name}=\"{value}\": expected a number between 0 and 1 (e.g. \"0.5\")";
                return false;
            }

            // InvariantCulture's float parser accepts "NaN" / "Infinity", and the range test below
            // is false for NaN whatever the bounds are — so without this it would walk straight into
            // a shader uniform and produce undefined output with no diagnostic anywhere.
            if (float.IsNaN(result) || float.IsInfinity(result))
            {
                result = Default;
                error = $"{Name}=\"{value}\": must be a finite number";
                return false;
            }

            if (result < Min || result > Max)
            {
                result = Default;
                error = $"{Name}=\"{value}\": must be between 0 (sparse patches) and 1 (the whole field)";
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
