using PromptUGUI.Application;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The seven uniforms one <see cref="ColorSpec"/> occupies in an SDF shader (spec 2026-09-17
    /// §6.1): four stop colours, their positions, the per-segment curves with the stop count in
    /// <c>w</c>, and the direction. Names mirror <c>PUGUI_RAMP_UNIFORMS(prefix)</c> in
    /// UI-PanelSDF.cginc — <c>_Fill0.._Fill3 / _FillStops / _FillCurves / _FillDir</c> — so the
    /// shader macro and this writer are the two halves of one contract.
    /// </summary>
    internal readonly struct GradientUniforms
    {
        public readonly int C0, C1, C2, C3, Stops, Curves, Dir;

        private GradientUniforms(string prefix)
        {
            C0 = Shader.PropertyToID(prefix + "0");
            C1 = Shader.PropertyToID(prefix + "1");
            C2 = Shader.PropertyToID(prefix + "2");
            C3 = Shader.PropertyToID(prefix + "3");
            Stops = Shader.PropertyToID(prefix + "Stops");
            Curves = Shader.PropertyToID(prefix + "Curves");
            Dir = Shader.PropertyToID(prefix + "Dir");
        }

        public static readonly GradientUniforms Fill = new GradientUniforms("_Fill");
        public static readonly GradientUniforms Border = new GradientUniforms("_Border");
        public static readonly GradientUniforms Glow = new GradientUniforms("_Glow");
        public static readonly GradientUniforms InnerGlow = new GradientUniforms("_InnerGlow");
        /// <summary>The fog colour, which doubles as its directional mask (spec 2026-09-17 haze §4).</summary>
        public static readonly GradientUniforms Haze = new GradientUniforms("_Haze");

        public static void Write(Material mat, in GradientUniforms ids, in ColorSpec spec)
        {
            mat.SetColor(ids.C0, spec.C0);
            mat.SetColor(ids.C1, spec.C1);
            mat.SetColor(ids.C2, spec.C2);
            mat.SetColor(ids.C3, spec.C3);
            mat.SetVector(ids.Stops, new Vector4(spec.P0, spec.P1, spec.P2, spec.P3));
            mat.SetVector(ids.Curves, new Vector4(spec.E0, spec.E1, spec.E2, spec.Count));
            mat.SetVector(ids.Dir, DirectionVector(spec.Direction));
        }

        /// <summary>
        /// The shader's direction word: an angle is its unit vector in <c>xy</c> (y up, CSS
        /// <c>sin θ, cos θ</c>) with <c>z = 0</c>; a corner sets <c>z = 1</c> and puts the CSS
        /// corner index in <c>w</c>, because its actual vector depends on the box and is computed
        /// per fragment (<c>PuguiCornerDir</c>).
        /// </summary>
        public static Vector4 DirectionVector(in GradientDirection direction)
        {
            if (direction.Kind == GradientDirection.Kinds.Corner)
                return new Vector4(0f, 0f, 1f, direction.CornerIndex);
            var v = ColorSpec.AngleVector(direction.AngleDeg);
            return new Vector4(v.x, v.y, 0f, 0f);
        }
    }
}
