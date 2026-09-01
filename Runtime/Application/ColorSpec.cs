using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// A resolved colour value: solid, or a two-stop vertical gradient (Top → Bottom).
    /// Produced by <c>UI.Theme.ResolveSpec</c>; applied by <c>ColorApplier</c>
    /// (vertex-tint slot) or the TMP vertex-gradient path in <c>Text</c>.
    /// Solid values keep <see cref="Bottom"/> == <see cref="Top"/> so consumers that
    /// only need one colour can read Top unconditionally.
    /// </summary>
    internal readonly struct ColorSpec
    {
        public readonly Color Top;
        public readonly Color Bottom;
        /// <summary>Where <see cref="Top"/> stops being solid, as a 0..1 share measured from the
        /// top edge. Default 0 — the ramp starts at the very top.</summary>
        public readonly float TopStop;
        /// <summary>Where <see cref="Bottom"/> becomes solid, measured from the top edge.
        /// Default 1 — the ramp ends at the very bottom.</summary>
        public readonly float BottomStop;
        /// <summary>
        /// The power the normalized ramp is raised to, from a CSS colour hint. <c>1</c> is the plain
        /// linear ramp. A hint bends the whole ramp instead of cutting it, which is the difference
        /// between "mostly blue, gold creeping in at the bottom" and a visible dividing line: a moved
        /// stop leaves a slope discontinuity, and the eye reads that as an edge (spec §14).
        /// </summary>
        public readonly float Curve;
        public readonly bool IsGradient;

        private ColorSpec(Color top, Color bottom, float topStop, float bottomStop, float curve, bool isGradient)
        {
            Top = top;
            Bottom = bottom;
            TopStop = topStop;
            BottomStop = bottomStop;
            Curve = curve;
            IsGradient = isGradient;
        }

        public static ColorSpec Solid(Color c) => new ColorSpec(c, c, 0f, 1f, 1f, false);

        public static ColorSpec Gradient(Color top, Color bottom) => Gradient(top, bottom, 0f, 1f, 1f);

        public static ColorSpec Gradient(Color top, Color bottom, float topStop, float bottomStop)
            => Gradient(top, bottom, topStop, bottomStop, 1f);

        public static ColorSpec Gradient(Color top, Color bottom, float topStop, float bottomStop, float curve)
            => new ColorSpec(top, bottom, topStop, bottomStop, curve, true);

        /// <summary>
        /// The author shaped the ramp — moved a stop, or bent it with a hint. Both the procedural
        /// shader (per fragment) and <c>GradientTint</c> (by slicing the mesh at the stops, spec
        /// 2026-09-01 VGS §4.2) can draw that. TMP text cannot: it paints a gradient per glyph, and
        /// four glyph corners have nowhere to put a stop — that path warns instead of lying.
        /// </summary>
        public bool HasStops => IsGradient && (TopStop != 0f || BottomStop != 1f || Curve != 1f);

        /// <summary>
        /// The colour at normalized distance <paramref name="s"/> from the TOP edge (0 = top,
        /// 1 = bottom) — the same ramp <c>PuguiFillRamp</c> (UI-PanelSDF.cginc) draws per fragment,
        /// evaluated per vertex. Keep the two in step: a &lt;Frame&gt; and an &lt;Image&gt; carrying
        /// the same token have to change over at the same row of pixels.
        /// </summary>
        public Color Evaluate(float s)
        {
            var span = Mathf.Max(BottomStop - TopStop, 1e-4f);
            var u = Mathf.Clamp01((s - TopStop) / span);
            if (Curve != 1f) u = Mathf.Pow(u, Curve);
            return Color.Lerp(Top, Bottom, u);
        }

        /// <summary>Component-wise multiply (modulate) a tint colour into both stops. Stop
        /// positions describe the shape of the ramp and are left alone.</summary>
        public ColorSpec Multiply(Color m)
            => new ColorSpec(Top * m, Bottom * m, TopStop, BottomStop, Curve, IsGradient);
    }
}
