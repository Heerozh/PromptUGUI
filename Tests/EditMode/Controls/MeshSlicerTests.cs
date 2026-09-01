using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>MeshSlicer</c> — the geometry half of vertex-path gradient stops (spec 2026-09-01 VGS
    /// §4.2). A stop is a colour discontinuity in the middle of a face, and hardware interpolation
    /// only runs between vertices, so the face has to be cut there first.
    /// </summary>
    public class MeshSlicerTests
    {
        private static UIVertex Vert(float x, float y, Color32 color, Vector4 uv0)
        {
            var v = UIVertex.simpleVert;
            v.position = new Vector3(x, y, 0f);
            v.color = color;
            v.uv0 = uv0;
            return v;
        }

        private static UIVertex Vert(float x, float y) => Vert(x, y, Color.white, Vector4.zero);

        private static void Tri(List<UIVertex> into, UIVertex a, UIVertex b, UIVertex c)
        {
            into.Add(a);
            into.Add(b);
            into.Add(c);
        }

        /// <summary>Signed double area — the sign carries the winding.</summary>
        private static float Cross(UIVertex a, UIVertex b, UIVertex c)
        {
            var ab = b.position - a.position;
            var ac = c.position - a.position;
            return ab.x * ac.y - ab.y * ac.x;
        }

        private static float TotalArea(List<UIVertex> tris)
        {
            var sum = 0f;
            for (var i = 0; i < tris.Count; i += 3)
                sum += Mathf.Abs(Cross(tris[i], tris[i + 1], tris[i + 2])) * 0.5f;
            return sum;
        }

        private static UIVertex Find(List<UIVertex> tris, float x, float y)
        {
            foreach (var v in tris)
                if (Mathf.Abs(v.position.x - x) < 1e-3f && Mathf.Abs(v.position.y - y) < 1e-3f)
                    return v;
            Assert.Fail("no vertex near (" + x + ", " + y + ")");
            return default;
        }

        [Test]
        public void SingleTriangle_SplitsInThreeAndPinsTheCutExactly()
        {
            var apex = Vert(50f, 100f, Color.red, new Vector4(0.5f, 1f, 0f, 0f));
            var left = Vert(0f, 0f, Color.blue, new Vector4(0f, 0f, 0f, 0f));
            var right = Vert(100f, 0f, Color.blue, new Vector4(1f, 0f, 0f, 0f));

            var tris = new List<UIVertex>();
            Tri(tris, left, right, apex);
            var output = new List<UIVertex>();

            MeshSlicer.SplitAlongY(tris, 50f, output);

            Assert.AreEqual(9, output.Count, "tip triangle plus a quad");
            foreach (var v in output)
                if (Mathf.Abs(v.position.y - 50f) < 1f)
                    Assert.AreEqual(50f, v.position.y, "a cut vertex must sit EXACTLY on the line, "
                        + "or Evaluate lands a hair off the stop");

            // Halfway up the left edge: colour and uv blend with it.
            var mid = Find(output, 25f, 50f);
            Assert.AreEqual(127, mid.color.r, 2);
            Assert.AreEqual(127, mid.color.b, 2);
            Assert.AreEqual(0.25f, mid.uv0.x, 1e-4f);
            Assert.AreEqual(0.5f, mid.uv0.y, 1e-4f);

            Assert.AreEqual(TotalArea(tris), TotalArea(output), 1e-2f, "area is conserved");
        }

        [Test]
        public void Quad_EveryTriangleEndsUpWhollyOnOneSide()
        {
            // uGUI winding: BL, TL, TR then BL, TR, BR.
            var bl = Vert(0f, 0f);
            var tl = Vert(0f, 100f);
            var tr = Vert(100f, 100f);
            var br = Vert(100f, 0f);
            var tris = new List<UIVertex>();
            Tri(tris, bl, tl, tr);
            Tri(tris, bl, tr, br);
            var output = new List<UIVertex>();

            MeshSlicer.SplitAlongY(tris, 50f, output);

            for (var i = 0; i < output.Count; i += 3)
            {
                var above = 0;
                var below = 0;
                for (var k = 0; k < 3; k++)
                {
                    if (output[i + k].position.y > 50f + 1e-4f) above++;
                    if (output[i + k].position.y < 50f - 1e-4f) below++;
                }
                Assert.IsTrue(above == 0 || below == 0, "no triangle may straddle the cut any more");
            }

            Assert.AreEqual(TotalArea(tris), TotalArea(output), 1e-2f, "area is conserved");
            Find(output, 0f, 50f);
            Find(output, 100f, 50f);
        }

        [Test]
        public void VertexAlreadyOnTheLine_SplitsInTwoWithNoSlivers()
        {
            var tris = new List<UIVertex>();
            Tri(tris, Vert(0f, 50f), Vert(100f, 0f), Vert(100f, 100f));
            var output = new List<UIVertex>();

            MeshSlicer.SplitAlongY(tris, 50f, output);

            Assert.AreEqual(6, output.Count, "one triangle each side, no degenerate third");
            for (var i = 0; i < output.Count; i += 3)
                Assert.Greater(Mathf.Abs(Cross(output[i], output[i + 1], output[i + 2])), 1e-3f,
                    "zero-area triangles cost draw work and mean the split logic double-counted");
            Assert.AreEqual(TotalArea(tris), TotalArea(output), 1e-2f);
        }

        [Test]
        public void CutOutsideTheBounds_PassesGeometryThrough()
        {
            var tris = new List<UIVertex>();
            Tri(tris, Vert(0f, 0f, Color.red, Vector4.one), Vert(100f, 0f), Vert(50f, 100f));
            var output = new List<UIVertex>();

            MeshSlicer.SplitAlongY(tris, 200f, output);

            Assert.AreEqual(tris.Count, output.Count);
            for (var i = 0; i < tris.Count; i++)
            {
                Assert.AreEqual(tris[i].position, output[i].position);
                Assert.AreEqual(tris[i].color, output[i].color);
                Assert.AreEqual(tris[i].uv0, output[i].uv0);
            }
        }

        [Test]
        public void WindingIsPreserved()
        {
            var tris = new List<UIVertex>();
            Tri(tris, Vert(0f, 0f), Vert(0f, 100f), Vert(100f, 100f));
            var expected = Mathf.Sign(Cross(tris[0], tris[1], tris[2]));
            var output = new List<UIVertex>();

            MeshSlicer.SplitAlongY(tris, 50f, output);

            for (var i = 0; i < output.Count; i += 3)
                Assert.AreEqual(expected, Mathf.Sign(Cross(output[i], output[i + 1], output[i + 2])),
                    "a flipped triangle would be culled away and leave a hole");
        }

        [Test]
        public void TwoCuts_LeaveThreeBandsOfGeometry()
        {
            var tris = new List<UIVertex>();
            Tri(tris, Vert(0f, 0f), Vert(0f, 100f), Vert(100f, 100f));
            Tri(tris, Vert(0f, 0f), Vert(100f, 100f), Vert(100f, 0f));

            var first = new List<UIVertex>();
            MeshSlicer.SplitAlongY(tris, 30f, first);
            var second = new List<UIVertex>();
            MeshSlicer.SplitAlongY(first, 60f, second);

            for (var i = 0; i < second.Count; i += 3)
            {
                var lo = Mathf.Min(second[i].position.y, Mathf.Min(second[i + 1].position.y, second[i + 2].position.y));
                var hi = Mathf.Max(second[i].position.y, Mathf.Max(second[i + 1].position.y, second[i + 2].position.y));
                var banded = (lo >= 60f - 1e-4f && hi <= 100f + 1e-4f)
                          || (lo >= 30f - 1e-4f && hi <= 60f + 1e-4f)
                          || (lo >= -1e-4f && hi <= 30f + 1e-4f);
                Assert.IsTrue(banded, "triangle " + (i / 3) + " spans " + lo + ".." + hi + " and crosses a cut");
            }

            Assert.AreEqual(TotalArea(tris), TotalArea(second), 1e-2f);
        }

        [Test]
        public void Lerp_BlendsEveryChannel()
        {
            var a = UIVertex.simpleVert;
            a.position = new Vector3(0f, 0f, 0f);
            a.normal = new Vector3(1f, 0f, 0f);
            a.tangent = new Vector4(0f, 1f, 0f, 1f);
            a.color = new Color32(0, 0, 0, 0);
            a.uv0 = Vector4.zero;
            a.uv1 = Vector4.zero;
            a.uv2 = Vector4.zero;
            a.uv3 = Vector4.zero;

            var b = UIVertex.simpleVert;
            b.position = new Vector3(10f, 20f, 30f);
            b.normal = new Vector3(0f, 1f, 0f);
            b.tangent = new Vector4(1f, 0f, 0f, -1f);
            b.color = new Color32(255, 100, 50, 200);
            b.uv0 = new Vector4(1f, 2f, 3f, 4f);
            b.uv1 = new Vector4(4f, 3f, 2f, 1f);
            b.uv2 = new Vector4(1f, 1f, 1f, 1f);
            b.uv3 = new Vector4(2f, 2f, 2f, 2f);

            var m = MeshSlicer.Lerp(a, b, 0.5f);

            Assert.AreEqual(new Vector3(5f, 10f, 15f), m.position);
            Assert.AreEqual(0.5f, m.normal.x, 1e-4f);
            Assert.AreEqual(0f, m.tangent.w, 1e-4f);
            Assert.AreEqual(127, m.color.r, 2);
            Assert.AreEqual(100, m.color.a, 2);
            Assert.AreEqual(new Vector4(0.5f, 1f, 1.5f, 2f), m.uv0);
            Assert.AreEqual(new Vector4(2f, 1.5f, 1f, 0.5f), m.uv1);
            Assert.AreEqual(new Vector4(0.5f, 0.5f, 0.5f, 0.5f), m.uv2);
            Assert.AreEqual(new Vector4(1f, 1f, 1f, 1f), m.uv3);
        }
    }
}
