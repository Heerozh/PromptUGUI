namespace PromptUGUI.Parser
{
    /// <summary>
    /// Where a gradient runs (spec 2026-09-17 §4.1), as the author wrote it: a CSS angle
    /// (<c>0deg</c> up, <c>90deg</c> right, <c>180deg</c> down — the default — <c>270deg</c> left),
    /// or a CSS "magic corner" (<c>to bottom right</c>), whose actual angle depends on the box's
    /// aspect ratio and is therefore resolved only where the box size is known
    /// (<c>ColorSpec.DirectionFor</c> / the shader). Pure C#: no vectors here, only the kind, the
    /// angle and the corner's signs, so the UIXmlLint CLI can compile it.
    /// </summary>
    internal readonly struct GradientDirection : System.IEquatable<GradientDirection>
    {
        public enum Kinds : byte { Angle = 0, Corner = 1 }

        public readonly Kinds Kind;
        /// <summary>Angle kind: degrees, normalized into [0, 360).</summary>
        public readonly float AngleDeg;
        /// <summary>Corner kind: −1 = left, +1 = right. 0 for the angle kind.</summary>
        public readonly sbyte CornerX;
        /// <summary>Corner kind: −1 = bottom, +1 = top. 0 for the angle kind.</summary>
        public readonly sbyte CornerY;

        private GradientDirection(Kinds kind, float angleDeg, sbyte cornerX, sbyte cornerY)
        {
            Kind = kind;
            AngleDeg = angleDeg;
            CornerX = cornerX;
            CornerY = cornerY;
        }

        /// <summary><c>to bottom</c> — the ramp every gradient ran before directions existed.</summary>
        public static readonly GradientDirection Default = Angle(180f);

        public static GradientDirection Angle(float degrees)
        {
            var d = degrees % 360f;
            if (d < 0f) d += 360f;
            if (d >= 360f) d -= 360f;      // −0 and float drift at exactly 360
            return new GradientDirection(Kinds.Angle, d, 0, 0);
        }

        /// <param name="x">−1 left, +1 right.</param>
        /// <param name="y">−1 bottom, +1 top.</param>
        public static GradientDirection Corner(int x, int y)
            => new GradientDirection(Kinds.Corner, 0f, (sbyte)(x < 0 ? -1 : 1), (sbyte)(y < 0 ? -1 : 1));

        public bool IsDefault => Kind == Kinds.Angle && AngleDeg == 180f;

        /// <summary>
        /// The corner in CSS <c>border-radius</c> order — 0 top-left, 1 top-right, 2 bottom-right,
        /// 3 bottom-left — which is how the shader receives it. −1 for the angle kind.
        /// </summary>
        public int CornerIndex
        {
            get
            {
                if (Kind != Kinds.Corner) return -1;
                if (CornerY > 0) return CornerX < 0 ? 0 : 1;
                return CornerX > 0 ? 2 : 3;
            }
        }

        public bool Equals(GradientDirection o)
            => Kind == o.Kind && AngleDeg == o.AngleDeg && CornerX == o.CornerX && CornerY == o.CornerY;

        public override bool Equals(object obj) => obj is GradientDirection o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                var h = (int)Kind;
                h = (h * 397) ^ AngleDeg.GetHashCode();
                h = (h * 397) ^ CornerX;
                h = (h * 397) ^ CornerY;
                return h;
            }
        }

        public static bool operator ==(GradientDirection a, GradientDirection b) => a.Equals(b);
        public static bool operator !=(GradientDirection a, GradientDirection b) => !a.Equals(b);

        public override string ToString()
            => Kind == Kinds.Angle
                ? $"{AngleDeg:0.###}deg"
                : $"to {(CornerY > 0 ? "top" : "bottom")} {(CornerX > 0 ? "right" : "left")}";
    }
}
