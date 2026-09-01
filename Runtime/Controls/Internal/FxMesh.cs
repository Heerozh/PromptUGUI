using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The geometry half of <c>blur</c> / <c>glow</c> on a sprite graphic (spec 2026-09-02 §4.2).
    /// Takes the four vertices <see cref="UnityEngine.UI.Image"/> generated for a Simple sprite and
    /// pushes each corner <c>pad</c> units outward on both axes, extrapolating uv0 by the matching
    /// amount so the fragment can keep sampling the atlas linearly past the sprite's edge.
    ///
    /// <para>Before moving anything it records the two things the fragment cannot work out for
    /// itself: the sprite's own UV rectangle into <c>uv1</c> (a tap outside it is a NEIGHBOUR in the
    /// atlas and must read as empty, which is the whole reason a glow can leave the sprite at all),
    /// and the uv-per-canvas-unit scale into <c>uv2</c> (a radius is authored in pixels; this is what
    /// turns it into a uv offset). Both are computed here rather than read from
    /// <c>_MainTex_TexelSize</c> — see the same reasoning in <c>UI-GlassBlur.shader</c>: a texture
    /// bound by something other than the usual path is not guaranteed to have it filled in.</para>
    ///
    /// <para>Each vertex is pushed away from its own centre on each channel independently — the uv
    /// side is decided by the uv centre, not by the position centre. Art whose uv runs against its
    /// position (a mirrored sprite; a rotated atlas entry, were one ever supported) would otherwise
    /// have its uv pulled INTO the sprite on one side.</para>
    /// </summary>
    internal static class FxMesh
    {
        /// <summary>
        /// Inflates the quad in <paramref name="vh"/> and writes the fx channels.
        /// </summary>
        /// <returns>
        /// false — leaving the mesh exactly as it was — unless the mesh is a single quad
        /// (four vertices) covering a non-degenerate area in both space and uv. Sliced, Tiled,
        /// Filled and sprite-mesh geometry all land here, and all are declined: the fx path only
        /// supports <c>type="simple"</c> (spec §3).
        /// </returns>
        public static bool Inflate(VertexHelper vh, float pad)
        {
            if (vh == null || vh.currentVertCount != 4) return false;

            var v = new UIVertex();
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            float minU = float.MaxValue, minV = float.MaxValue;
            float maxU = float.MinValue, maxV = float.MinValue;

            for (var i = 0; i < 4; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                minX = Mathf.Min(minX, v.position.x);
                minY = Mathf.Min(minY, v.position.y);
                maxX = Mathf.Max(maxX, v.position.x);
                maxY = Mathf.Max(maxY, v.position.y);
                minU = Mathf.Min(minU, v.uv0.x);
                minV = Mathf.Min(minV, v.uv0.y);
                maxU = Mathf.Max(maxU, v.uv0.x);
                maxV = Mathf.Max(maxV, v.uv0.y);
            }

            var width = maxX - minX;
            var height = maxY - minY;
            var uWidth = maxU - minU;
            var vHeight = maxV - minV;
            if (width <= 0f || height <= 0f || uWidth <= 0f || vHeight <= 0f) return false;

            var rect = new Vector4(minU, minV, maxU, maxV);
            var perUnit = new Vector4(uWidth / width, vHeight / height, 0f, 0f);

            var cx = (minX + maxX) * 0.5f;
            var cy = (minY + maxY) * 0.5f;
            var cu = (minU + maxU) * 0.5f;
            var cv = (minV + maxV) * 0.5f;
            var p = Mathf.Max(0f, pad);

            for (var i = 0; i < 4; i++)
            {
                vh.PopulateUIVertex(ref v, i);

                var sx = v.position.x < cx ? -1f : 1f;
                var sy = v.position.y < cy ? -1f : 1f;
                var su = v.uv0.x < cu ? -1f : 1f;
                var sv = v.uv0.y < cv ? -1f : 1f;

                v.position = new Vector3(v.position.x + sx * p, v.position.y + sy * p, v.position.z);
                v.uv0 = new Vector4(v.uv0.x + su * p * perUnit.x,
                                    v.uv0.y + sv * p * perUnit.y,
                                    v.uv0.z, v.uv0.w);
                v.uv1 = rect;
                v.uv2 = perUnit;

                vh.SetUIVertex(v, i);
            }

            return true;
        }
    }
}
