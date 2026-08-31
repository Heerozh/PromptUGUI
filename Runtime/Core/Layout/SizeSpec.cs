using System;
using System.Globalization;
using PromptUGUI.IR;
using UnityEngine;

namespace PromptUGUI.Layout
{
    public readonly struct SizeSpec
    {
        // Per-axis parse result. One struct instead of a fistful of out-params: the clamp form
        // parses its middle term by recursing into the same axis parser and then decorates the
        // result with bounds, which reads naturally as "take an Axis, return an Axis".
        private readonly struct Axis
        {
            public readonly bool Has;
            public readonly float Numeric;
            public readonly bool IsNative;
            public readonly bool IsFlexible;
            public readonly float Weight;
            public readonly bool IsFractional;
            public readonly float Fraction;
            public readonly bool IsClamped;
            public readonly float Min;
            public readonly float Max;
            public readonly bool IsHug;

            public Axis(bool has, float numeric, bool isNative, bool isFlexible, float weight,
                        bool isFractional, float fraction, bool isClamped, float min, float max,
                        bool isHug = false)
            {
                Has = has; Numeric = numeric; IsNative = isNative;
                IsFlexible = isFlexible; Weight = weight;
                IsFractional = isFractional; Fraction = fraction;
                IsClamped = isClamped; Min = min; Max = max;
                IsHug = isHug;
            }

            // Bounds default to the identity of Mathf.Clamp so consumers never branch on IsClamped
            // just to read them.
            public static readonly Axis None = new(false, 0f, false, false, 1f, false, 0f, false,
                float.NegativeInfinity, float.PositiveInfinity);

            public static Axis Fixed(float v) => new(true, v, false, false, 1f, false, 0f, false,
                float.NegativeInfinity, float.PositiveInfinity);

            public Axis WithNumeric(float v) => new(true, v, false, IsFlexible, Weight,
                IsFractional, Fraction, IsClamped, Min, Max, IsHug);

            public Axis WithBounds(float min, float max) => new(Has, Numeric, IsNative, IsFlexible, Weight,
                IsFractional, Fraction, true, min, max, IsHug);

            public static readonly Axis Hug = new(true, 0f, false, false, 1f, false, 0f, false,
                float.NegativeInfinity, float.PositiveInfinity, true);
        }

        private readonly Axis _w;
        private readonly Axis _h;

        public float Width => _w.Numeric;
        public float Height => _h.Numeric;
        public bool HasWidth => _w.Has;
        public bool HasHeight => _h.Has;
        public bool IsNativeWidth => _w.IsNative;
        public bool IsNativeHeight => _h.IsNative;

        // Flexible (LayoutGroup child): width="stretch" / "stretch*N" → LayoutElement.flexibleX = WeightX.
        public bool IsFlexibleWidth => _w.IsFlexible;
        public bool IsFlexibleHeight => _h.IsFlexible;
        public float WeightWidth => _w.Weight;
        public float WeightHeight => _h.Weight;

        // Fractional (free-positioning child): width="50%" → child occupies 50% of parent on that axis,
        // positioned by anchor= preset (left/center/right or top/center/bottom).
        public bool IsFractionalWidth => _w.IsFractional;
        public bool IsFractionalHeight => _h.IsFractional;
        public float WidthFraction => _w.Fraction;
        public float HeightFraction => _h.Fraction;

        // Clamped: width="clamp(min, N%, max)" (free-positioning, IsFractional stays set) or
        // width="clamp(min, stretch, max)" (LayoutGroup child, IsFlexible stays set). The middle term
        // keeps its own flags; clamp only adds the bounds. An open bound ('_') is ±Infinity so
        // Mathf.Clamp(v, Min, Max) is the identity on that side.
        // Hug (content-fit): width="hug" → the control sizes itself to its own content on that axis.
        // Only meaningful on the tags that HAVE a content size (VStack / HStack / Grid / ScrollList /
        // Collapsible — HugRules.HugTags); everywhere else it is a lint/parse error. Combines with
        // clamp: clamp(min, hug, max) keeps IsHug set and only adds the bounds, exactly like % and
        // stretch do. Spec 2026-08-31-hug-reveal-flip-checked-design §1.
        public bool IsHugWidth => _w.IsHug;
        public bool IsHugHeight => _h.IsHug;

        public bool IsClampedWidth => _w.IsClamped;
        public bool IsClampedHeight => _h.IsClamped;
        public float MinWidth => _w.Min;
        public float MaxWidth => _w.Max;
        public float MinHeight => _h.Min;
        public float MaxHeight => _h.Max;

        private SizeSpec(Axis w, Axis h)
        {
            _w = w;
            _h = h;
        }

        public static SizeSpec Parse(string size, string width, string height)
        {
            var w = Axis.None;
            var h = Axis.None;

            if (!string.IsNullOrEmpty(size))
            {
                if (size == "native")
                {
                    w = new Axis(true, 0f, true, false, 1f, false, 0f, false,
                        float.NegativeInfinity, float.PositiveInfinity);
                    h = w;
                }
                else
                {
                    // size= stays purely numeric WxH. Keyword forms ('stretch', 'N%', 'hug', 'clamp(...)')
                    // belong on per-axis width=/height= attrs so the reading "W by H" stays unambiguous.
                    if (LooksLikeKeyword(size))
                        throw new ArgumentException(
                            $"size '{size}' is numeric-only ('WxH' or 'native'). For 'stretch' / '%' / 'hug' / " +
                            "'clamp(...)', use per-axis attrs: width=\"stretch\" / height=\"50%\" / height=\"hug\" / " +
                            "width=\"clamp(167, 46%, 250)\" etc.");
                    var x = size.IndexOf('x');
                    if (x <= 0 || x == size.Length - 1)
                        throw new ArgumentException($"size '{size}' must be 'WxH' or 'native'");
                    w = Axis.Fixed(ParseFloat(size.Substring(0, x), $"size '{size}' width"));
                    h = Axis.Fixed(ParseFloat(size.Substring(x + 1), $"size '{size}' height"));
                }
            }

            if (!string.IsNullOrEmpty(width))
            {
                if (w.Has) throw new ArgumentException("cannot specify both size and width");
                w = ParseAxis(width, "width");
            }

            if (!string.IsNullOrEmpty(height))
            {
                if (h.Has) throw new ArgumentException("cannot specify both size and height");
                h = ParseAxis(height, "height");
            }

            return new SizeSpec(w, h);
        }

        public SizeSpec WithNativeResolved(Vector2 native) =>
            new(
                _w.IsNative ? _w.WithNumeric(native.x) : _w,
                _h.IsNative ? _h.WithNumeric(native.y) : _h);

        internal static SizeSpec FromNumeric(float w, float h) =>
            new(Axis.Fixed(w), Axis.Fixed(h));

        /// <summary>
        /// Replaces the vertical axis with a bare <c>hug</c>. For the one control whose height is
        /// not the author's to give (<c>&lt;Collapsible&gt;</c> — see
        /// <c>Control.ForcesHugHeight</c>); anything the author wrote on that axis is a lint /
        /// runtime error of its own, so overwriting it here is not hiding a legal value.
        /// </summary>
        internal SizeSpec WithHugHeight() => new(_w, Axis.Hug);

        internal SizeSpec WithFallbackForMissing(Vector2 native) =>
            new(
                _w.Has ? _w : Axis.Fixed(native.x),
                _h.Has ? _h : Axis.Fixed(native.y));

        private static bool LooksLikeKeyword(string s)
        {
            // Heuristic for the size= validator: catch 'stretch', 'stretch*N', 'N%', 'NxN%', 'clamp(...)',
            // 'hug' early so the error message points at the keyword rule, not at "x is not a number".
            return s.Contains("stretch") || s.Contains("%") || s.Contains("clamp") || s.Contains("hug");
        }

        private static Axis ParseAxis(string value, string label)
        {
            if (value == "native")
                return new Axis(true, 0f, true, false, 1f, false, 0f, false,
                    float.NegativeInfinity, float.PositiveInfinity);

            if (value == "stretch")
                return new Axis(true, 0f, false, true, 1f, false, 0f, false,
                    float.NegativeInfinity, float.PositiveInfinity);

            // 'hug' carries no number of its own — the size comes from the control's content at
            // layout time (ClampFitter's Hug mode / HugElement). Numeric stays 0 so a consumer that
            // ignores the flag lands on "nothing authored" rather than a bogus size.
            if (value == "hug")
                return new Axis(true, 0f, false, false, 1f, false, 0f, false,
                    float.NegativeInfinity, float.PositiveInfinity, isHug: true);

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
                return new Axis(true, 0f, false, true, wt, false, 0f, false,
                    float.NegativeInfinity, float.PositiveInfinity);
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
                return new Axis(true, 0f, false, false, 1f, true, pct / 100f, false,
                    float.NegativeInfinity, float.PositiveInfinity);
            }

            if (value.StartsWith("clamp(", StringComparison.Ordinal))
                return ParseClamp(value, label);

            // 'Clamp(', 'clamp 167 46% 250', 'clamp[...]' — the author meant the function form; say so
            // instead of "'clamp' is not a number".
            if (value.StartsWith("clamp", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"{label} '{value}': unknown value — the only function form is clamp(min, middle, max), " +
                    "lowercase, e.g. 'clamp(167, 46%, 250)'");

            return Axis.Fixed(ParseFloat(value, label));
        }

        // clamp(min, middle, max) — middle is 'N%' (free-positioning) or 'stretch' / 'stretch*N'
        // (LayoutGroup child); '_' opens a bound. Spec 2026-08-30-clamp-size-design §4.
        private static Axis ParseClamp(string value, string label)
        {
            if (!value.EndsWith(")", StringComparison.Ordinal))
                throw new ArgumentException(
                    $"{label} '{value}': missing ')' — write clamp(min, middle, max)");

            var inner = value.Substring("clamp(".Length, value.Length - "clamp(".Length - 1);
            var parts = inner.Split(',');
            if (parts.Length != 3)
                throw new ArgumentException(
                    $"{label} '{value}': clamp takes exactly 3 parts clamp(min, middle, max), got {parts.Length}");

            var min = ParseBound(parts[0].Trim(), value, label, isMin: true);
            var max = ParseBound(parts[2].Trim(), value, label, isMin: false);

            var middle = parts[1].Trim();
            var mid = middle.Length == 0 ? Axis.None : ParseAxis(middle, label);
            if (!mid.IsFractional && !mid.IsFlexible && !mid.IsHug || mid.IsClamped)
                throw new ArgumentException(
                    $"{label} '{value}': middle must be 'N%' (free-positioning), 'stretch' (inside <VStack>/<HStack>) " +
                    "or 'hug' (content-fit) — a constant needs no clamp");

            if (mid.IsFlexible && mid.Weight != 1f && !float.IsPositiveInfinity(max))
                throw new ArgumentException(
                    $"{label} '{value}': a weighted stretch cannot be capped (flexible > 0 grows past preferred) — " +
                    "drop the weight or open the max with '_'");

            if (float.IsNegativeInfinity(min) && float.IsPositiveInfinity(max))
                throw new ArgumentException(
                    $"{label} '{value}': both bounds open — write '{middle}' instead");

            if (min > max)
                throw new ArgumentException(
                    $"{label} '{value}': min {min.ToString(CultureInfo.InvariantCulture)} > max " +
                    $"{max.ToString(CultureInfo.InvariantCulture)}");

            return mid.WithBounds(min, max);
        }

        private static float ParseBound(string s, string whole, string label, bool isMin)
        {
            if (s == "_") return isMin ? float.NegativeInfinity : float.PositiveInfinity;
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                || float.IsNaN(v) || float.IsInfinity(v) || v < 0f)
                throw new ArgumentException(
                    $"{label} '{whole}': {(isMin ? "min" : "max")} '{s}' — bounds must be finite and >= 0 " +
                    "(or '_' for no bound)");
            return v;
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
