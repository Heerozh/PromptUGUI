using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Vertical gradient tint as a vertex-colour effect (spec §4.2). Multiplies the ramp into each
    /// vertex's existing colour, so the final composite stays <c>texture × Graphic.color ×
    /// gradient</c> — the Graphic.color slot remains free for state modulates. Y is normalized across
    /// the actual mesh bounds (Sliced/Tiled have &gt;4 verts; vertex order is not assumed). Lazy-added
    /// by <c>ColorApplier</c> and toggled via <c>enabled</c>, never destroyed (Variant/ReSolve
    /// round-trips, same convention as ApplyViewportMask).
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    internal sealed class GradientTint : BaseMeshEffect
    {
        private ColorSpec _spec = ColorSpec.Gradient(Color.white, Color.white);

        /// <summary>The whole resolved value, stop positions and hint curve included — the shape of
        /// the ramp has to survive a <c>Peek</c> / re-<c>Apply</c> round trip through
        /// <c>StateTintReactor</c>, which only modulates the colours.</summary>
        public ColorSpec Spec => _spec;

        public void Set(in ColorSpec spec)
        {
            if (Same(_spec, spec)) return;
            _spec = spec;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        /// <summary>Convenience for the plain two-colour ramp.</summary>
        public void Set(Color top, Color bottom) => Set(ColorSpec.Gradient(top, bottom));

        public Color Top => _spec.Top;
        public Color Bottom => _spec.Bottom;

        private static bool Same(in ColorSpec a, in ColorSpec b)
            => a.Top == b.Top && a.Bottom == b.Bottom
            && a.TopStop == b.TopStop && a.BottomStop == b.BottomStop
            && a.Curve == b.Curve && a.IsGradient == b.IsGradient;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;

            var v = new UIVertex();
            float minY = float.MaxValue, maxY = float.MinValue;
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                if (v.position.y < minY) minY = v.position.y;
                if (v.position.y > maxY) maxY = v.position.y;
            }

            var h = maxY - minY;
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var t = h > 0f ? (v.position.y - minY) / h : 1f;
                v.color = (Color)v.color * Color.Lerp(_spec.Bottom, _spec.Top, t);
                vh.SetUIVertex(v, i);
            }
        }
    }
}
