using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Two-stop vertical gradient tint as a vertex-colour effect (spec §4.2). Multiplies
    /// Lerp(Bottom, Top, normalizedY) into each vertex's existing colour, so the final
    /// composite stays <c>texture × Graphic.color × gradient</c> — the Graphic.color slot
    /// remains free for state modulates. Y is normalized across the actual mesh bounds
    /// (Sliced/Tiled have &gt;4 verts; vertex order is not assumed). Lazy-added by
    /// <c>ColorApplier</c> and toggled via <c>enabled</c>, never destroyed
    /// (Variant/ReSolve round-trips, same convention as ApplyViewportMask).
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    internal sealed class GradientTint : BaseMeshEffect
    {
        private Color _top = Color.white;
        private Color _bottom = Color.white;

        public void Set(Color top, Color bottom)
        {
            if (_top == top && _bottom == bottom) return;
            _top = top;
            _bottom = bottom;
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public Color Top => _top;
        public Color Bottom => _bottom;

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
                v.color = (Color)v.color * Color.Lerp(_bottom, _top, t);
                vh.SetUIVertex(v, i);
            }
        }
    }
}
