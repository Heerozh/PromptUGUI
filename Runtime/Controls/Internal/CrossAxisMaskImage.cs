using UnityEngine;
using UnityEngine.Sprites;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The stencil-mask graphic on a <see cref="ScrollList"/>'s Viewport. A list scrolls along ONE
    /// axis, so only that axis has anything to clip: a row that is wider than the viewport is an
    /// authoring mistake, but a row's glow, its shadow, or the 1.03 lift scale of a drag-to-reorder
    /// session is not — and the viewport used to cut all of them at its left / right edge.
    ///
    /// <para>This draws what <see cref="UnityImage"/> draws (the sprite, 9-sliced), then adds a band along
    /// the scroll axis that stretches the straight part of the two cross-axis edges to "infinity"
    /// (<see cref="Reach"/>). The band stops at the sprite's 9-slice borders, so the corner pieces —
    /// the rounding — still clip exactly where they did; only the straight sides stop clipping. The
    /// stencil the <c>Mask</c> writes is the union: rounded corners, open sides. A sprite with no
    /// border is not a 9-slice and gives no hint where its straight edges are, so it is left alone
    /// (a hexagonal list keeps its hexagon).</para>
    ///
    /// <para>The <c>RectMask2D</c> twin (a square list) is <c>RectMask2D.padding</c> pushed out on
    /// the cross axis — see <c>ScrollList.ApplyCrossAxisClip</c>.</para>
    /// </summary>
    // Image's own [RequireComponent(typeof(CanvasRenderer))] does not carry over to a subclass added
    // via AddComponent at runtime (see ProceduralPanel / HitCatcher).
    [RequireComponent(typeof(CanvasRenderer))]
    internal sealed class CrossAxisMaskImage : UnityImage
    {
        /// <summary>How far the open sides reach, in canvas units — "off any screen".</summary>
        internal const float Reach = 100000f;

        // -1 = plain Image; 0 = the band runs along X (a vertical list: left / right stay open);
        // 1 = along Y (a horizontal list: top / bottom stay open).
        private int _axis = -1;

        internal int BandAxis => _axis;

        internal void SetBandAxis(int axis)
        {
            if (_axis == axis) return;
            _axis = axis;
            SetVerticesDirty();
        }

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            base.OnPopulateMesh(vh);
            if (_axis < 0) return;
            var s = overrideSprite;
            if (s == null) return;
            var border = s.border;   // (left, bottom, right, top) in sprite pixels
            if (border == Vector4.zero) return;

            // Where the corner pieces end, in the same units Image slices with.
            var ppu = multipliedPixelsPerUnit;
            if (ppu <= 0f) return;
            var inset = border / ppu;
            var r = GetPixelAdjustedRect();
            var band = _axis == 0
                ? Rect.MinMaxRect(-Reach, r.yMin + inset.y, Reach, r.yMax - inset.w)
                : Rect.MinMaxRect(r.xMin + inset.x, -Reach, r.xMax - inset.z, Reach);
            if (band.width <= 0f || band.height <= 0f) return;

            // An opaque texel: the centre of the sprite's inner (middle 9-slice) piece — the same
            // pixels that already tile the inside of the straight edges. The Mask writes stencil
            // wherever alpha survives the UI shader's clip, so this is all the band needs.
            var inner = DataUtility.GetInnerUV(s);
            var uv = new Vector2((inner.x + inner.z) * 0.5f, (inner.y + inner.w) * 0.5f);
            var c = color;
            var i = vh.currentVertCount;
            vh.AddVert(new Vector3(band.xMin, band.yMin), c, uv);
            vh.AddVert(new Vector3(band.xMin, band.yMax), c, uv);
            vh.AddVert(new Vector3(band.xMax, band.yMax), c, uv);
            vh.AddVert(new Vector3(band.xMax, band.yMin), c, uv);
            vh.AddTriangle(i, i + 1, i + 2);
            vh.AddTriangle(i + 2, i + 3, i);
        }

        /// <summary>Test seams: the mesh this graphic would submit, without a canvas rebuild, and the
        /// units it slices with (Image keeps <c>multipliedPixelsPerUnit</c> protected).</summary>
        internal void PopulateForTests(VertexHelper vh) => OnPopulateMesh(vh);
        internal float PixelsPerUnitForTests => multipliedPixelsPerUnit;
    }
}
