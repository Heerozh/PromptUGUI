using System;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Layout {
    public readonly struct SizeSpec {
        public float Width  { get; }
        public float Height { get; }
        public bool  HasWidth  { get; }
        public bool  HasHeight { get; }

        SizeSpec(float w, float h, bool hw, bool hh) {
            Width = w; Height = h; HasWidth = hw; HasHeight = hh;
        }

        public static SizeSpec Parse(string size, string width, string height) {
            float w = 0f, h = 0f;
            bool hw = false, hh = false;

            if (!string.IsNullOrEmpty(size)) {
                var x = size.IndexOf('x');
                if (x <= 0 || x == size.Length - 1)
                    throw new ArgumentException($"size '{size}' must be 'WxH'");
                w = ParseFloat(size.Substring(0, x), $"size '{size}' width");
                h = ParseFloat(size.Substring(x + 1), $"size '{size}' height");
                hw = hh = true;
            }

            if (!string.IsNullOrEmpty(width)) {
                if (hw) throw new ArgumentException("cannot specify both size and width");
                w = ParseFloat(width, "width");
                hw = true;
            }

            if (!string.IsNullOrEmpty(height)) {
                if (hh) throw new ArgumentException("cannot specify both size and height");
                h = ParseFloat(height, "height");
                hh = true;
            }

            return new SizeSpec(w, h, hw, hh);
        }

        static float ParseFloat(string s, string label) {
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                throw new ArgumentException($"{label}: '{s}' is not a number");
            return v;
        }

        public void ValidateAgainst(AnchorPreset anchor) {
            if (anchor.StretchX && HasWidth)
                throw new ArgumentException(
                    "cannot specify width/size on a horizontally-stretched axis");
            if (anchor.StretchY && HasHeight)
                throw new ArgumentException(
                    "cannot specify height/size on a vertically-stretched axis");
        }
    }
}
