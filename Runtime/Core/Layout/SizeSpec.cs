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
        public bool IsFlexibleWidth { get; }
        public bool IsFlexibleHeight { get; }

        private SizeSpec(float w, float h, bool hw, bool hh, bool nw, bool nh, bool fw, bool fh)
        {
            Width = w; Height = h;
            HasWidth = hw; HasHeight = hh;
            IsNativeWidth = nw; IsNativeHeight = nh;
            IsFlexibleWidth = fw; IsFlexibleHeight = fh;
        }

        public static SizeSpec Parse(string size, string width, string height)
        {
            float w = 0f, h = 0f;
            bool hw = false, hh = false;
            bool nw = false, nh = false;
            bool fw = false, fh = false;

            if (!string.IsNullOrEmpty(size))
            {
                if (size == "native")
                {
                    hw = hh = true;
                    nw = nh = true;
                }
                else
                {
                    // 'stretch' is intentionally only valid on single-axis width=/height= attrs.
                    // The compact size=WxH form stays purely numeric so it reads as a literal
                    // "W by H" dimension. Anyone wanting flex must use the per-axis attrs.
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
                if (width == "native") { nw = true; }
                else if (width == "stretch") { fw = true; }
                else { w = ParseFloat(width, "width"); }
                hw = true;
            }

            if (!string.IsNullOrEmpty(height))
            {
                if (hh) throw new ArgumentException("cannot specify both size and height");
                if (height == "native") { nh = true; }
                else if (height == "stretch") { fh = true; }
                else { h = ParseFloat(height, "height"); }
                hh = true;
            }

            return new SizeSpec(w, h, hw, hh, nw, nh, fw, fh);
        }

        public SizeSpec WithNativeResolved(Vector2 native) =>
            new(
                IsNativeWidth ? native.x : Width,
                IsNativeHeight ? native.y : Height,
                HasWidth, HasHeight,
                false, false,
                IsFlexibleWidth, IsFlexibleHeight);

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
