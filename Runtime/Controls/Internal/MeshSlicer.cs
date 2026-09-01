using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Cuts a de-indexed uGUI triangle list along a horizontal line. Vertex colours interpolate
    /// linearly between vertices and nowhere else, so a gradient stop in the middle of a face — the
    /// row where <c>ColorSpec</c> stops being flat, or where a hint's curve bends — can only be drawn
    /// once the face actually has vertices there (spec 2026-09-01 VGS §4.2).
    /// </summary>
    internal static class MeshSlicer
    {
        /// <summary>Linear blend of every UIVertex channel: position, normal, tangent, colour,
        /// uv0–uv3. Anything left un-blended shows up as a seam along the cut.</summary>
        public static UIVertex Lerp(in UIVertex a, in UIVertex b, float t)
        {
            return new UIVertex
            {
                position = Vector3.LerpUnclamped(a.position, b.position, t),
                normal = Vector3.LerpUnclamped(a.normal, b.normal, t),
                tangent = Vector4.LerpUnclamped(a.tangent, b.tangent, t),
                color = Color32.Lerp(a.color, b.color, t),
                uv0 = Vector4.LerpUnclamped(a.uv0, b.uv0, t),
                uv1 = Vector4.LerpUnclamped(a.uv1, b.uv1, t),
                uv2 = Vector4.LerpUnclamped(a.uv2, b.uv2, t),
                uv3 = Vector4.LerpUnclamped(a.uv3, b.uv3, t),
            };
        }

        /// <summary>
        /// Splits a de-indexed triangle list (3 vertices per triangle) along <c>y = cut</c> and
        /// appends the result to <paramref name="output"/>. Triangles wholly on one side — including
        /// ones merely touching the line — are copied through untouched. Winding is preserved, so
        /// nothing gets back-face culled, and every new vertex gets <c>y</c> assigned the cut value
        /// exactly rather than whatever the division produced, so the evaluator lands precisely on
        /// the stop instead of a hair to one side of it.
        /// </summary>
        public static void SplitAlongY(List<UIVertex> tris, float cut, List<UIVertex> output)
        {
            for (var i = 0; i + 2 < tris.Count; i += 3)
            {
                var a = tris[i];
                var b = tris[i + 1];
                var c = tris[i + 2];
                var sa = Side(a, cut);
                var sb = Side(b, cut);
                var sc = Side(c, cut);

                // Nothing straddles the line unless some pair is STRICTLY opposite; a vertex sitting
                // on it is not a crossing (that is the sliver case the winding would not survive).
                var min = Mathf.Min(sa, Mathf.Min(sb, sc));
                var max = Mathf.Max(sa, Mathf.Max(sb, sc));
                if (min >= 0 || max <= 0)
                {
                    output.Add(a);
                    output.Add(b);
                    output.Add(c);
                    continue;
                }

                // Rotate the triangle — (a,b,c) → (b,c,a) keeps the winding — until `a` is the pivot:
                // the vertex on the line, or, with none there, the one alone on its side.
                var onLine = sa == 0 || sb == 0 || sc == 0;
                for (var guard = 0; guard < 3; guard++)
                {
                    if (onLine ? sa == 0 : sa != sb && sa != sc) break;
                    var v = a; a = b; b = c; c = v;
                    var s = sa; sa = sb; sb = sc; sc = s;
                }

                if (onLine)
                {
                    // One corner already sits on the line: a single cut through the opposite edge.
                    output.Add(a);
                    output.Add(b);
                    var m = OnCut(b, c, cut);
                    output.Add(m);

                    output.Add(a);
                    output.Add(m);
                    output.Add(c);
                }
                else
                {
                    // The lone corner keeps a tip triangle; the other two keep a quad.
                    var mb = OnCut(a, b, cut);
                    var mc = OnCut(a, c, cut);

                    output.Add(a);
                    output.Add(mb);
                    output.Add(mc);

                    output.Add(mb);
                    output.Add(b);
                    output.Add(c);

                    output.Add(mb);
                    output.Add(c);
                    output.Add(mc);
                }
            }
        }

        private static int Side(in UIVertex v, float cut)
            => v.position.y > cut ? 1 : v.position.y < cut ? -1 : 0;

        /// <summary>The point where edge <c>from → to</c> crosses the line, pinned to it exactly.</summary>
        private static UIVertex OnCut(in UIVertex from, in UIVertex to, float cut)
        {
            var m = Lerp(from, to, (cut - from.position.y) / (to.position.y - from.position.y));
            var p = m.position;
            p.y = cut;
            m.position = p;
            return m;
        }
    }
}
