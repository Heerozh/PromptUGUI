using System;

namespace PromptUGUI.IR
{
    public enum AnchorVertical { Top, Center, Bottom, Stretch }
    public enum AnchorHorizontal { Left, Center, Right, Stretch }

    public readonly struct AnchorPreset : IEquatable<AnchorPreset>
    {
        public AnchorVertical V { get; }
        public AnchorHorizontal H { get; }

        public AnchorPreset(AnchorVertical v, AnchorHorizontal h) { V = v; H = h; }

        public bool StretchX => H == AnchorHorizontal.Stretch;
        public bool StretchY => V == AnchorVertical.Stretch;

        public static AnchorPreset Parse(string s)
        {
            if (string.IsNullOrEmpty(s))
                throw new ArgumentException("anchor cannot be empty");

            switch (s)
            {
                case "center": return new AnchorPreset(AnchorVertical.Center, AnchorHorizontal.Center);
                case "stretch":
                case "fill": return new AnchorPreset(AnchorVertical.Stretch, AnchorHorizontal.Stretch);
            }

            var dash = s.IndexOf('-');
            if (dash < 1 || dash == s.Length - 1)
                throw new ArgumentException(
                    $"anchor '{s}' is invalid; expected 'center', 'stretch' (alias 'fill'), or "
                    + "'<vertical>-<horizontal>' such as 'top-left' or 'bottom-stretch' "
                    + "(vertical: top|center|bottom|stretch, horizontal: left|center|right|stretch)");

            var v = ParseV(s.Substring(0, dash));
            var h = ParseH(s.Substring(dash + 1));
            return new AnchorPreset(v, h);
        }

        private static AnchorVertical ParseV(string s) => s switch
        {
            "top" => AnchorVertical.Top,
            "center" => AnchorVertical.Center,
            "bottom" => AnchorVertical.Bottom,
            "stretch" => AnchorVertical.Stretch,
            _ => throw new ArgumentException(
                $"anchor vertical '{s}' is invalid; expected 'top', 'center', 'bottom', or 'stretch'")
        };

        private static AnchorHorizontal ParseH(string s) => s switch
        {
            "left" => AnchorHorizontal.Left,
            "center" => AnchorHorizontal.Center,
            "right" => AnchorHorizontal.Right,
            "stretch" => AnchorHorizontal.Stretch,
            _ => throw new ArgumentException(
                $"anchor horizontal '{s}' is invalid; expected 'left', 'center', 'right', or 'stretch'")
        };

        public bool Equals(AnchorPreset o) => V == o.V && H == o.H;
        public override bool Equals(object o) => o is AnchorPreset p && Equals(p);
        public override int GetHashCode() => ((int)V * 4) + (int)H;

        public static bool operator ==(AnchorPreset left, AnchorPreset right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(AnchorPreset left, AnchorPreset right)
        {
            return !(left == right);
        }
    }
}
