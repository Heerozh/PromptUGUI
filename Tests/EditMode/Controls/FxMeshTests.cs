using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The geometry half of blur / glow (spec 2026-09-02 §4.2). Pure vertex maths on the four
    /// vertices <c>Image</c> generates for a Simple sprite: push each corner outward by the radius,
    /// extrapolate uv0 by the matching amount, and record what the fragment needs to clip its taps
    /// to THIS sprite (uv1 = the sprite's UV rect) and to size a pixel radius (uv2 = uv per unit).
    /// </summary>
    public class FxMeshTests
    {
        // A 60x40 quad mapped onto a 0.25 x 0.25 patch of an atlas — deliberately non-square and
        // deliberately not at the origin, so a mistake in either axis or a forgotten offset shows.
        private const float W = 60f;
        private const float H = 40f;
        private const float U0 = 0.25f;
        private const float U1 = 0.5f;
        private const float V0 = 0.5f;
        private const float V1 = 0.75f;

        private static readonly Color32 White = new Color32(255, 255, 255, 255);

        /// <summary>Per-unit uv scale of the fixture: what one canvas unit is worth in uv.</summary>
        private static readonly Vector2 PerUnit = new Vector2((U1 - U0) / W, (V1 - V0) / H);

        private static VertexHelper BuildQuad(bool mirrorU = false)
        {
            var uLeft = mirrorU ? U1 : U0;
            var uRight = mirrorU ? U0 : U1;

            var vh = new VertexHelper();
            AddVert(vh, 0f, 0f, uLeft, V0);
            AddVert(vh, W, 0f, uRight, V0);
            AddVert(vh, W, H, uRight, V1);
            AddVert(vh, 0f, H, uLeft, V1);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
            return vh;
        }

        private static void AddVert(VertexHelper vh, float x, float y, float u, float v)
        {
            var vert = UIVertex.simpleVert;
            vert.position = new Vector3(x, y, 0f);
            vert.color = White;
            vert.uv0 = new Vector4(u, v, 0f, 0f);
            vh.AddVert(vert);
        }

        private static UIVertex Vert(VertexHelper vh, int i)
        {
            var v = new UIVertex();
            vh.PopulateUIVertex(ref v, i);
            return v;
        }

        private static Rect PositionBounds(VertexHelper vh)
        {
            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                var p = Vert(vh, i).position;
                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxX = Mathf.Max(maxX, p.x);
                maxY = Mathf.Max(maxY, p.y);
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        private static Rect UvBounds(VertexHelper vh)
        {
            float minU = float.MaxValue, minV = float.MaxValue;
            float maxU = float.MinValue, maxV = float.MinValue;
            for (var i = 0; i < vh.currentVertCount; i++)
            {
                var uv = Vert(vh, i).uv0;
                minU = Mathf.Min(minU, uv.x);
                minV = Mathf.Min(minV, uv.y);
                maxU = Mathf.Max(maxU, uv.x);
                maxV = Mathf.Max(maxV, uv.y);
            }
            return Rect.MinMaxRect(minU, minV, maxU, maxV);
        }

        [Test]
        public void Inflate_pushes_every_corner_outward_by_the_radius()
        {
            using var vh = BuildQuad();

            Assert.IsTrue(FxMesh.Inflate(vh, 6f));

            var b = PositionBounds(vh);
            Assert.AreEqual(-6f, b.xMin, 1e-4f);
            Assert.AreEqual(-6f, b.yMin, 1e-4f);
            Assert.AreEqual(W + 6f, b.xMax, 1e-4f);
            Assert.AreEqual(H + 6f, b.yMax, 1e-4f);

            // Each vertex stays in ITS own corner — a bounds check alone would pass on a mesh whose
            // corners had been shuffled.
            Assert.AreEqual(new Vector3(-6f, -6f, 0f), Vert(vh, 0).position);
            Assert.AreEqual(new Vector3(W + 6f, -6f, 0f), Vert(vh, 1).position);
            Assert.AreEqual(new Vector3(W + 6f, H + 6f, 0f), Vert(vh, 2).position);
            Assert.AreEqual(new Vector3(-6f, H + 6f, 0f), Vert(vh, 3).position);
        }

        [Test]
        public void Inflate_extrapolates_uv0_by_the_same_distance()
        {
            using var vh = BuildQuad();

            FxMesh.Inflate(vh, 6f);

            // 6 canvas units past the edge, measured in uv: the sample the fragment takes there is
            // exactly the texel 6 units outside the sprite (which the shader then reads as empty).
            var uv = UvBounds(vh);
            Assert.AreEqual(U0 - 6f * PerUnit.x, uv.xMin, 1e-5f);
            Assert.AreEqual(V0 - 6f * PerUnit.y, uv.yMin, 1e-5f);
            Assert.AreEqual(U1 + 6f * PerUnit.x, uv.xMax, 1e-5f);
            Assert.AreEqual(V1 + 6f * PerUnit.y, uv.yMax, 1e-5f);

            var v0 = Vert(vh, 0).uv0;
            Assert.AreEqual(U0 - 6f * PerUnit.x, v0.x, 1e-5f);
            Assert.AreEqual(V0 - 6f * PerUnit.y, v0.y, 1e-5f);
        }

        [Test]
        public void Inflate_records_the_sprite_rect_in_uv1_and_the_uv_scale_in_uv2()
        {
            using var vh = BuildQuad();

            FxMesh.Inflate(vh, 6f);

            for (var i = 0; i < vh.currentVertCount; i++)
            {
                var v = Vert(vh, i);
                Assert.AreEqual(U0, v.uv1.x, 1e-5f, $"vertex {i} uv1.x");
                Assert.AreEqual(V0, v.uv1.y, 1e-5f, $"vertex {i} uv1.y");
                Assert.AreEqual(U1, v.uv1.z, 1e-5f, $"vertex {i} uv1.z");
                Assert.AreEqual(V1, v.uv1.w, 1e-5f, $"vertex {i} uv1.w");

                Assert.AreEqual(PerUnit.x, v.uv2.x, 1e-6f, $"vertex {i} uv2.x");
                Assert.AreEqual(PerUnit.y, v.uv2.y, 1e-6f, $"vertex {i} uv2.y");
            }
        }

        [Test]
        public void Zero_pad_writes_the_channels_but_moves_nothing()
        {
            using var vh = BuildQuad();

            Assert.IsTrue(FxMesh.Inflate(vh, 0f));

            var b = PositionBounds(vh);
            Assert.AreEqual(0f, b.xMin, 1e-5f);
            Assert.AreEqual(0f, b.yMin, 1e-5f);
            Assert.AreEqual(W, b.xMax, 1e-5f);
            Assert.AreEqual(H, b.yMax, 1e-5f);

            var uv = UvBounds(vh);
            Assert.AreEqual(U0, uv.xMin, 1e-6f);
            Assert.AreEqual(U1, uv.xMax, 1e-6f);

            Assert.AreEqual(U0, Vert(vh, 0).uv1.x, 1e-5f, "the rect is still recorded");
        }

        [Test]
        public void A_mesh_that_is_not_one_quad_is_left_alone()
        {
            using var vh = BuildQuad();
            AddVert(vh, W * 0.5f, H * 2f, U0, V1);   // a fifth vertex: Sliced / Tiled / sprite mesh

            Assert.IsFalse(FxMesh.Inflate(vh, 6f));

            var b = PositionBounds(vh);
            Assert.AreEqual(0f, b.xMin, 1e-5f, "positions must be untouched");
            Assert.AreEqual(Vector4.zero, Vert(vh, 0).uv1, "and so must the extra channels");
        }

        [Test]
        public void A_degenerate_quad_is_left_alone()
        {
            var vh = new VertexHelper();
            AddVert(vh, 0f, 0f, U0, V0);
            AddVert(vh, 0f, 0f, U1, V0);
            AddVert(vh, 0f, H, U1, V1);
            AddVert(vh, 0f, H, U0, V1);
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);

            using (vh)
            {
                // Zero width: the uv-per-unit scale would be a division by zero.
                Assert.IsFalse(FxMesh.Inflate(vh, 6f));
                Assert.AreEqual(Vector4.zero, Vert(vh, 0).uv2);
            }
        }

        [Test]
        public void Mirrored_uvs_still_extrapolate_outward()
        {
            // flip="x" art (or any sprite whose uv runs against its position) must not have its uv
            // pulled INTO the sprite on one side — each vertex is pushed away from the uv centre,
            // not along the position axis.
            using var vh = BuildQuad(mirrorU: true);

            FxMesh.Inflate(vh, 6f);

            var uv = UvBounds(vh);
            Assert.AreEqual(U0 - 6f * PerUnit.x, uv.xMin, 1e-5f);
            Assert.AreEqual(U1 + 6f * PerUnit.x, uv.xMax, 1e-5f);
            Assert.AreEqual(U0, Vert(vh, 0).uv1.x, 1e-5f, "uv1 is the ORIGINAL rect, always min-first");
            Assert.AreEqual(U1, Vert(vh, 0).uv1.z, 1e-5f);
            Assert.AreEqual(PerUnit.x, Vert(vh, 0).uv2.x, 1e-6f, "the scale is a magnitude, never negative");
        }
    }
}
