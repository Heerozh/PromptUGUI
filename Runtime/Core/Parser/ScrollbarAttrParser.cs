using System.Globalization;

namespace PromptUGUI.Parser
{
    /// <summary>
    /// Value grammar for the geometry attributes of <c>&lt;Scrollbar&gt;</c> —
    /// <c>thickness</c> / <c>spacing</c> / <c>padding</c> (spec 2026-09-12-scrollbar-part-element
    /// §4.1, §5.1). Pure C#, shared by the runtime setters and <c>ScrollbarRules</c>, so the CLI
    /// rejects exactly what the setter would throw on, with the same wording.
    ///
    /// <para>Empty / whitespace is the default for every one of them: a Variant can only override a
    /// value, never remove it, so <c>thickness.mobile=""</c> is the author's way back to the stock
    /// bar (the <see cref="IntensityAttrParser"/> convention).</para>
    /// </summary>
    public static class ScrollbarAttrParser
    {
        public const string ThicknessName = "thickness";
        public const string SpacingName = "spacing";
        public const string PaddingName = "padding";

        /// <summary>The stock Scroll View bar.</summary>
        public const float DefaultThickness = 20f;

        /// <summary>
        /// The stock <c>-3</c> overlap, capped at the bar's own thickness: below 3 units the overlap
        /// would make the viewport WIDER than the list (PR #131's rule, unchanged).
        /// </summary>
        public static float DefaultSpacing(float thickness) => System.Math.Max(-thickness, -3f);

        /// <summary>
        /// The handle's size across the bar: the thickness minus the two side insets, never below
        /// 1 so the handle cannot vanish (a zero-thickness bar has no handle at all).
        /// <paramref name="clamped"/> reports when the clamp bit.
        /// </summary>
        public static float HandleThickness(float thickness, float across, out bool clamped)
        {
            // thickness="0" is "no visible bar": no handle either, and nothing to warn about.
            if (thickness <= 0f) { clamped = false; return 0f; }
            var h = thickness - 2f * across;
            clamped = h <= 0f;
            return clamped ? 1f : h;
        }

        public static bool TryParseThickness(string value, out float result, out string error)
        {
            result = DefaultThickness;
            error = null;
            if (string.IsNullOrWhiteSpace(value)) return true;
            if (!TryNumber(value, out result))
            {
                result = DefaultThickness;
                error = $"{ThicknessName}=\"{value}\": expected a number of pixels (e.g. \"6\")";
                return false;
            }
            if (result < 0f)
            {
                result = DefaultThickness;
                error = $"{ThicknessName}=\"{value}\": must not be negative";
                return false;
            }
            return true;
        }

        /// <summary><paramref name="result"/> is null for "not declared" — the host then applies
        /// <see cref="DefaultSpacing"/>, which depends on the thickness.</summary>
        public static bool TryParseSpacing(string value, out float? result, out string error)
        {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(value)) return true;
            if (!TryNumber(value, out var v))
            {
                error = $"{SpacingName}=\"{value}\": expected a number of pixels, negative to overlap the viewport (e.g. \"6\", \"-3\")";
                return false;
            }
            result = v;
            return true;
        }

        /// <summary>
        /// <c>"P"</c> = all four sides, <c>"E,S"</c> = along the bar (the ends) then across it (the
        /// sides). Both non-negative.
        /// </summary>
        public static bool TryParsePadding(string value, out float along, out float across, out string error)
        {
            along = across = 0f;
            error = null;
            if (string.IsNullOrWhiteSpace(value)) return true;

            var parts = value.Split(',');
            if (parts.Length > 2)
            {
                error = $"{PaddingName}=\"{value}\": expected one number (all sides) or two (\"ends,sides\")";
                return false;
            }
            if (!TryNumber(parts[0], out along)
                || (parts.Length == 2 && !TryNumber(parts[1], out across)))
            {
                along = across = 0f;
                error = $"{PaddingName}=\"{value}\": expected numbers of pixels (e.g. \"1.5\" or \"4,1.5\")";
                return false;
            }
            if (parts.Length == 1) across = along;
            if (along < 0f || across < 0f)
            {
                along = across = 0f;
                error = $"{PaddingName}=\"{value}\": must not be negative";
                return false;
            }
            return true;
        }

        public static float ParseThickness(string value)
            => TryParseThickness(value, out var v, out var error) ? v : throw new ParseException(error);

        public static float? ParseSpacing(string value)
            => TryParseSpacing(value, out var v, out var error) ? v : throw new ParseException(error);

        public static (float Along, float Across) ParsePadding(string value)
            => TryParsePadding(value, out var along, out var across, out var error)
                ? (along, across)
                : throw new ParseException(error);

        private static bool TryNumber(string raw, out float result)
        {
            if (!float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result))
                return false;
            // InvariantCulture accepts "NaN" / "Infinity"; neither is a pixel count.
            if (float.IsNaN(result) || float.IsInfinity(result)) { result = 0f; return false; }
            return true;
        }
    }
}
