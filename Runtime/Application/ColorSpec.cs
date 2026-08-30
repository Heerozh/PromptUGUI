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
        /// The author shaped the ramp — moved a stop, or bent it with a hint. Only the procedural
        /// shader can draw either: a vertex-coloured Graphic has nothing but corner vertices to place
        /// them on, so the hardware interpolates straight through and the ramp comes out plain and
        /// full-height anyway (spec 2026-08-30 §5). Callers on the vertex path use this to warn
        /// rather than lie.
        /// </summary>
        public bool HasStops => IsGradient && (TopStop != 0f || BottomStop != 1f || Curve != 1f);

        /// <summary>Component-wise multiply (modulate) a tint colour into both stops. Stop
        /// positions describe the shape of the ramp and are left alone.</summary>
        public ColorSpec Multiply(Color m)
            => new ColorSpec(Top * m, Bottom * m, TopStop, BottomStop, Curve, IsGradient);
    }
}
