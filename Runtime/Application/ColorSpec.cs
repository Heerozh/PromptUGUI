using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// A resolved colour value: a solid, or a linear gradient of two to four stops along a
    /// direction (spec 2026-09-17). Produced by <c>UI.Theme.ResolveSpec</c>; drawn per fragment by
    /// the SDF shaders (<c>PuguiGradient</c>), per vertex by <c>ColorApplier</c> / <c>GradientTint</c>,
    /// and per glyph corner by the TMP path in <c>Text</c>.
    ///
    /// <para>Fixed four slots rather than an array: this struct is part of the procedural material
    /// cache key, so it needs value semantics and no allocation. Unused slots repeat the last stop,
    /// so <see cref="End"/> and the shader's fourth slot read correctly without checking
    /// <see cref="Count"/>. A solid keeps every slot equal to its one colour.</para>
    /// </summary>
    internal readonly struct ColorSpec : System.IEquatable<ColorSpec>
    {
        public readonly Color C0, C1, C2, C3;
        /// <summary>Stop positions, 0..1 along the gradient line (0 = where the line starts).</summary>
        public readonly float P0, P1, P2, P3;
        /// <summary>
        /// The power each segment's normalized ramp is raised to, from a CSS colour hint. <c>1</c> is
        /// the plain linear ramp. A hint bends the segment instead of cutting it, which is the
        /// difference between "mostly blue, gold creeping in at the end" and a visible dividing
        /// line: a moved stop leaves a slope discontinuity, and the eye reads that as an edge
        /// (spec 2026-08-30 §14).
        /// </summary>
        public readonly float E0, E1, E2;
        /// <summary>Number of stops: 1 for a solid, 2..4 for a gradient.</summary>
        public readonly byte Count;
        public readonly GradientDirection Direction;

        private ColorSpec(Color c0, Color c1, Color c2, Color c3,
                          float p0, float p1, float p2, float p3,
                          float e0, float e1, float e2,
                          byte count, GradientDirection direction)
        {
            C0 = c0; C1 = c1; C2 = c2; C3 = c3;
            P0 = p0; P1 = p1; P2 = p2; P3 = p3;
            E0 = e0; E1 = e1; E2 = e2;
            Count = count;
            Direction = direction;
        }

        // ---- construction ----

        public static ColorSpec Solid(Color c)
            => new ColorSpec(c, c, c, c, 0f, 1f, 1f, 1f, 1f, 1f, 1f, 1, GradientDirection.Default);

        /// <summary>Two stops, the full-length linear ramp, default direction (<c>to bottom</c>).</summary>
        public static ColorSpec Gradient(Color start, Color end) => Gradient(start, end, 0f, 1f, 1f);

        public static ColorSpec Gradient(Color start, Color end, float startStop, float endStop)
            => Gradient(start, end, startStop, endStop, 1f);

        public static ColorSpec Gradient(Color start, Color end, float startStop, float endStop, float curve)
            => new ColorSpec(start, end, end, end, startStop, endStop, endStop, endStop, curve, 1f, 1f, 2,
                             GradientDirection.Default);

        /// <summary>
        /// The general form: 2..4 stops with their positions and the curve of each segment
        /// (<paramref name="curves"/> has one fewer entry than <paramref name="colours"/>).
        /// </summary>
        public static ColorSpec Gradient(GradientDirection direction, Color[] colours, float[] stops, float[] curves)
        {
            var n = colours.Length;
            if (n < 2 || n > ColorParser.MaxStops)
                throw new System.ArgumentException($"a gradient has 2 to {ColorParser.MaxStops} stops, got {n}");
            if (stops.Length != n || curves.Length != n - 1)
                throw new System.ArgumentException("stops must match colours, curves must be one fewer");

            var last = colours[n - 1];
            var lastStop = stops[n - 1];
            return new ColorSpec(
                colours[0],
                colours[1],
                n > 2 ? colours[2] : last,
                n > 3 ? colours[3] : last,
                stops[0],
                stops[1],
                n > 2 ? stops[2] : lastStop,
                n > 3 ? stops[3] : lastStop,
                curves[0],
                n > 2 ? curves[1] : 1f,
                n > 3 ? curves[2] : 1f,
                (byte)n, direction);
        }

        public ColorSpec WithDirection(GradientDirection direction)
            => new ColorSpec(C0, C1, C2, C3, P0, P1, P2, P3, E0, E1, E2, Count, direction);

        // ---- reading ----

        public bool IsGradient => Count > 1;
        /// <summary>The first stop's colour — the one at the start of the gradient line.</summary>
        public Color Start => C0;
        /// <summary>The last stop's colour (unused slots repeat it, so this is always <see cref="C3"/>).</summary>
        public Color End => C3;
        public float StartStop => P0;
        public float EndStop => P3;

        public Color ColorAt(int i)
        {
            switch (i)
            {
                case 0: return C0;
                case 1: return C1;
                case 2: return C2;
                default: return C3;
            }
        }

        public float StopAt(int i)
        {
            switch (i)
            {
                case 0: return P0;
                case 1: return P1;
                case 2: return P2;
                default: return P3;
            }
        }

        /// <summary>Curve exponent of segment <paramref name="i"/> (between stop i and i+1).</summary>
        public float CurveAt(int i)
        {
            switch (i)
            {
                case 0: return E0;
                case 1: return E1;
                default: return E2;
            }
        }

        /// <summary>
        /// The author shaped the ramp — a third colour, a moved stop, or a hint. Both the procedural
        /// shader (per fragment) and <c>GradientTint</c> (by slicing the mesh at the stops, spec
        /// 2026-09-01 VGS §4.2) can draw that. TMP text cannot: it paints a gradient per glyph, and
        /// four glyph corners can hold any two-colour linear ramp in any direction but nothing
        /// else — that path warns instead of lying.
        /// </summary>
        public bool HasStops => IsGradient && (Count > 2 || P0 != 0f || P3 != 1f || E0 != 1f);

        /// <summary>Some stop would paint something.</summary>
        public bool AnyVisible => C0.a > 0f || C1.a > 0f || C2.a > 0f || C3.a > 0f;

        // ---- geometry (spec §4.1, CSS linear-gradient) ----

        /// <summary>
        /// The unit direction the gradient runs in, local space, y up. An angle is the CSS
        /// <c>(sin θ, cos θ)</c>: 0deg up, 90deg right, 180deg down. A corner needs the box: the line
        /// is perpendicular to the diagonal through the two NEIGHBOURING corners, so that on any
        /// aspect ratio the named corner is 0%, the opposite one 100%, and the other two sit exactly
        /// on the 50% line.
        /// </summary>
        public Vector2 DirectionFor(Vector2 size)
        {
            if (Direction.Kind == GradientDirection.Kinds.Angle) return AngleVector(Direction.AngleDeg);
            // "to bottom right": neighbours are top-right and bottom-left, joined by (w, h);
            // the perpendicular pointing into the bottom-right quadrant is (h, −w). The other
            // corners follow by sign.
            var v = new Vector2(size.y * Direction.CornerX, size.x * Direction.CornerY);
            return v.sqrMagnitude > 1e-8f ? v.normalized : Vector2.down;
        }

        /// <summary>
        /// CSS gradient-line length: long enough that 0% and 100% touch the two corners of the box
        /// the line points between. <c>|w·dir.x| + |h·dir.y|</c>.
        /// </summary>
        public float LineLengthFor(Vector2 size, Vector2 dir)
            => Mathf.Abs(size.x * dir.x) + Mathf.Abs(size.y * dir.y);

        /// <summary>
        /// CSS <c>(sin θ, cos θ)</c>, with the four axis angles snapped to exact unit vectors: <c>sin(180°)</c>
        /// is −8.7e-8 in float, and that hair would put the default direction a colour step off the ramp
        /// every panel drew before directions existed. Shared with <c>GradientUniforms</c> so the shader
        /// and the vertex path agree bit for bit.
        /// </summary>
        internal static Vector2 AngleVector(float degrees)
        {
            switch (degrees)
            {
                case 0f: return Vector2.up;
                case 90f: return Vector2.right;
                case 180f: return Vector2.down;
                case 270f: return Vector2.left;
            }
            var rad = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(rad), Mathf.Cos(rad));
        }

        // ---- evaluation (spec §4.2) ----

        /// <summary>
        /// The colour at normalized distance <paramref name="s"/> along the gradient line (0 =
        /// start, 1 = end) — the same ramp <c>PuguiGradient</c> (UI-PanelSDF.cginc) draws per
        /// fragment, evaluated per vertex. Keep the two in step: a &lt;Frame&gt; and an &lt;Image&gt;
        /// carrying the same token have to change over at the same row of pixels.
        /// </summary>
        public Color Evaluate(float s)
        {
            if (Count < 2) return C0;
            if (s <= P0) return C0;
            if (Count > 2 && s > P1)
            {
                if (Count > 3 && s > P2)
                    return Segment(C2, C3, P2, P3, E2, s);
                return Segment(C1, C2, P1, P2, E1, s);
            }
            return Segment(C0, C1, P0, P1, E0, s);
        }

        private static Color Segment(Color a, Color b, float pa, float pb, float e, float s)
        {
            var u = Mathf.Clamp01((s - pa) / Mathf.Max(pb - pa, 1e-4f));
            if (e != 1f) u = Mathf.Pow(u, e);
            return Color.Lerp(a, b, u);
        }

        // ---- TMP (spec 2026-09-17 §6.4) ----

        /// <summary>
        /// The four corner colours of one glyph, evaluated on the unit square: TMP paints a gradient
        /// per glyph and gives no per-glyph size, so a two-colour ramp in any direction fits exactly
        /// and a magic corner is its 45° family. Anything else — a third colour, a moved stop, a
        /// hint — has no corner to live on; the caller warns (<c>GradientStopWarning</c>) and the
        /// corners still get the evaluated end colours.
        /// </summary>
        public TMPro.VertexGradient ToVertexGradient()
        {
            var size = Vector2.one;
            var dir = DirectionFor(size);
            var length = Mathf.Max(LineLengthFor(size, dir), 1e-4f);
            var self = this;
            Color At(float x, float y) => self.Evaluate((x * dir.x + y * dir.y + length * 0.5f) / length);
            return new TMPro.VertexGradient(At(-0.5f, 0.5f), At(0.5f, 0.5f), At(-0.5f, -0.5f), At(0.5f, -0.5f));
        }

        // ---- whole-value transforms: colours change, the shape does not ----

        /// <summary>Component-wise multiply (modulate) a tint colour into every stop.</summary>
        public ColorSpec Multiply(Color m)
            => new ColorSpec(C0 * m, C1 * m, C2 * m, C3 * m, P0, P1, P2, P3, E0, E1, E2, Count, Direction);

        /// <summary><c>token/alpha</c>: replace every stop's alpha.</summary>
        public ColorSpec WithAlpha(float a)
            => new ColorSpec(A(C0, a), A(C1, a), A(C2, a), A(C3, a), P0, P1, P2, P3, E0, E1, E2, Count, Direction);

        /// <summary>Every stop at full alpha — what an unset <c>glowColor</c> takes from the fill.</summary>
        public ColorSpec Opaque() => WithAlpha(1f);

        /// <summary>Luma grey per stop, alpha kept — the disabled look of a procedural surface.</summary>
        public ColorSpec Desaturate()
            => new ColorSpec(Grey(C0), Grey(C1), Grey(C2), Grey(C3), P0, P1, P2, P3, E0, E1, E2, Count, Direction);

        private static Color A(Color c, float a) { c.a = a; return c; }

        private static Color Grey(Color c)
        {
            var luma = c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;
            return new Color(luma, luma, luma, c.a);
        }

        // ---- value semantics ----

        public bool Equals(ColorSpec o)
            => Count == o.Count && Direction == o.Direction
            && C0 == o.C0 && C1 == o.C1 && C2 == o.C2 && C3 == o.C3
            && P0 == o.P0 && P1 == o.P1 && P2 == o.P2 && P3 == o.P3
            && E0 == o.E0 && E1 == o.E1 && E2 == o.E2;

        public override bool Equals(object obj) => obj is ColorSpec o && Equals(o);

        public override int GetHashCode()
        {
            unchecked
            {
                int h = Count;
                h = (h * 397) ^ Direction.GetHashCode();
                h = (h * 397) ^ C0.GetHashCode();
                h = (h * 397) ^ C1.GetHashCode();
                h = (h * 397) ^ C2.GetHashCode();
                h = (h * 397) ^ C3.GetHashCode();
                h = (h * 397) ^ P0.GetHashCode();
                h = (h * 397) ^ P1.GetHashCode();
                h = (h * 397) ^ P2.GetHashCode();
                h = (h * 397) ^ P3.GetHashCode();
                h = (h * 397) ^ E0.GetHashCode();
                h = (h * 397) ^ E1.GetHashCode();
                h = (h * 397) ^ E2.GetHashCode();
                return h;
            }
        }

        public static bool operator ==(ColorSpec a, ColorSpec b) => a.Equals(b);
        public static bool operator !=(ColorSpec a, ColorSpec b) => !a.Equals(b);
    }
}
