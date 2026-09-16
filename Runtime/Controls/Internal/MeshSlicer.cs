using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Cuts a de-indexed uGUI triangle list along a straight line. Vertex colours interpolate
    /// linearly between vertices and nowhere else, so a gradient stop in the middle of a face — the
    /// line where <c>ColorSpec</c> stops being flat, or where a hint's curve bends — can only be
    /// drawn once the face actually has vertices there (spec 2026-09-01 VGS §4.2). The line is
    /// given as <c>dot(position, dir) = cut</c>, so a gradient in any direction (spec 2026-09-17
    /// §6.3) slices perpendicular to itself; the vertical ramp is <c>dir = (0, 1)</c>.
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

        /// <summary>The vertical ramp's special case: <c>y = cut</c>.</summary>
        public static void SplitAlongY(List<UIVertex> tris, float cut, List<UIVertex> output)
            => SplitAlongLine(tris, Vector2.up, cut, output);

        /// <summary>
        /// Splits a de-indexed triangle list (3 vertices per triangle) along the line
        /// <c>dot(position.xy, dir) = cut</c> and appends the result to <paramref name="output"/>.
        /// Triangles wholly on one side — including ones merely touching the line — are copied
        /// through untouched. Winding is preserved, so nothing gets back-face culled, and every new
        /// vertex has its projection pinned to <paramref name="cut"/> exactly rather than whatever
        /// the division produced, so the evaluator lands precisely on the stop instead of a hair to
        /// one side of it.
        /// </summary>
        public static void SplitAlongLine(List<UIVertex> tris, Vector2 dir, float cut, List<UIVertex> output)
        {
            for (var i = 0; i + 2 < tris.Count; i += 3)
            {
                var a = tris[i];
                var b = tris[i + 1];
                var c = tris[i + 2];
                var sa = Side(a, dir, cut);
                var sb = Side(b, dir, cut);
                var sc = Side(c, dir, cut);

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
                    var m = OnCut(b, c, dir, cut);
                    output.Add(m);

                    output.Add(a);
                    output.Add(m);
                    output.Add(c);
                }
                else
                {
                    // The lone corner keeps a tip triangle; the other two keep a quad.
                    var mb = OnCut(a, b, dir, cut);
                    var mc = OnCut(a, c, dir, cut);

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

        private static float Project(in UIVertex v, Vector2 dir)
            => v.position.x * dir.x + v.position.y * dir.y;

        private static int Side(in UIVertex v, Vector2 dir, float cut)
        {
            var p = Project(v, dir);
            return p > cut ? 1 : p < cut ? -1 : 0;
        }

        /// <summary>The point where edge <c>from → to</c> crosses the line, pinned to it exactly.</summary>
        private static UIVertex OnCut(in UIVertex from, in UIVertex to, Vector2 dir, float cut)
        {
            var pf = Project(from, dir);
            var pt = Project(to, dir);
            var m = Lerp(from, to, (cut - pf) / (pt - pf));
            // Slide along the direction until the projection reads the cut value exactly.
            var p = m.position;
            var drift = cut - (p.x * dir.x + p.y * dir.y);
            p.x += dir.x * drift;
            p.y += dir.y * drift;
            m.position = p;
            return m;
        }
    }
}
