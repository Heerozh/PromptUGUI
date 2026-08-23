using System.Globalization;

namespace PromptUGUI.Parser
{
    /// <summary>
    /// Corner-radius spec produced by <see cref="RadiusParser"/>. Four independent corners in CSS
    /// <c>border-radius</c> order (clockwise from top-left) plus a <c>pill</c> sentinel.
    /// </summary>
    /// <remarks>
    /// <see cref="IsPill"/> is deliberately NOT resolved to a number here: the pill radius is
    /// <c>min(width, height) / 2</c>, which depends on the live rect. Resolving it in C# would make
    /// two same-styled panels of different sizes carry different values and lose material sharing
    /// (see <c>ProceduralMaterialCache</c>) — the shader resolves it per-fragment from its own size
    /// input instead, for free.
    /// </remarks>
    public readonly struct RadiusSpec
    {
        public readonly float TopLeft;
        public readonly float TopRight;
        public readonly float BottomRight;
        public readonly float BottomLeft;
        public readonly bool IsPill;

        public RadiusSpec(float tl, float tr, float br, float bl, bool isPill = false)
        {
            TopLeft = tl; TopRight = tr; BottomRight = br; BottomLeft = bl; IsPill = isPill;
        }

        public static readonly RadiusSpec Zero = new RadiusSpec(0f, 0f, 0f, 0f);
        public static readonly RadiusSpec Pill = new RadiusSpec(0f, 0f, 0f, 0f, true);

        public bool IsZero => !IsPill && TopLeft == 0f && TopRight == 0f
                              && BottomRight == 0f && BottomLeft == 0f;
    }

    /// <summary>
    /// Parses the <c>radius</c> attribute. Pure C# (no UnityEngine types) so the UIXmlLint CLI
    /// compiles it and surfaces syntax errors before the author ever opens Unity.
    /// </summary>
    public static class RadiusParser
    {
        public const string PillKeyword = "pill";

        /// <summary>Throwing wrapper used by the runtime attribute setters.</summary>
        public static RadiusSpec Parse(string value)
            => TryParse(value, out var spec, out var error) ? spec : throw new ParseException(error);

        /// <summary>
        /// Null / empty parses to <see cref="RadiusSpec.Zero"/> (square corners) rather than an
        /// error — a Variant can only override an attribute's value, never remove it, so
        /// <c>radius.desktop=""</c> is the only way back to square and must stay legal.
        /// </summary>
        public static bool TryParse(string value, out RadiusSpec spec, out string error)
        {
            spec = RadiusSpec.Zero;
            error = null;

            if (string.IsNullOrWhiteSpace(value)) return true;

            var raw = value.Trim();
            var parts = raw.Split(',');

            if (parts.Length == 1 && string.Equals(parts[0].Trim(), PillKeyword,
                                                   System.StringComparison.Ordinal))
            {
                spec = RadiusSpec.Pill;
                return true;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                if (!string.Equals(parts[i].Trim(), PillKeyword, System.StringComparison.Ordinal))
                    continue;
                error = $"radius=\"{raw}\": '{PillKeyword}' is a whole-shape keyword and cannot be " +
                        "mixed with per-corner values (write radius=\"pill\" on its own)";
                return false;
            }

            if (parts.Length != 1 && parts.Length != 4)
            {
                error = $"radius=\"{raw}\": expected 1 value (all corners), 4 values " +
                        "(top-left,top-right,bottom-right,bottom-left) or the keyword " +
                        $"'{PillKeyword}' — got {parts.Length} comma-separated values";
                return false;
            }

            if (parts.Length == 1)
            {
                if (!TryParseCorner(parts[0], raw, "value", out var all, out error)) return false;
                spec = new RadiusSpec(all, all, all, all);
                return true;
            }

            if (!TryParseCorner(parts[0], raw, "top-left", out var tl, out error)) return false;
            if (!TryParseCorner(parts[1], raw, "top-right", out var tr, out error)) return false;
            if (!TryParseCorner(parts[2], raw, "bottom-right", out var br, out error)) return false;
            if (!TryParseCorner(parts[3], raw, "bottom-left", out var bl, out error)) return false;

            spec = new RadiusSpec(tl, tr, br, bl);
            return true;
        }

        private static bool TryParseCorner(string segment, string raw, string corner,
                                           out float result, out string error)
        {
            result = 0f;
            error = null;
            var s = segment.Trim();

            if (s.Length == 0)
            {
                error = $"radius=\"{raw}\": {corner} segment is empty";
                return false;
            }
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                error = $"radius=\"{raw}\": {corner} segment '{s}' is not a number";
                return false;
            }
            // "NaN" / "Infinity" parse fine under InvariantCulture, and NaN also slips past the
            // negative test below (every comparison with NaN is false).
            if (float.IsNaN(result) || float.IsInfinity(result))
            {
                result = 0f;
                error = $"radius=\"{raw}\": {corner} segment '{s}' is not a finite number";
                return false;
            }
            if (result < 0f)
            {
                error = $"radius=\"{raw}\": {corner} segment '{s}' is negative";
                return false;
            }
            return true;
        }
    }
}
