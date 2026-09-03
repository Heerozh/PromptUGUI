using UnityEngine;

namespace PromptUGUI.Application.Glass
{
    /// <summary>
    /// The colour transform the capture applies while downsampling, so that what a glass panel
    /// samples sits in the space its own pixels are composited in.
    ///
    /// <para>On an SDR display that is the identity: the captured image is linear Rec709 and so is
    /// the overlay UI. On an HDR display (URP's HDR Output) it is not. With post-processing on, URP
    /// hands the capture over already prepared for the display — its colour-grading LUT rotates the
    /// graded picture into the display's gamut and scales it to nits by the paper-white brightness
    /// (<c>LutBuilderHdr.shader</c>: <c>RotateRec709ToOutputSpace × PaperWhite</c>; the PQ / scRGB
    /// encoding itself waits for the final blit, which is why the capture can read the buffer at
    /// all). The overlay UI, meanwhile, is drawn into an 8-bit off-screen target and composited
    /// afterwards as SDR content: <c>SceneUIComposition</c> applies that same rotation and scale to
    /// every UI pixel. A glass panel that samples the display-ready picture and outputs it as UI
    /// therefore goes through the transform twice — a 1 % grey in the scene, 3 nits at a 300-nit
    /// paper white, lands in the UI target as 3.0 and clips to white — and every panel turns into a
    /// white slab with a faint tint wherever some channel stayed under 1/300. Undoing the transform
    /// once here — inverse gamut rotation, then divide by paper white — puts the sample back where a
    /// UI pixel is expected to be, and the composite then treats the glass exactly like the scene
    /// behind it.</para>
    ///
    /// <para>Without post-processing there is nothing to undo: URP leaves the captured image as the
    /// raw linear scene and converts scene and UI alike in the final blit
    /// (<c>BlitHDROverlay.shader</c>).</para>
    ///
    /// <para>Pure C# on purpose — the URP half (<c>GlassBackdropSystem</c>) only supplies the four
    /// inputs, so the matrix can be checked against URP's forward constants without an HDR
    /// display.</para>
    /// </summary>
    internal static class GlassBackdropDecode
    {
        /// <summary>
        /// Below this the paper white is not a brightness any more; URP's own output has already
        /// gone to NaN by then, but the capture must not add to it.
        /// </summary>
        private const float MinPaperWhiteNits = 1e-3f;

        // Inverses of the Rec709 → output-space rotations URP applies (core's HDROutput.hlsl and
        // ColorSpaceUtils.cs; the P3 inverse is not spelled out there and was derived from its
        // forward matrix). Row-major as written, like the HLSL.
        private static readonly Matrix4x4 Rec2020ToRec709 = Rows(
            1.660496f, -0.587656f, -0.072840f,
            -0.124547f, 1.132895f, -0.008348f,
            -0.018154f, -0.100597f, 1.118751f);

        private static readonly Matrix4x4 P3D65ToRec709 = Rows(
            1.224940f, -0.224940f, 0.000000f,
            -0.042057f, 1.042057f, 0.000000f,
            -0.019638f, -0.078635f, 1.098274f);

        /// <summary>
        /// The matrix to multiply every captured colour by. Identity unless
        /// <paramref name="hdrOutputActive"/> and <paramref name="postProcessed"/> both hold for the
        /// capture camera — the same two conditions under which URP's LUT pass converts for the
        /// display.
        /// </summary>
        internal static Matrix4x4 For(bool hdrOutputActive, bool postProcessed,
                                      ColorGamut displayGamut, float paperWhiteNits)
        {
            if (!hdrOutputActive || !postProcessed) return Matrix4x4.identity;

            // A gamut URP cannot classify gets none of its conversions either
            // (HDROutputUtils.GetColorSpaceForGamut bails out), so there is nothing to undo.
            if (ColorGamutUtility.GetWhitePoint(displayGamut) != WhitePoint.D65) return Matrix4x4.identity;
            Matrix4x4 rotate;
            switch (ColorGamutUtility.GetColorPrimaries(displayGamut))
            {
                case ColorPrimaries.Rec709: rotate = Matrix4x4.identity; break;
                case ColorPrimaries.Rec2020: rotate = Rec2020ToRec709; break;
                case ColorPrimaries.P3: rotate = P3D65ToRec709; break;
                default: return Matrix4x4.identity;
            }

            var scale = 1f / Mathf.Max(paperWhiteNits, MinPaperWhiteNits);
            return Matrix4x4.Scale(new Vector3(scale, scale, scale)) * rotate;
        }

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
    }
}
