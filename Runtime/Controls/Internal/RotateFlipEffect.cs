using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Mesh-level rotation and mirroring for <c>&lt;Image&gt;</c> / <c>&lt;Icon&gt;</c> /
    /// <c>&lt;RawImage&gt;</c> (spec 2026-08-31-hug-reveal-flip-checked-design §3). It rewrites the
    /// generated vertices about the rect's centre and touches nothing else: the RectTransform,
    /// anchors, margins, pivot, <c>LayoutElement</c> and raycast area are all exactly as authored.
    ///
    /// <para><b>Why not the transform.</b> Three reasons, each fatal on its own: a
    /// <c>localEulerAngles</c> turn happens about the <i>pivot</i>, which is derived from the anchor,
    /// so a top-left anchored icon would swing out of its slot (the trap
    /// <c>TabMenu</c> documented when it flipped its caret by scale instead); a parent LayoutGroup
    /// measures the un-rotated rect, so a rotated child would overlap its siblings; and
    /// <c>Screen.ApplyScales</c> overwrites <c>localScale</c> outright for any node declaring
    /// <c>scale=</c>, which would silently erase a scale-based mirror.</para>
    ///
    /// <para>Identity (no rotation, no flip) disables the component, so the common case costs
    /// nothing. Being a <see cref="BaseMeshEffect"/> it does not break batching.</para>
    /// </summary>
    [DisallowMultipleComponent]
    internal sealed class RotateFlipEffect : BaseMeshEffect
    {
        private float _rotation;
        private bool _flipX;
        private bool _flipY;

        /// <summary>Clockwise degrees (CSS convention), normalised to [0, 360).</summary>
        public float Rotation
        {
            get => _rotation;
            set
            {
                var v = Normalize(value);
                if (Mathf.Approximately(_rotation, v)) return;
                _rotation = v;
                Refresh();
            }
        }

        public bool FlipX
        {
            get => _flipX;
            set { if (_flipX == value) return; _flipX = value; Refresh(); }
        }

        public bool FlipY
        {
            get => _flipY;
            set { if (_flipY == value) return; _flipY = value; Refresh(); }
        }

        public bool IsIdentity => _rotation == 0f && !_flipX && !_flipY;

        /// <summary>Whether these authored values would be a no-op — asked before attaching.</summary>
        internal static bool IsIdentityValues(float rotation, bool flipX, bool flipY)
            => Normalize(rotation) == 0f && !flipX && !flipY;

        internal static float Normalize(float degrees)
        {
            var v = degrees % 360f;
            if (v < 0f) v += 360f;
            return v;
        }

        private void Refresh()
        {
            if (graphic != null) graphic.SetVerticesDirty();
        }

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || IsIdentity || graphic == null) return;

            // The rect's own centre, so the result is independent of the pivot (and therefore of the
            // anchor the pivot was derived from) — see the class note.
            var center = graphic.rectTransform.rect.center;
            var rad = _rotation * Mathf.Deg2Rad;
            var cos = Mathf.Cos(rad);
            var sin = Mathf.Sin(rad);

            var v = new UIVertex();
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var p = Apply(new Vector2(v.position.x, v.position.y), center, _flipX, _flipY, cos, sin);
                // uv0 is deliberately left alone: it travels with its vertex, so the picture turns
                // with the quad instead of sliding under it. A 9-slice keeps its borders as borders.
                v.position = new Vector3(p.x, p.y, v.position.z);
                vh.SetUIVertex(v, i);
            }
        }

        /// <summary>
        /// Mirrors then rotates one point about <paramref name="center"/>. Flip first: "rotate the
        /// mirrored glyph" is what an author writing both means, and it makes the pair read the same
        /// way the two attributes do, left to right.
        /// </summary>
        internal static Vector2 Apply(Vector2 p, Vector2 center, bool flipX, bool flipY, float cos, float sin)
        {
            var d = p - center;
            if (flipX) d.x = -d.x;
            if (flipY) d.y = -d.y;

            // Clockwise: Unity's positive Z rotation runs counter-clockwise, so this is the transpose
            // of the textbook matrix.
            d = new Vector2(d.x * cos + d.y * sin, -d.x * sin + d.y * cos);
            return d + center;
        }
    }
}
