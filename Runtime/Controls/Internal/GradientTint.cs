using System.Collections.Generic;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.Pool;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Linear gradient tint as a vertex-colour effect (spec §4.2; direction and multi-stop: spec
    /// 2026-09-17 §6.3). Multiplies the ramp into each vertex's existing colour, so the final
    /// composite stays <c>texture × Graphic.color × gradient</c> — the Graphic.color slot remains free
    /// for state modulates. The gradient line is laid over the actual mesh bounds (Sliced/Tiled have
    /// &gt;4 verts; vertex order is not assumed), CSS style: <c>ColorSpec.DirectionFor</c> /
    /// <c>LineLengthFor</c>, so an &lt;Image&gt; and a &lt;Frame&gt; sharing a token change over on the
    /// same line of pixels. Lazy-added by <c>ColorApplier</c> and toggled via <c>enabled</c>, never
    /// destroyed (Variant/ReSolve round-trips, same convention as ApplyViewportMask).
    ///
    /// <para>A ramp the author shaped — a third colour, moved stops, or a hint's curve — cannot ride on the corner
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

        public Color StartColor => _spec.Start;
        public Color EndColor => _spec.End;

        private static bool Same(in ColorSpec a, in ColorSpec b) => a == b;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || vh.currentVertCount == 0) return;
            if (!_spec.HasStops) ModifyPlain(vh);
            else ModifyWithStops(vh);
        }

        /// <summary>
        /// The gradient line over the mesh's bounds (spec 2026-09-17 §4.1): its unit direction and
        /// the length CSS gives it, plus the centre the projection is measured from. Every vertex's
        /// <c>s = (dot(p − centre, dir) + L/2) / L</c>. The default direction reproduces the old
        /// <c>(maxY − y) / height</c> exactly.
        /// </summary>
        private readonly struct Line
        {
            public readonly Vector2 Centre;
            public readonly Vector2 Dir;
            public readonly float Length;

            public Line(in ColorSpec spec, Vector2 min, Vector2 max)
            {
                Centre = (min + max) * 0.5f;
                var size = max - min;
                Dir = spec.DirectionFor(size);
                Length = Mathf.Max(spec.LineLengthFor(size, Dir), 1e-4f);
            }

            public float Share(Vector3 p)
                => (Vector2.Dot(new Vector2(p.x, p.y) - Centre, Dir) + Length * 0.5f) / Length;

            /// <summary>The raw projection <c>dot(p, dir)</c> at which share <paramref name="s"/> sits —
            /// the value <see cref="MeshSlicer"/> cuts at.</summary>
            public float CutAt(float s)
                => Vector2.Dot(Centre, Dir) + (s - 0.5f) * Length;
        }

        private static bool Bounds(List<UIVertex> tris, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < tris.Count; i++)
            {
                var p = tris[i].position;
                if (p.x < min.x) min.x = p.x;
                if (p.y < min.y) min.y = p.y;
                if (p.x > max.x) max.x = p.x;
                if (p.y > max.y) max.y = p.y;
            }
            return max.x > min.x || max.y > min.y;
        }

        /// <summary>A two-colour ramp with no stops is linear along its direction, so the corner
        /// vertices carry it exactly — no de-indexing, no allocation beyond the bounds scan.</summary>
        private void ModifyPlain(VertexHelper vh)
        {
            var v = new UIVertex();
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var p = v.position;
                if (p.x < min.x) min.x = p.x;
                if (p.y < min.y) min.y = p.y;
                if (p.x > max.x) max.x = p.x;
                if (p.y > max.y) max.y = p.y;
            }

            var flat = max.x <= min.x && max.y <= min.y;
            var line = new Line(_spec, min, max);
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var s = flat ? 1f : line.Share(v.position);
                v.color = (Color)v.color * _spec.Evaluate(s);
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
                if (tris.Count < 3 || !Bounds(tris, out var min, out var max))
                {
                    ModifyPlain(vh);
                    return;
                }

                var line = new Line(_spec, min, max);
                var n = _spec.Count;

                // One cut per stop, then K−1 strips across every segment a hint bends. A hard edge
                // (two stops at one position) cuts once: the triangle stream does not share vertices,
                // so each side keeps its own copy of the line.
                var previous = float.NaN;
                for (var i = 0; i < n; i++)
                {
                    var stop = _spec.StopAt(i);
                    if (stop != previous) Cut(ref tris, ref spare, line, stop);
                    if (i + 1 < n)
                    {
                        var next = _spec.StopAt(i + 1);
                        if (_spec.CurveAt(i) != 1f && next > stop)
                            for (var k = 1; k < HintStrips; k++)
                                Cut(ref tris, ref spare, line, stop + (next - stop) * k / HintStrips);
                    }
                    previous = stop;
                }

                spare.Clear();
                var cullStart = _spec.Start.a <= 0f;
                var cullEnd = _spec.End.a <= 0f;
                var sStart = _spec.StartStop;
                var sEnd = _spec.EndStop;
                for (var i = 0; i + 2 < tris.Count; i += 3)
                {
                    var s0 = line.Share(tris[i].position);
                    var s1 = line.Share(tris[i + 1].position);
                    var s2 = line.Share(tris[i + 2].position);

                    // A stop that ends fully transparent ends the geometry too — no overdraw, and
                    // the crop comes free instead of costing a mask. Only the two ENDS: a see-through
                    // middle stop is a seam inside the picture, not an end of it.
                    if (cullEnd && s0 >= sEnd - CullEpsilon && s1 >= sEnd - CullEpsilon && s2 >= sEnd - CullEpsilon) continue;
                    if (cullStart && s0 <= sStart + CullEpsilon && s1 <= sStart + CullEpsilon && s2 <= sStart + CullEpsilon) continue;

                    var centre = (s0 + s1 + s2) / 3f;
                    for (var k = 0; k < 3; k++)
                    {
                        var v = tris[i + k];
                        var s = k == 0 ? s0 : k == 1 ? s1 : s2;
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

        /// <summary>Slice at share <paramref name="s"/> along the line and swap the working list for the result.</summary>
        private static void Cut(ref List<UIVertex> tris, ref List<UIVertex> spare, in Line line, float s)
        {
            if (s <= 0f || s >= 1f) return;      // the line is an edge of the box; there is nothing to cut
            spare.Clear();
            MeshSlicer.SplitAlongLine(tris, line.Dir, line.CutAt(s), spare);
            var swap = tris;
            tris = spare;
            spare = swap;
        }
    }
}
