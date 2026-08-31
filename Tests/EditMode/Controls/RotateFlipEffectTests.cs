using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The vertex maths of <c>rotation</c> / <c>flip</c> (spec
    /// 2026-08-31-hug-reveal-flip-checked-design §3.4), driven straight through
    /// <c>ModifyMesh</c> on a 2×2 quad centred on the origin: corners are (±1, ±1), so every
    /// expected value can be read off by hand.
    /// </summary>
    public class RotateFlipEffectTests
    {
        private GameObject _go;

        [TearDown]
        public void TearDown()
        {
            if (_go != null) Object.DestroyImmediate(_go);
            _go = null;
        }

        private RotateFlipEffect MakeEffect(float size = 2f)
        {
            _go = new GameObject("g", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)_go.transform;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = new Vector2(size, size);
            return _go.AddComponent<RotateFlipEffect>();
        }

        // The four corners in the order uGUI's Image emits them: bottom-left, top-left, top-right,
        // bottom-right — with a distinct uv per corner so a mirrored quad is distinguishable from a
        // re-ordered one.
        private static readonly Vector2[] Corners =
        {
            new Vector2(-1f, -1f), new Vector2(-1f, 1f), new Vector2(1f, 1f), new Vector2(1f, -1f),
        };

        private static VertexHelper Quad()
        {
            var vh = new VertexHelper();
            for (var i = 0; i < 4; i++)
            {
                var v = UIVertex.simpleVert;
                v.position = Corners[i];
                v.uv0 = new Vector4((Corners[i].x + 1f) * 0.5f, (Corners[i].y + 1f) * 0.5f, 0f, 0f);
                vh.AddVert(v);
            }
            vh.AddTriangle(0, 1, 2);
            vh.AddTriangle(2, 3, 0);
            return vh;
        }

        private static (Vector2 pos, Vector2 uv) Read(VertexHelper vh, int index)
        {
            var v = new UIVertex();
            vh.PopulateUIVertex(ref v, index);
            return (v.position, new Vector2(v.uv0.x, v.uv0.y));
        }

        private static void AssertPos(VertexHelper vh, int index, float x, float y, string because = null)
        {
            var (pos, _) = Read(vh, index);
            Assert.AreEqual(x, pos.x, 0.001f, because ?? $"vertex {index}.x");
            Assert.AreEqual(y, pos.y, 0.001f, because ?? $"vertex {index}.y");
        }

        [Test]
        public void Identity_leaves_every_vertex_alone()
        {
            var fx = MakeEffect();
            var vh = Quad();

            fx.ModifyMesh(vh);

            for (var i = 0; i < 4; i++)
                AssertPos(vh, i, Corners[i].x, Corners[i].y);
            Assert.IsTrue(fx.IsIdentity);
        }

        [Test]
        public void Rotation_90_turns_clockwise()
        {
            var fx = MakeEffect();
            fx.Rotation = 90f;
            var vh = Quad();

            fx.ModifyMesh(vh);

            // top-right (1,1) → bottom-right (1,-1) when the picture turns clockwise.
            AssertPos(vh, 2, 1f, -1f, "top-right goes to bottom-right");
            AssertPos(vh, 1, 1f, 1f, "top-left goes to top-right");
        }

        [Test]
        public void Rotation_carries_the_uv_with_the_vertex()
        {
            var fx = MakeEffect();
            fx.Rotation = 90f;
            var vh = Quad();

            fx.ModifyMesh(vh);

            var (_, uv) = Read(vh, 2);
            Assert.AreEqual(1f, uv.x, 0.001f, "the uv stays attached to its vertex — the picture turns with the quad");
            Assert.AreEqual(1f, uv.y, 0.001f);
        }

        [Test]
        public void FlipX_mirrors_horizontally()
        {
            var fx = MakeEffect();
            fx.FlipX = true;
            var vh = Quad();

            fx.ModifyMesh(vh);

            AssertPos(vh, 2, -1f, 1f, "top-right lands on top-left");
            AssertPos(vh, 1, 1f, 1f, "top-left lands on top-right");
        }

        [Test]
        public void FlipY_mirrors_vertically()
        {
            var fx = MakeEffect();
            fx.FlipY = true;
            var vh = Quad();

            fx.ModifyMesh(vh);

            AssertPos(vh, 2, 1f, -1f);
            AssertPos(vh, 0, -1f, 1f);
        }

        [Test]
        public void FlipXY_equals_a_half_turn()
        {
            var both = MakeEffect();
            both.FlipX = true;
            both.FlipY = true;
            var vhFlip = Quad();
            both.ModifyMesh(vhFlip);

            Object.DestroyImmediate(_go);
            var turned = MakeEffect();
            turned.Rotation = 180f;
            var vhTurn = Quad();
            turned.ModifyMesh(vhTurn);

            for (var i = 0; i < 4; i++)
            {
                var (flipped, _) = Read(vhFlip, i);
                var (rotated, _) = Read(vhTurn, i);
                Assert.AreEqual(rotated.x, flipped.x, 0.001f, $"vertex {i}.x");
                Assert.AreEqual(rotated.y, flipped.y, 0.001f, $"vertex {i}.y");
            }
        }

        [Test]
        public void Rotation_is_about_the_rect_center_not_the_origin()
        {
            // pivot bottom-left → the rect runs (0,0)..(2,2) and its centre is (1,1).
            _go = new GameObject("g", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            var rt = (RectTransform)_go.transform;
            rt.pivot = Vector2.zero;
            rt.sizeDelta = new Vector2(2f, 2f);
            var fx = _go.AddComponent<RotateFlipEffect>();
            fx.Rotation = 180f;

            var vh = new VertexHelper();
            var v = UIVertex.simpleVert;
            v.position = new Vector2(0f, 0f);
            vh.AddVert(v);
            v.position = new Vector2(2f, 2f);
            vh.AddVert(v);
            v.position = new Vector2(2f, 0f);
            vh.AddVert(v);
            vh.AddTriangle(0, 1, 2);

            fx.ModifyMesh(vh);

            AssertPos(vh, 0, 2f, 2f, "a half turn about (1,1) swaps the two far corners");
            AssertPos(vh, 1, 0f, 0f);
        }

        [TestCase(-90f, 270f)]
        [TestCase(450f, 90f)]
        [TestCase(360f, 0f)]
        public void Rotation_is_normalised(float written, float expected)
        {
            var fx = MakeEffect();
            fx.Rotation = written;

            Assert.AreEqual(expected, fx.Rotation, 0.001f);
        }

        [Test]
        public void Identity_is_only_identity_when_nothing_is_set()
        {
            var fx = MakeEffect();
            Assert.IsTrue(fx.IsIdentity);

            fx.Rotation = 90f;
            Assert.IsFalse(fx.IsIdentity);

            fx.Rotation = 0f;
            fx.FlipY = true;
            Assert.IsFalse(fx.IsIdentity);
        }
    }
}
