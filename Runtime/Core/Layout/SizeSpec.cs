using System;
using System.Globalization;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Layout
{
    public readonly struct SizeSpec
    {
        public float Width { get; }
        public float Height { get; }
        public bool HasWidth { get; }
        public bool HasHeight { get; }
        public bool IsNativeWidth { get; }
        public bool IsNativeHeight { get; }

        // Flexible (LayoutGroup child): width="stretch" / "stretch*N" → LayoutElement.flexibleX = WeightX.
        public bool IsFlexibleWidth { get; }
        public bool IsFlexibleHeight { get; }
        public float WeightWidth { get; }
        public float WeightHeight { get; }

        // Fractional (free-positioning child): width="50%" → child occupies 50% of parent on that axis,
        // positioned by anchor= preset (left/center/right or top/center/bottom).
        public bool IsFractionalWidth { get; }
        public bool IsFractionalHeight { get; }
        public float WidthFraction { get; }
        public float HeightFraction { get; }

        private SizeSpec(
            float w, float h, bool hw, bool hh, bool nw, bool nh,
            bool fw, bool fh, float ww, float wh,
            bool prw, bool prh, float pw, float ph)
        {
            Width = w; Height = h;
            HasWidth = hw; HasHeight = hh;
            IsNativeWidth = nw; IsNativeHeight = nh;
            IsFlexibleWidth = fw; IsFlexibleHeight = fh;
            WeightWidth = ww; WeightHeight = wh;
            IsFractionalWidth = prw; IsFractionalHeight = prh;
            WidthFraction = pw; HeightFraction = ph;
        }

        public static SizeSpec Parse(string size, string width, string height)
        {
            float w = 0f, h = 0f;
            bool hw = false, hh = false;
            bool nw = false, nh = false;
            bool fw = false, fh = false;
            float ww = 1f, wh = 1f;
            bool prw = false, prh = false;
            float pw = 0f, ph = 0f;

            if (!string.IsNullOrEmpty(size))
            {
                if (size == "native")
                {
                    hw = hh = true;
                    nw = nh = true;
                }
                else
                {
                    // size= stays purely numeric WxH. Keyword forms ('stretch', 'N%') belong on
                    // per-axis width=/height= attrs so the reading "W by H" stays unambiguous.
                    if (LooksLikeKeyword(size))
                        throw new ArgumentException(
                            $"size '{size}' is numeric-only ('WxH' or 'native'). For 'stretch' / '%', " +
                            "use per-axis attrs: width=\"stretch\" / height=\"50%\" etc.");
                    var x = size.IndexOf('x');
                    if (x <= 0 || x == size.Length - 1)
                        throw new ArgumentException($"size '{size}' must be 'WxH' or 'native'");
                    w = ParseFloat(size.Substring(0, x), $"size '{size}' width");
                    h = ParseFloat(size.Substring(x + 1), $"size '{size}' height");
                    hw = hh = true;
                }
            }

            if (!string.IsNullOrEmpty(width))
            {
                if (hw) throw new ArgumentException("cannot specify both size and width");
                ParseAxis(width, "width", out var axisW, out var axisN, out var axisF, out var axisWt,
                    out var axisPr, out var axisFrac);
                w = axisW; nw = axisN; fw = axisF; ww = axisWt; prw = axisPr; pw = axisFrac;
                hw = true;
            }

            if (!string.IsNullOrEmpty(height))
            {
                if (hh) throw new ArgumentException("cannot specify both size and height");
                ParseAxis(height, "height", out var axisH, out var axisN, out var axisF, out var axisWt,
                    out var axisPr, out var axisFrac);
                h = axisH; nh = axisN; fh = axisF; wh = axisWt; prh = axisPr; ph = axisFrac;
                hh = true;
            }

            return new SizeSpec(w, h, hw, hh, nw, nh, fw, fh, ww, wh, prw, prh, pw, ph);
        }

        public SizeSpec WithNativeResolved(Vector2 native) =>
            new(
                IsNativeWidth ? native.x : Width,
                IsNativeHeight ? native.y : Height,
                HasWidth, HasHeight,
                false, false,
                IsFlexibleWidth, IsFlexibleHeight,
                WeightWidth, WeightHeight,
                IsFractionalWidth, IsFractionalHeight,
                WidthFraction, HeightFraction);

        private static bool LooksLikeKeyword(string s)
        {
            // Heuristic for the size= validator: catch 'stretch', 'stretch*N', 'N%', 'NxN%' early
            // so the error message points at the keyword rule, not at "x is not a number".
            return s.Contains("stretch") || s.Contains("%");
        }

        private static void ParseAxis(
            string value, string label,
            out float numeric, out bool isNative, out bool isFlexible, out float weight,
            out bool isFractional, out float fraction)
        {
            numeric = 0f;
            isNative = false;
            isFlexible = false;
            weight = 1f;
            isFractional = false;
            fraction = 0f;

            if (value == "native") { isNative = true; return; }

            if (value == "stretch") { isFlexible = true; return; }

            if (value.StartsWith("stretch*", StringComparison.Ordinal))
            {
                var tail = value.Substring("stretch*".Length);
                if (tail.Length == 0)
                    throw new ArgumentException(
                        $"{label} 'stretch*' must include a positive weight, e.g. 'stretch*2'");
                if (!float.TryParse(tail, NumberStyles.Float, CultureInfo.InvariantCulture, out var wt))
                    throw new ArgumentException(
                        $"{label} 'stretch*{tail}': '{tail}' is not a number");
                if (!(wt > 0f) || float.IsInfinity(wt))
                    throw new ArgumentException(
                        $"{label} 'stretch*{tail}': weight must be > 0");
                isFlexible = true;
                weight = wt;
                return;
            }

            if (value.EndsWith("%", StringComparison.Ordinal))
            {
                var head = value.Substring(0, value.Length - 1);
                if (head.Length == 0)
                    throw new ArgumentException(
                        $"{label} '%' needs a number, e.g. '50%'");
                if (!float.TryParse(head, NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                    throw new ArgumentException(
                        $"{label} '{value}': '{head}' is not a number");
                if (!(pct > 0f) || pct > 100f)
                    throw new ArgumentException(
                        $"{label} '{value}': must be in (0%, 100%]");
                isFractional = true;
                fraction = pct / 100f;
                return;
            }

            numeric = ParseFloat(value, label);
        }

        private static float ParseFloat(string s, string label)
        {
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new ArgumentException($"{label}: '{s}' is not a number");
            return v;
        }

        public void ValidateAgainst(AnchorPreset anchor)
        {
            if (anchor.StretchX && HasWidth)
                throw new ArgumentException(
                    "cannot specify width/size on a horizontally-stretched axis");
            if (anchor.StretchY && HasHeight)
                throw new ArgumentException(
                    "cannot specify height/size on a vertically-stretched axis");
        }
    }
}
