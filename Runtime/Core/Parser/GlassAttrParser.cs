using System.Globalization;

namespace PromptUGUI.Parser
{
    /// <summary>
    /// Value grammar for the glass attributes (<c>glass</c> / <c>frost</c> / <c>depth</c> /
    /// <c>dispersion</c> / <c>lightAngle</c> / <c>lightIntensity</c> / <c>saturation</c> /
    /// <c>noise</c> / <c>weld</c> / <c>seam</c>).
    ///
    /// Pure C# — no UnityEngine types — so the UIXmlLint CLI shares this exact implementation with
    /// the runtime setters and the two can never drift on either the accepted range or the wording
    /// of the error. Each attribute's range and default live here and nowhere else.
    /// </summary>
    public static class GlassAttrParser
    {
        public const string Glass = "glass";
        public const string Frost = "frost";
        public const string Depth = "depth";
        public const string Dispersion = "dispersion";
        public const string LightAngle = "lightAngle";
        public const string LightIntensity = "lightIntensity";
        public const string Saturation = "saturation";
        public const string Noise = "noise";
        public const string Weld = "weld";
        public const string Seam = "seam";

        public const float DefaultFrost = 0.5f;
        public const float DefaultDepth = 4f;
        public const float DefaultDispersion = 0f;
        public const float DefaultLightAngle = 0f;
        public const float DefaultLightIntensity = 0.6f;
        public const float DefaultSaturation = 1.15f;
        public const float DefaultNoise = 0.02f;
        public const float DefaultWeld = 0f;
        public const float DefaultSeam = 3f;

        /// <summary>Every numeric glass attribute — the set the lint layer validates.</summary>
        public static readonly string[] NumericAttrs =
        {
            Frost, Depth, Dispersion, LightAngle, LightIntensity, Saturation, Noise, Weld, Seam,
        };

        public static bool IsNumericAttr(string name)
        {
            foreach (var n in NumericAttrs)
                if (string.Equals(n, name, System.StringComparison.Ordinal)) return true;
            return false;
        }

        /// <summary>
        /// Parses one numeric glass attribute against its own range.
        ///
        /// Empty / whitespace resolves to the attribute's default rather than erroring: a Variant can
        /// only override an attribute's value, never remove it, so <c>frost.desktop=""</c> is the
        /// author's only way back to the default and must stay legal (same rule as
        /// <see cref="RadiusParser"/>).
        /// </summary>
        public static bool TryParseValue(string attrName, string value,
                                         out float result, out string error)
        {
            error = null;
            GetRange(attrName, out var min, out var max, out var fallback);
            result = fallback;

            if (string.IsNullOrWhiteSpace(value)) return true;

            var raw = value.Trim();
            if (!float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                result = fallback;
                error = $"{attrName}=\"{value}\": expected a number (e.g. \"0.5\")";
                return false;
            }

            // InvariantCulture's float parser accepts "NaN" / "Infinity", and the range test below
            // is false for NaN whatever the bounds are — so without this both walk straight into a
            // shader uniform and produce undefined output with no diagnostic anywhere.
            if (float.IsNaN(result) || float.IsInfinity(result))
            {
                result = fallback;
                error = $"{attrName}=\"{value}\": must be a finite number";
                return false;
            }

            if (result < min || result > max)
            {
                var range = float.IsPositiveInfinity(max)
                    ? $"must not be less than {min.ToString(CultureInfo.InvariantCulture)}"
                    : $"must be between {min.ToString(CultureInfo.InvariantCulture)} and " +
                      max.ToString(CultureInfo.InvariantCulture);
                result = fallback;
                error = $"{attrName}=\"{value}\": {range}";
                return false;
            }

            return true;
        }

        /// <summary>Throwing wrapper for the runtime attribute setters.</summary>
        public static float ParseValue(string attrName, string value)
            => TryParseValue(attrName, value, out var v, out var error)
                ? v
                : throw new ParseException(error);

        /// <summary>
        /// Parses <c>glass</c>. Empty resolves to <c>false</c> (the Variant escape hatch above);
        /// anything other than <c>true</c> / <c>false</c> is an error rather than a silent false —
        /// <c>glass="yes"</c> looks like it works and would leave the author staring at a plain box.
        /// </summary>
        public static bool TryParseFlag(string attrName, string value, out bool result, out string error)
        {
            error = null;
            result = false;

            if (string.IsNullOrWhiteSpace(value)) return true;

            var raw = value.Trim();
            if (string.Equals(raw, "true", System.StringComparison.Ordinal)) { result = true; return true; }
            if (string.Equals(raw, "false", System.StringComparison.Ordinal)) return true;

            error = $"{attrName}=\"{value}\": expected \"true\" or \"false\"";
            return false;
        }

        public static bool ParseFlag(string attrName, string value)
            => TryParseFlag(attrName, value, out var v, out var error)
                ? v
                : throw new ParseException(error);

        private static void GetRange(string attrName, out float min, out float max, out float fallback)
        {
            switch (attrName)
            {
                case Frost: min = 0f; max = 1f; fallback = DefaultFrost; return;
                case Dispersion: min = 0f; max = 1f; fallback = DefaultDispersion; return;
                case LightIntensity: min = 0f; max = 1f; fallback = DefaultLightIntensity; return;
                case Noise: min = 0f; max = 1f; fallback = DefaultNoise; return;
                case Depth: min = 0f; max = float.PositiveInfinity; fallback = DefaultDepth; return;
                case Saturation: min = 0f; max = float.PositiveInfinity; fallback = DefaultSaturation; return;
                case Weld: min = 0f; max = float.PositiveInfinity; fallback = DefaultWeld; return;
                // Signed: the magnitude is how far the thickness step's ramp reaches, the sign is
                // which side of the raised block's contour it falls on (+ outside, - inside). 0 is
                // legal and means "as sharp as this screen can draw it" — the group shader floors
                // the magnitude at two device pixels, so the step never thins away to nothing.
                case Seam:
                    min = float.NegativeInfinity; max = float.PositiveInfinity;
                    fallback = DefaultSeam; return;
                // An angle is cyclic: -30 and 330 are the same light direction, and clamping either
                // would silently move the highlight. Accept the whole line and wrap in the shader.
                case LightAngle:
                    min = float.NegativeInfinity; max = float.PositiveInfinity;
                    fallback = DefaultLightAngle; return;
                default:
                    min = float.NegativeInfinity; max = float.PositiveInfinity; fallback = 0f; return;
            }
        }
    }
}
