using NUnit.Framework;
using PromptUGUI.Application.Glass;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Application
{
    /// <summary>
    /// On an HDR display URP's post-processing hands the glass capture over already prepared for
    /// the display — rotated into its gamut and scaled to nits by the paper-white brightness —
    /// while the overlay UI the glass panel draws into gets that same treatment later, during
    /// compositing. Sampled as-is, the picture is transformed twice and every panel turns into a
    /// white slab. The capture undoes the transform once with one matrix; this is the matrix.
    ///
    /// No test can turn an HDR display on, so the forward transform is reproduced here from URP's
    /// own constants (HDROutput.hlsl / ColorSpaceUtils.cs) and the decode has to be its inverse.
    /// </summary>
    public class GlassBackdropDecodeTests
    {
        private static readonly Matrix4x4 Rec709ToRec2020 = Rows(
            0.627402f, 0.329292f, 0.043306f,
            0.069095f, 0.919544f, 0.011360f,
            0.016394f, 0.088028f, 0.895578f);

        private static readonly Matrix4x4 Rec709ToP3D65 = Rows(
            0.822462f, 0.177538f, 0.000000f,
            0.033194f, 0.966806f, 0.000000f,
            0.017083f, 0.072397f, 0.910520f);

        private static readonly Vector3[] Colors =
        {
            new(1f, 1f, 1f),
            new(1f, 0f, 0f),
            new(0f, 0f, 1f),
            new(0.85f, 0.42f, 0.10f),
            new(0.02f, 0.03f, 0.10f),
        };

        private static Matrix4x4 Rows(
            float m00, float m01, float m02,
            float m10, float m11, float m12,
            float m20, float m21, float m22)
        {
            var m = Matrix4x4.identity;
            m.m00 = m00; m.m01 = m01; m.m02 = m02;
            m.m10 = m10; m.m11 = m11; m.m12 = m12;
            m.m20 = m20; m.m21 = m21; m.m22 = m22;
            return m;
        }

        /// <summary>What LutBuilderHdr / UberPost leave in the camera colour buffer on an HDR display.</summary>
        private static Vector3 AsUrpLeavesIt(Vector3 rec709, Matrix4x4 toOutputSpace, float paperWhite)
            => toOutputSpace.MultiplyVector(rec709) * paperWhite;

        private static void AssertRoundTrips(Matrix4x4 decode, Matrix4x4 toOutputSpace, float paperWhite)
        {
            foreach (var c in Colors)
            {
                var back = decode.MultiplyVector(AsUrpLeavesIt(c, toOutputSpace, paperWhite));
                Assert.AreEqual(c.x, back.x, 1e-3f, $"red of {c} came back as {back}");
                Assert.AreEqual(c.y, back.y, 1e-3f, $"green of {c} came back as {back}");
                Assert.AreEqual(c.z, back.z, 1e-3f, $"blue of {c} came back as {back}");
            }
        }

        private static void AssertIdentity(Matrix4x4 m, string why)
            => Assert.IsTrue(m == Matrix4x4.identity, $"{why}, got\n{m}");

        [Test]
        public void SdrDisplay_IsIdentity()
        {
            AssertIdentity(GlassBackdropDecode.For(hdrOutputActive: false, postProcessed: true,
                                                   ColorGamut.HDR10, paperWhiteNits: 300f),
                "on an SDR display the capture is already in the space the UI is drawn in");
        }

        [Test]
        public void HdrDisplay_WithoutPostProcessing_IsIdentity()
        {
            // Without post-processing URP leaves the captured image untouched and converts scene
            // and UI alike in the final blit — nothing to undo.
            AssertIdentity(GlassBackdropDecode.For(hdrOutputActive: true, postProcessed: false,
                                                   ColorGamut.HDR10, paperWhiteNits: 300f),
                "without post-processing the capture is still the raw linear scene");
        }

        [Test]
        public void HdrDisplay_Rec709_DividesByPaperWhite()
        {
            // scRGB (16-bit) output keeps Rec709 primaries: the only thing to undo is the scale.
            var decode = GlassBackdropDecode.For(true, true, ColorGamut.Rec709, 200f);
            AssertRoundTrips(decode, Matrix4x4.identity, 200f);
        }

        [Test]
        public void HdrDisplay_Rec2020_RotatesBackAndDividesByPaperWhite()
        {
            // HDR10 (10-bit PQ) output: URP grades into Rec2020 and scales by paper white.
            var decode = GlassBackdropDecode.For(true, true, ColorGamut.HDR10, 160f);
            AssertRoundTrips(decode, Rec709ToRec2020, 160f);
        }

        [Test]
        public void HdrDisplay_P3_RotatesBackAndDividesByPaperWhite()
        {
            var decode = GlassBackdropDecode.For(true, true, ColorGamut.DisplayP3, 300f);
            AssertRoundTrips(decode, Rec709ToP3D65, 300f);
        }

        [Test]
        public void ZeroPaperWhite_StaysFinite()
        {
            // The Tonemapping override's paper white can be dragged to 0. URP's own output is
            // broken at that point; the capture must not add NaNs to it.
            var decode = GlassBackdropDecode.For(true, true, ColorGamut.HDR10, 0f);
            for (var i = 0; i < 16; i++)
                Assert.IsFalse(float.IsNaN(decode[i]) || float.IsInfinity(decode[i]),
                    $"element {i} is {decode[i]}");
        }
    }
}
