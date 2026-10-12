using System;
using System.Globalization;

namespace PromptUGUI.Parser
{
    /// <summary>
    /// The three forms of a node's <c>scale</c> attribute and the <c>RectTransform.localScale</c>
    /// each one yields for a given canvas factor (physical pixels per canvas unit). Pure C# so the
    /// runtime (<c>Screen.ApplyScaleToNode</c>) and the font-floor lint share one formula.
    ///
    /// <list type="bullet">
    /// <item><c>"N"</c> (positive float) — a plain multiplier, independent of the factor.</item>
    /// <item><c>"Nx"</c> (positive integer) — device density: <c>N / factor</c>, so the content
    /// renders at exactly N physical pixels per design unit.</item>
    /// <item><c>"&lt;r&gt;r"</c> (positive float) — canvas-relative, snapped:
    /// <c>max(1, round(factor·r)) / factor</c>, an integer number of physical pixels per unit that
    /// still grows with the factor.</item>
    /// </list>
    /// Anything else (unset, non-numeric such as an <c>&lt;Animation scale="1:0.5"&gt;</c>) is
    /// identity. A non-positive factor counts as 1.
    /// </summary>
    public static class ScaleSyntax
    {
        /// <summary><c>"Nx"</c>, N a positive integer.</summary>
        public static bool TryParseDevice(string raw, out int n)
        {
            n = 0;
            if (string.IsNullOrEmpty(raw) || raw.Length < 2 || raw[raw.Length - 1] != 'x') return false;
            return int.TryParse(raw.Substring(0, raw.Length - 1), NumberStyles.None,
                       CultureInfo.InvariantCulture, out n) && n >= 1;
        }

        /// <summary><c>"&lt;r&gt;r"</c>, r a positive float.</summary>
        public static bool TryParseRelative(string raw, out float r)
        {
            r = 0f;
            if (string.IsNullOrEmpty(raw) || raw.Length < 2 || raw[raw.Length - 1] != 'r') return false;
            return float.TryParse(raw.Substring(0, raw.Length - 1), NumberStyles.Float,
                       CultureInfo.InvariantCulture, out r) && r > 0f;
        }

        /// <summary>A plain positive multiplier.</summary>
        public static bool TryParseMultiplier(string raw, out float v)
        {
            v = 0f;
            return !string.IsNullOrEmpty(raw)
                   && float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out v)
                   && v > 0f;
        }

        /// <summary>Whether the value's localScale depends on the canvas factor (<c>Nx</c> / <c>&lt;r&gt;r</c>).</summary>
        public static bool IsFactorDependent(string raw)
            => TryParseDevice(raw, out _) || TryParseRelative(raw, out _);

        /// <summary><c>"Nx"</c>: <c>N / factor</c>.</summary>
        public static float DeviceLocalScale(int n, float canvasFactor) => n / Positive(canvasFactor);

        /// <summary><c>"&lt;r&gt;r"</c>: <c>max(1, round-half-up(factor·r)) / factor</c>.</summary>
        public static float RelativeLocalScale(float r, float canvasFactor)
        {
            var f = Positive(canvasFactor);
            var effective = Math.Max(1f, (float)Math.Floor(f * r + 0.5f));
            return effective / f;
        }

        /// <summary>The localScale <paramref name="raw"/> yields under <paramref name="canvasFactor"/>; 1 when it is not a scale.</summary>
        public static float LocalScale(string raw, float canvasFactor)
        {
            if (TryParseDevice(raw, out var n)) return DeviceLocalScale(n, canvasFactor);
            if (TryParseRelative(raw, out var r)) return RelativeLocalScale(r, canvasFactor);
            return TryParseMultiplier(raw, out var v) ? v : 1f;
        }

        private static float Positive(float factor) => factor > 0f ? factor : 1f;
    }
}
