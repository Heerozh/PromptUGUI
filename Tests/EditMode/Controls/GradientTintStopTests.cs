using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Gradient stops and hints on the vertex path (spec 2026-09-01 VGS). The plain two-colour ramp
    /// keeps its old code path untouched; a shaped ramp slices the mesh at the stops instead.
    /// </summary>
    public class GradientTintStopTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp() => _go = new GameObject("GradientTintStopTest", typeof(Image));

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        /// <summary>A quad spanning y 0..100 — with triangles, unlike the tint tests' bare vertex
        /// list, because the slicing path works on the triangle stream.</summary>
        private static VertexHelper BuildWhiteQuad()
        {
            var vh = new VertexHelper();
            vh.AddVert(new Vector3(0f, 0f, 0f), White, new Vector4(0f, 0f, 0f, 0f));
            vh.AddVert(new Vector3(100f, 0f, 0f), White, new Vector4(1f, 0f, 0f, 0f));
            vh.AddVert(new Vector3(100f, 100f, 0f), White, new Vector4(1f, 1f, 0f, 0f));
            vh.AddVert(new Vector3(0f, 100f, 0f), White, new Vector4(0f, 1f, 0f, 0f));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
            return vh;
        }

        /// <summary>An n×n grid of quads over the same 0..100 box — a stand-in for Sliced / Tiled
        /// meshes, where the stop lands inside some cells and misses others entirely.</summary>
        private static VertexHelper BuildGrid(int n)
        {
            var vh = new VertexHelper();
            var step = 100f / n;
            for (var gy = 0; gy < n; gy++)
                for (var gx = 0; gx < n; gx++)
                {
                    var x0 = gx * step;
                    var y0 = gy * step;
                    var i = vh.currentVertCount;
                    vh.AddVert(new Vector3(x0, y0, 0f), White, Vector4.zero);
                    vh.AddVert(new Vector3(x0 + step, y0, 0f), White, Vector4.zero);
                    vh.AddVert(new Vector3(x0 + step, y0 + step, 0f), White, Vector4.zero);
                    vh.AddVert(new Vector3(x0, y0 + step, 0f), White, Vector4.zero);
                    vh.AddTriangle(i, i + 1, i + 2);
                    vh.AddTriangle(i + 2, i + 3, i);
                }
            return vh;
        }

        private static List<UIVertex> Read(VertexHelper vh)
        {
            var list = new List<UIVertex>();
            vh.GetUIVertexStream(list);
            return list;
        }

        private static void AssertColorApprox(Color32 expected, Color32 actual, string what, int tol = 3)
        {
            Assert.That(Mathf.Abs(expected.r - actual.r), Is.LessThanOrEqualTo(tol), what + " R");
            Assert.That(Mathf.Abs(expected.g - actual.g), Is.LessThanOrEqualTo(tol), what + " G");
            Assert.That(Mathf.Abs(expected.b - actual.b), Is.LessThanOrEqualTo(tol), what + " B");
        }

        private static bool HasVertexAt(List<UIVertex> verts, float y)
            => verts.Exists(v => Mathf.Abs(v.position.y - y) < 1e-3f);

        /// <summary>Total area of a triangle stream — how much of the quad is still being drawn.
        /// Slicing a quad across its diagonal yields three triangles on one side and one on the
        /// other, so counting vertices would pin down the triangulation rather than the picture.</summary>
        private static float Area(List<UIVertex> tris)
        {
            var sum = 0f;
            for (var i = 0; i + 2 < tris.Count; i += 3)
            {
                var a = tris[i].position;
                var b = tris[i + 1].position;
                var c = tris[i + 2].position;
                sum += Mathf.Abs((b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x)) * 0.5f;
            }
            return sum;
        }

        private static readonly Color32 Red = new Color32(255, 0, 0, 255);
        private static readonly Color32 Blue = new Color32(0, 0, 255, 255);

        // ── plumbing ─────────────────────────────────────────────────────────────

        [Test]
        public void Set_Spec_RoundTrips()
        {
            var fx = _go.AddComponent<GradientTint>();
            var spec = ColorSpec.Gradient(Color.red, Color.blue, 0.3f, 0.6f, 2f);

            fx.Set(spec);

            Assert.AreEqual(Color.red, fx.Spec.Top);
            Assert.AreEqual(Color.blue, fx.Spec.Bottom);
            Assert.AreEqual(0.3f, fx.Spec.TopStop, 1e-5f);
            Assert.AreEqual(0.6f, fx.Spec.BottomStop, 1e-5f);
            Assert.AreEqual(2f, fx.Spec.Curve, 1e-5f);
            Assert.IsTrue(fx.Spec.HasStops);
        }

        [Test]
        public void Set_ColorPair_KeepsTheConvenienceOverload()
        {
            var fx = _go.AddComponent<GradientTint>();

            fx.Set(Color.red, Color.blue);

            Assert.AreEqual(Color.red, fx.Top);
            Assert.AreEqual(Color.blue, fx.Bottom);
            Assert.IsTrue(fx.Spec.IsGradient);
            Assert.IsFalse(fx.Spec.HasStops, "a plain pair must stay on the untouched code path");
        }

        [Test]
        public void Set_ReplacingAShapedRampWithAPlainOne_ForgetsTheStops()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.red, Color.blue, 0.3f, 0.6f, 2f));

            fx.Set(Color.red, Color.blue);

            Assert.IsFalse(fx.Spec.HasStops, "stale stops would keep slicing a mesh that no longer needs it");
        }

        // ── geometry ─────────────────────────────────────────────────────────────

        [Test]
        public void Plain_NoStops_LeavesTheMeshIndexed()
        {
            // VGS-D1: without stops nothing is de-indexed, so every existing document keeps the
            // exact vertex count, batching and colours it has today.
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(Color.red, Color.blue);

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);

            Assert.AreEqual(4, vh.currentVertCount, "a plain ramp must not touch the geometry");
        }

        [Test]
        public void Slicing_carries_the_fx_channels_through_untouched()
        {
            // blur / glow put the sprite's atlas rect in uv1 and its uv scale in uv2, the same for
            // every vertex of the quad (spec 2026-09-02 §4.1). A cut here introduces NEW vertices by
            // interpolating along an edge, so the two channels only survive if MeshSlicer.Lerp
            // carries them — and a vertex that lost them tells the shader "no rect", which silently
            // drops the effect on any icon that also has a shaped gradient.
            var rect = new Vector4(0.1f, 0.2f, 0.3f, 0.4f);
            var perUnit = new Vector4(0.5f, 0.6f, 0f, 0f);

            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.red, Color.blue, 0.3f, 0.6f));

            using var vh = BuildWhiteQuad();
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                var v = new UIVertex();
                vh.PopulateUIVertex(ref v, i);
                v.uv1 = rect;
                v.uv2 = perUnit;
                vh.SetUIVertex(v, i);
            }

            fx.ModifyMesh(vh);

            var verts = Read(vh);
            Assert.Greater(verts.Count, 6, "前置：a shaped ramp really did cut the mesh");
            foreach (var v in verts)
            {
                Assert.AreEqual(rect, v.uv1, $"uv1 at y={v.position.y}");
                Assert.AreEqual(perUnit, v.uv2, $"uv2 at y={v.position.y}");
            }
        }

        [Test]
        public void Stop_Bottom50_CutsAtTheMidline()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.red, Color.blue, 0f, 0.5f));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            Assert.IsTrue(HasVertexAt(verts, 50f), "the mesh has to be cut where the bottom stop sits");
            foreach (var v in verts)
            {
                if (v.position.y > 99f) AssertColorApprox(Red, v.color, "top edge");
                if (v.position.y < 51f) AssertColorApprox(Blue, v.color, "at and below the stop");
            }
        }

        [Test]
        public void Stops_Band_30_60_IsFlatOutsideTheBand()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.red, Color.blue, 0.3f, 0.6f));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            // Stops are measured from the TOP edge: 30% → y = 70, 60% → y = 40.
            Assert.IsTrue(HasVertexAt(verts, 70f), "cut at the top stop");
            Assert.IsTrue(HasVertexAt(verts, 40f), "cut at the bottom stop");
            foreach (var v in verts)
            {
                if (v.position.y >= 70f - 1e-3f) AssertColorApprox(Red, v.color, "above the band");
                if (v.position.y <= 40f + 1e-3f) AssertColorApprox(Blue, v.color, "below the band");
            }
        }

        [Test]
        public void HardEdge_BothStopsTogether_KeepsBothColoursOnTheLine()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.red, Color.blue, 0.5f, 0.5f));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            var redOnTheLine = false;
            var blueOnTheLine = false;
            foreach (var v in verts)
            {
                if (Mathf.Abs(v.position.y - 50f) > 1e-3f) continue;
                if (v.color.r > 200) redOnTheLine = true;
                if (v.color.b > 200) blueOnTheLine = true;
            }

            Assert.IsTrue(redOnTheLine && blueOnTheLine,
                "a hard edge needs the line to carry both colours — one per side — or it fades");
        }

        [Test]
        public void Hint_AddsStripsAndTracksTheCurve()
        {
            var curve = ColorParser.StopCurveExponent(0f, 1f, 0.3f);
            var spec = ColorSpec.Gradient(Color.white, Color.black, 0f, 1f, curve);
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(spec);

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            var rows = new HashSet<int>();
            foreach (var v in verts) rows.Add(Mathf.RoundToInt(v.position.y * 10f));
            Assert.AreEqual(9, rows.Count, "eight strips means nine rows of vertices");

            // The strip boundary at s = 0.25 (y = 75) carries the curve's own value...
            var atQuarter = verts.Find(v => Mathf.Abs(v.position.y - 75f) < 1e-3f);
            AssertColorApprox((Color32)spec.Evaluate(0.25f), atQuarter.color, "strip boundary");

            // ...and the chord to the next boundary (s = 0.375, y = 62.5) still passes within a hair
            // of the hint, which is what makes eight strips enough to stand in for a curve the
            // fragment shader draws exactly.
            var atNext = verts.Find(v => Mathf.Abs(v.position.y - 62.5f) < 1e-3f);
            var alongStrip = (0.3f - 0.25f) / 0.125f;
            var chord = Mathf.Lerp(atQuarter.color.r / 255f, atNext.color.r / 255f, alongStrip);
            Assert.AreEqual(0.5f, chord, 0.03f, "half and half at the hint");
        }

        [Test]
        public void TransparentBottom_DropsTheTailGeometry()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.white, new Color(1f, 1f, 1f, 0f), 0f, 0.5f));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            Assert.AreEqual(5000f, Area(verts), 1f, "the fully transparent half is dropped as geometry, not just alpha");
            foreach (var v in verts)
                Assert.GreaterOrEqual(v.position.y, 50f - 1e-3f, "nothing below the stop survives");
        }

        [Test]
        public void TransparentTop_DropsTheHeadGeometry()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(new Color(1f, 1f, 1f, 0f), Color.white, 0.5f, 1f));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            Assert.AreEqual(5000f, Area(verts), 1f);
            foreach (var v in verts)
                Assert.LessOrEqual(v.position.y, 50f + 1e-3f, "nothing above the stop survives");
        }

        [Test]
        public void Grid_StopLandsInsideOneRowOfCells()
        {
            // Sliced and Tiled images arrive as many quads; the stop crosses some and misses others.
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.red, Color.blue, 0f, 0.5f));

            using var vh = BuildGrid(3);
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            Assert.IsTrue(HasVertexAt(verts, 50f), "the crossed row of cells is cut");
            foreach (var v in verts)
            {
                if (v.position.y > 99f) AssertColorApprox(Red, v.color, "top edge");
                if (v.position.y < 51f) AssertColorApprox(Blue, v.color, "at and below the stop");
            }
        }
    }
}
