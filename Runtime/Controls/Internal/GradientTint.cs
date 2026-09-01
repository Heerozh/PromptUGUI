using System.Collections.Generic;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.Pool;
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
    ///
    /// <para>A ramp the author shaped — moved stops, or a hint's curve — cannot ride on the corner
    /// vertices alone, so it takes a second path: cut the mesh at the stops (and at a few strips
    /// across a hint), then evaluate <c>ColorSpec.Evaluate</c> per vertex. Without stops nothing is
    /// de-indexed and the geometry comes out exactly as it does today (spec 2026-09-01 VGS §4.2,
    /// VGS-D1).</para>
    /// </summary>
    [RequireComponent(typeof(Graphic))]
    internal sealed class GradientTint : BaseMeshEffect
    {
        /// <summary>How many strips stand in for a hint's curve between the two stops. The chord
        /// error at eight is a fraction of a colour step at any size a UI element is drawn (VGS-D4).</summary>
        private const int HintStrips = 8;

        /// <summary>Triangles this far past a fully transparent stop are dropped outright.</summary>
        private const float CullEpsilon = 1e-3f;

        /// <summary>A vertex sitting exactly on a cut belongs to two triangles that want different
        /// colours there — that is what a hard edge (both stops at one position) IS. Nudging each
        /// vertex a hair towards its own triangle's centre picks the right side; anywhere the ramp is
        /// continuous the shift stays far below one colour step.</summary>
        private const float CentroidBias = 1e-3f;

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
            if (!_spec.HasStops) ModifyPlain(vh);
            else ModifyWithStops(vh);
        }

        /// <summary>The full-height two-colour ramp: the corners already carry it.</summary>
        private void ModifyPlain(VertexHelper vh)
        {
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

        private void ModifyWithStops(VertexHelper vh)
        {
            var tris = ListPool<UIVertex>.Get();
            var spare = ListPool<UIVertex>.Get();
            try
            {
                vh.GetUIVertexStream(tris);
                if (tris.Count < 3)
                {
                    ModifyPlain(vh);
                    return;
                }

                float minY = float.MaxValue, maxY = float.MinValue;
                for (var i = 0; i < tris.Count; i++)
                {
                    var y = tris[i].position.y;
                    if (y < minY) minY = y;
                    if (y > maxY) maxY = y;
                }

                var h = maxY - minY;
                if (h <= 0f)
                {
                    ModifyPlain(vh);
                    return;
                }

                // Stops are shares measured from the TOP edge, matching PuguiFillRamp.
                var a = _spec.TopStop;
                var b = _spec.BottomStop;
                var yA = maxY - a * h;
                var yB = maxY - b * h;

                Cut(ref tris, ref spare, yA, minY, maxY);
                if (b != a) Cut(ref tris, ref spare, yB, minY, maxY);
                if (_spec.Curve != 1f && b > a)
                    for (var k = 1; k < HintStrips; k++)
                        Cut(ref tris, ref spare, maxY - (a + (b - a) * k / HintStrips) * h, minY, maxY);

                spare.Clear();
                var cullTop = _spec.Top.a <= 0f;
                var cullBottom = _spec.Bottom.a <= 0f;
                for (var i = 0; i + 2 < tris.Count; i += 3)
                {
                    var y0 = tris[i].position.y;
                    var y1 = tris[i + 1].position.y;
                    var y2 = tris[i + 2].position.y;

                    // A stop that ends fully transparent ends the geometry too — no overdraw, and
                    // the letterbox crop comes free instead of costing a mask.
                    if (cullBottom && y0 <= yB + CullEpsilon && y1 <= yB + CullEpsilon && y2 <= yB + CullEpsilon) continue;
                    if (cullTop && y0 >= yA - CullEpsilon && y1 >= yA - CullEpsilon && y2 >= yA - CullEpsilon) continue;

                    var centre = (maxY - (y0 + y1 + y2) / 3f) / h;
                    for (var k = 0; k < 3; k++)
                    {
                        var v = tris[i + k];
                        var s = (maxY - v.position.y) / h;
                        v.color = (Color)v.color * _spec.Evaluate(Mathf.Lerp(s, centre, CentroidBias));
                        spare.Add(v);
                    }
                }

                vh.Clear();
                vh.AddUIVertexTriangleStream(spare);
            }
            finally
            {
                ListPool<UIVertex>.Release(tris);
                ListPool<UIVertex>.Release(spare);
            }
        }

        /// <summary>Slice at <paramref name="y"/> and swap the working list for the result.</summary>
        private static void Cut(ref List<UIVertex> tris, ref List<UIVertex> spare, float y, float minY, float maxY)
        {
            if (y <= minY || y >= maxY) return;      // the line is an edge; there is nothing to cut
            spare.Clear();
            MeshSlicer.SplitAlongY(tris, y, spare);
            var swap = tris;
            tris = spare;
            spare = swap;
        }
    }
}
