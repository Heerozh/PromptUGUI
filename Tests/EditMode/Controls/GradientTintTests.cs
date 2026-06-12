using NUnit.Framework;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class GradientTintTests
    {
        private GameObject _go;

        [SetUp]
        public void SetUp()
        {
            _go = new GameObject("GradientTintTest", typeof(Image));
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_go);
        }

        // Helper: compare two Color32 values within a byte tolerance.
        private static void AssertColorApprox(Color32 expected, Color32 actual, int tol = 3)
        {
            Assert.That(Mathf.Abs(expected.r - actual.r), Is.LessThanOrEqualTo(tol), $"R: expected {expected.r}, got {actual.r}");
            Assert.That(Mathf.Abs(expected.g - actual.g), Is.LessThanOrEqualTo(tol), $"G: expected {expected.g}, got {actual.g}");
            Assert.That(Mathf.Abs(expected.b - actual.b), Is.LessThanOrEqualTo(tol), $"B: expected {expected.b}, got {actual.b}");
        }

        // Build a VertexHelper with 4 white verts forming a quad at y=0 (bottom two) and y=100 (top two).
        private static VertexHelper BuildWhiteQuad()
        {
            var vh = new VertexHelper();
            var white = new Color32(255, 255, 255, 255);
            // Bottom-left, bottom-right
            vh.AddVert(new Vector3(0, 0, 0), white, new Vector4(0, 0, 0, 0));
            vh.AddVert(new Vector3(100, 0, 0), white, new Vector4(1, 0, 0, 0));
            // Top-right, top-left
            vh.AddVert(new Vector3(100, 100, 0), white, new Vector4(1, 1, 0, 0));
            vh.AddVert(new Vector3(0, 100, 0), white, new Vector4(0, 1, 0, 0));
            return vh;
        }

        [Test]
        public void TopAndBottom_PaintedAtExtremes()
        {
            var fx = _go.AddComponent<GradientTint>();
            // Set(top, bottom): top=red, bottom=blue
            fx.Set(Color.red, Color.blue);

            var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);

            // Verts 0 and 1 are at y=0 (bottom) → should be blue
            var v0 = default(UIVertex);
            var v1 = default(UIVertex);
            var v2 = default(UIVertex);
            var v3 = default(UIVertex);
            vh.PopulateUIVertex(ref v0, 0);
            vh.PopulateUIVertex(ref v1, 1);
            vh.PopulateUIVertex(ref v2, 2);
            vh.PopulateUIVertex(ref v3, 3);

            AssertColorApprox(new Color32(0, 0, 255, 255), v0.color, 3);   // bottom-left ≈ blue
            AssertColorApprox(new Color32(0, 0, 255, 255), v1.color, 3);   // bottom-right ≈ blue
            AssertColorApprox(new Color32(255, 0, 0, 255), v2.color, 3);   // top-right ≈ red
            AssertColorApprox(new Color32(255, 0, 0, 255), v3.color, 3);   // top-left ≈ red
        }

        [Test]
        public void Multiplies_IntoExistingVertexColor()
        {
            var fx = _go.AddComponent<GradientTint>();
            // Flat red gradient (both stops red) to prove multiply not replace
            fx.Set(Color.red, Color.red);

            var vh = new VertexHelper();
            var grey = new Color32(128, 128, 128, 255);
            // Build quad with grey verts at y=0 and y=100
            vh.AddVert(new Vector3(0, 0, 0), grey, new Vector4(0, 0, 0, 0));
            vh.AddVert(new Vector3(100, 0, 0), grey, new Vector4(1, 0, 0, 0));
            vh.AddVert(new Vector3(100, 100, 0), grey, new Vector4(1, 1, 0, 0));
            vh.AddVert(new Vector3(0, 100, 0), grey, new Vector4(0, 1, 0, 0));

            fx.ModifyMesh(vh);

            // grey × red = (128,0,0,255) — prove it's multiply not replace
            for (var i = 0; i < 4; i++)
            {
                var v = default(UIVertex);
                vh.PopulateUIVertex(ref v, i);
                // R ≈ 128 (128/255 * 255 ≈ 128), G ≈ 0, B ≈ 0
                AssertColorApprox(new Color32(128, 0, 0, 255), v.color, 3);
            }
        }

        [Test]
        public void Disabled_DoesNotModify()
        {
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(Color.red, Color.blue);
            fx.enabled = false;

            var vh = BuildWhiteQuad();
            fx.ModifyMesh(vh);

            // All verts should remain white
            for (var i = 0; i < 4; i++)
            {
                var v = default(UIVertex);
                vh.PopulateUIVertex(ref v, i);
                AssertColorApprox(new Color32(255, 255, 255, 255), v.color, 3);
            }
        }

        [Test]
        public void Set_SameValue_IsIdempotent()
        {
            // Driving graphic.SetVerticesDirty is hard to observe directly in EditMode;
            // assert the value-level idempotency: Set then Set-same leaves Top/Bottom equal.
            var fx = _go.AddComponent<GradientTint>();
            fx.Set(Color.red, Color.blue);
            fx.Set(Color.red, Color.blue);   // same values — early-return path
            Assert.AreEqual(Color.red, fx.Top);
            Assert.AreEqual(Color.blue, fx.Bottom);
        }

        [Test]
        public void MidVertex_HalfwayLerps()
        {
            var fx = _go.AddComponent<GradientTint>();
            // Set(top=red, bottom=blue): at y=50 out of [0,100] → Lerp(blue,red,0.5) = (0.5,0,0.5)
            fx.Set(Color.red, Color.blue);

            var vh = BuildWhiteQuad();
            // Add a 5th white vertex at y=50 (midpoint)
            vh.AddVert(new Vector3(50, 50, 0), new Color32(255, 255, 255, 255), new Vector4(0.5f, 0.5f, 0, 0));

            fx.ModifyMesh(vh);

            var mid = default(UIVertex);
            vh.PopulateUIVertex(ref mid, 4);
            // Lerp(blue, red, 0.5) = (0.5, 0, 0.5) → Color32 ≈ (127, 0, 127)
            AssertColorApprox(new Color32(127, 0, 127, 255), mid.color, 3);
        }
    }
}
