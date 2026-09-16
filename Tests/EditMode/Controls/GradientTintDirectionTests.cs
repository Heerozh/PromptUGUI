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
    /// Directions and three-plus stops on the vertex path (spec 2026-09-17 §6.3). A plain
    /// two-colour ramp in any direction is linear in the projection, so it stays per-vertex; a
    /// shaped ramp cuts the mesh along lines perpendicular to the direction, one per stop, and
    /// pins the new vertices' projection to the cut exactly.
    /// </summary>
    public class GradientTintDirectionTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp() => _go = new GameObject("GradientTintDirectionTest", typeof(Image));

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_go);

        private static readonly Color32 White = new Color32(255, 255, 255, 255);
        private static readonly Color32 Red = new Color32(255, 0, 0, 255);
        private static readonly Color32 Green = new Color32(0, 255, 0, 255);
        private static readonly Color32 Blue = new Color32(0, 0, 255, 255);

        /// <summary>A quad spanning x 0..W, y 0..H with triangles (the slicing path works on the stream).</summary>
        private static VertexHelper BuildWhiteQuad(float w = 100f, float h = 100f)
        {
            var vh = new VertexHelper();
            vh.AddVert(new Vector3(0f, 0f, 0f), White, new Vector4(0f, 0f, 0f, 0f));
            vh.AddVert(new Vector3(w, 0f, 0f), White, new Vector4(1f, 0f, 0f, 0f));
            vh.AddVert(new Vector3(w, h, 0f), White, new Vector4(1f, 1f, 0f, 0f));
            vh.AddVert(new Vector3(0f, h, 0f), White, new Vector4(0f, 1f, 0f, 0f));
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
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

        private static bool HasVertexAtX(List<UIVertex> verts, float x)
            => verts.Exists(v => Mathf.Abs(v.position.x - x) < 1e-3f);

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

        private static ColorSpec Three(GradientDirection dir, float mid = 0.5f)
            => ColorSpec.Gradient(dir, new[] { Color.red, Color.green, Color.blue }, new[] { 0f, mid, 1f }, new[] { 1f, 1f });

        // ── plain two-colour ramp in a direction: per vertex, no cut ────────────

        [Test]
        public void ToRight_TwoColours_ColoursTheCornersByX_NoSlicing()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.red, Color.blue).WithDirection(GradientDirection.Angle(90f)));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);

            Assert.AreEqual(4, vh.currentVertCount, "a plain ramp is linear along x too: no de-indexing");
            var v = new UIVertex();
            for (var i = 0; i < 4; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                if (v.position.x < 1f) AssertColorApprox(Red, v.color, "left edge");
                if (v.position.x > 99f) AssertColorApprox(Blue, v.color, "right edge");
            }
        }

        [Test]
        public void ToTop_TwoColours_IsTheDefaultFlipped()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.red, Color.blue).WithDirection(GradientDirection.Angle(0f)));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var v = new UIVertex();
            for (var i = 0; i < 4; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                if (v.position.y < 1f) AssertColorApprox(Red, v.color, "bottom edge is the start");
                if (v.position.y > 99f) AssertColorApprox(Blue, v.color, "top edge is the end");
            }
        }

        [Test]
        public void Corner_TwoColours_OnAWideQuad_PutsTheOtherCornersAtTheMidColour()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.red, Color.blue).WithDirection(GradientDirection.Corner(1, -1)));

            using var vh = BuildWhiteQuad(200f, 100f);
            fx.ModifyMesh(vh);
            var v = new UIVertex();
            var mid = new Color32(128, 0, 128, 255);
            for (var i = 0; i < 4; i++)
            {
                vh.PopulateUIVertex(ref v, i);
                var left = v.position.x < 1f;
                var top = v.position.y > 99f;
                if (left && top) AssertColorApprox(Red, v.color, "top-left is the start");
                else if (!left && !top) AssertColorApprox(Blue, v.color, "bottom-right is the end");
                else AssertColorApprox(mid, v.color, "the other two corners sit on the 50% line");
            }
        }

        // ── stops along a direction: cuts perpendicular to it ───────────────────

        [Test]
        public void ToRight_ThreeStops_CutsTwiceAlongX()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(Three(GradientDirection.Angle(90f)));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            Assert.IsTrue(HasVertexAtX(verts, 50f), "cut at the middle stop");
            foreach (var v in verts)
            {
                if (v.position.x < 1f) AssertColorApprox(Red, v.color, "left edge");
                if (Mathf.Abs(v.position.x - 50f) < 1e-3f) AssertColorApprox(Green, v.color, "on the middle stop");
                if (v.position.x > 99f) AssertColorApprox(Blue, v.color, "right edge");
            }
            Assert.AreEqual(10000f, Area(verts), 1f, "nothing dropped");
        }

        [Test]
        public void ToRight_ThreeStops_MovedMiddle_CutsWhereItSits()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(Three(GradientDirection.Angle(90f), 0.3f));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            Assert.IsTrue(HasVertexAtX(verts, 30f), "cut at 30%");
            foreach (var v in verts)
                if (Mathf.Abs(v.position.x - 30f) < 1e-3f) AssertColorApprox(Green, v.color, "on the moved stop");
        }

        [Test]
        public void Diagonal_Stop_CutIsPinnedToTheProjection()
        {
            // 45° on a square: the middle stop's line is the anti-diagonal x + y = 100... in the
            // direction (sin45, cos45) the projection of (x, y) is (x + y)/√2, and s = 0.5 is the
            // centre — every new vertex must project EXACTLY there, not a hair off.
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(Three(GradientDirection.Angle(45f)));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            var dir = new Vector2(1f, 1f).normalized;
            var onCut = 0;
            foreach (var v in verts)
            {
                var proj = Vector2.Dot(new Vector2(v.position.x - 50f, v.position.y - 50f), dir);
                if (Mathf.Abs(proj) < 1e-3f)
                {
                    onCut++;
                    AssertColorApprox(Green, v.color, "on the diagonal cut");
                }
            }
            Assert.GreaterOrEqual(onCut, 2, "the cut through the centre produced vertices on it");
            Assert.AreEqual(10000f, Area(verts), 1f);
        }

        [Test]
        public void FourStops_ThreeCuts()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(GradientDirection.Angle(90f),
                                      new[] { Color.red, Color.green, Color.blue, Color.white },
                                      new[] { 0f, 0.25f, 0.5f, 0.75f }, new[] { 1f, 1f, 1f }));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            Assert.IsTrue(HasVertexAtX(verts, 25f));
            Assert.IsTrue(HasVertexAtX(verts, 50f));
            Assert.IsTrue(HasVertexAtX(verts, 75f));
            foreach (var v in verts)
                if (v.position.x > 75f - 1e-3f) AssertColorApprox(White, v.color, "flat after the last stop");
        }

        // ── transparent ends cull along the projection ──────────────────────────

        [Test]
        public void TransparentEnd_ToRight_DropsTheRightHalf()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(Color.white, new Color(1f, 1f, 1f, 0f), 0f, 0.5f)
                       .WithDirection(GradientDirection.Angle(90f)));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            var verts = Read(vh);

            Assert.AreEqual(5000f, Area(verts), 1f, "the transparent half is dropped as geometry");
            foreach (var v in verts)
                Assert.LessOrEqual(v.position.x, 50f + 1e-3f, "nothing right of the stop survives");
        }

        [Test]
        public void TransparentMiddleStop_DropsNothing()
        {
            // Only the two ENDS cull: a see-through middle is a seam inside the picture, not an end.
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(ColorSpec.Gradient(GradientDirection.Angle(90f),
                                      new[] { Color.red, new Color(0f, 1f, 0f, 0f), Color.blue },
                                      new[] { 0f, 0.5f, 1f }, new[] { 1f, 1f }));

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            Assert.AreEqual(10000f, Area(Read(vh)), 1f);
        }

        // ── the evaluator and the shader stay in step ───────────────────────────

        [Test]
        public void Evaluate_MatchesWhatTheVerticesGot()
        {
            var spec = ColorSpec.Gradient(GradientDirection.Angle(90f),
                                          new[] { Color.red, Color.green, Color.blue },
                                          new[] { 0.2f, 0.5f, 0.9f }, new[] { 1f, 2f });
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(spec);

            using var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);
            foreach (var v in Read(vh))
            {
                var s = v.position.x / 100f;
                var expected = (Color32)spec.Evaluate(s);
                AssertColorApprox(expected, v.color, $"vertex at x={v.position.x:0.##}", 4);
            }
        }
    }
}
