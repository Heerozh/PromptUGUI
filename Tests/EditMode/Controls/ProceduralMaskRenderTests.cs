using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using PromptScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;Frame mask="self"&gt;</c> clipping children to the procedural shape — the only tests
    /// that can say it actually works, because every claim here is about the stencil buffer and
    /// nothing in C# can observe that. Same reasoning (and the same explicit
    /// <c>Camera.Render()</c> harness) as <see cref="GlassRenderTests"/>, minus the URP gate: a
    /// plain SDF panel needs no backdrop capture, so these run under Built-in too.
    ///
    /// <para>The shape assertions are what pin down the shader's mask coverage (spec §9.3). uGUI
    /// alpha-clips a stencil mask source, so whatever the fragment discards becomes a hole in the
    /// mask. Clipping on the final alpha makes the mask follow the panel's <em>paint</em> — an
    /// outer glow widens it, and a panel with no fill has no interior at all. Clipping on the SDF's
    /// inside coverage makes it follow the panel's <em>shape</em>, which is what an author writing
    /// <c>mask="self"</c> means.</para>
    ///
    /// <para>Rect corners are converted through <c>RectTransformUtility</c> rather than assumed:
    /// the canvas-to-rendertexture mapping depends on the scaler and the camera, and hard-coding it
    /// would make these fail for reasons that have nothing to do with masking.</para>
    /// </summary>
    public class ProceduralMaskRenderTests
    {
        private const int Size = 256;

        private Camera _ui;
        private RenderTexture _uiRt;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "ProceduralMaskUIRT" };
            _ui = new GameObject("ProceduralMaskUICamera").AddComponent<Camera>();
            _ui.clearFlags = CameraClearFlags.SolidColor;
            _ui.backgroundColor = Color.black;
            _ui.targetTexture = _uiRt;
            _ui.cullingMask = ~0;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_ui != null) Object.DestroyImmediate(_ui.gameObject);
            if (_uiRt != null)
            {
                _uiRt.Release();
                Object.DestroyImmediate(_uiRt);
            }
        }

        // A red child stretched 30px BEYOND the frame on every side. The overhang is the point: it
        // is the only way to see whether the mask reaches past the frame's rect, which is exactly
        // what an outer glow does to an alpha-clipped mask.
        private static string Doc(string frameAttrs) => $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' anchor='center' width='160' height='160' mask='self' showMask='false' {frameAttrs}>
    <Image id='k' anchor='stretch' margin='-30' color='#ff0000'/>
  </Frame>
</Screen></PromptUGUI>";

        /// <summary>
        /// Red coverage at three places: the middle of the shape, the frame's square corner (inside
        /// the rect, outside a large radius), and 10px below the frame's bottom edge (outside the
        /// shape, inside both the over-sized child and any glow halo).
        /// </summary>
        private (float Centre, float Corner, float BelowEdge) Sample(string frameAttrs, string dumpName)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", Doc(frameAttrs));
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponentInParent<Canvas>();
            Assert.IsNotNull(canvas);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _ui;
            canvas.planeDistance = 10f;

            Canvas.ForceUpdateCanvases();
            _ui.Render();

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = _uiRt;
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            try
            {
                // Diagnostic, not an assertion — the repo's pixel tooling insists someone go and
                // look at the image, and a mask is exactly the kind of thing whose numbers can all
                // be "correct" while the picture is wrong.
                var path = Path.Combine(UnityEngine.Application.temporaryCachePath, dumpName);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log($"PromptUGUI mask render dump: {path}");

                var rect = RectInPixels(screen);
                return (RedFraction(tex, rect.center, 8),
                        RedFraction(tex, new Vector2(rect.xMin + 6f, rect.yMin + 6f), 4),
                        RedFraction(tex, new Vector2(rect.center.x, rect.yMin - 10f), 4));
            }
            finally
            {
                Object.DestroyImmediate(tex);
            }
        }

        /// <summary>The frame's rect in render-texture pixels.</summary>
        private Rect RectInPixels(PromptScreen screen)
        {
            var rt = (RectTransform)screen.Get<Frame>("f").GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);   // 0 = bottom-left, 2 = top-right
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var b = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            return Rect.MinMaxRect(
                Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y),
                Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        /// <summary>Fraction of pixels in a box that are predominantly red (i.e. the child shows).</summary>
        private static float RedFraction(Texture2D tex, Vector2 centre, int half)
        {
            var x0 = Mathf.Clamp(Mathf.RoundToInt(centre.x) - half, 0, Size - 1);
            var x1 = Mathf.Clamp(Mathf.RoundToInt(centre.x) + half, 0, Size - 1);
            var y0 = Mathf.Clamp(Mathf.RoundToInt(centre.y) - half, 0, Size - 1);
            var y1 = Mathf.Clamp(Mathf.RoundToInt(centre.y) + half, 0, Size - 1);

            var red = 0;
            var total = 0;
            for (var y = y0; y <= y1; y++)
            {
                for (var x = x0; x <= x1; x++)
                {
                    var c = tex.GetPixel(x, y);
                    if (c.r > 0.5f && c.g < 0.3f && c.b < 0.3f) red++;
                    total++;
                }
            }
            return total == 0 ? 0f : (float)red / total;
        }

        /// <summary>
        /// The baseline contract: a filled rounded panel clips its children to the rounded shape.
        /// Guards the whole feature against regressing into "no clipping at all".
        /// </summary>
        [Test]
        public void MaskSelf_ClipsChildrenToTheRoundedShape()
        {
            var (centre, corner, _) = Sample("radius='60' color='#3366ff'", "promptugui-mask-filled.png");

            Assert.Greater(centre, 0.9f, "the child must show through the middle of the mask");
            Assert.Less(corner, 0.1f,
                "the frame's square corner lies outside a radius-60 rounded rect, so the child must "
                + "be clipped there — that is the whole point of masking to the SDF");
        }

        /// <summary>
        /// RED before the §9.3 shader change. With the mask alpha-clipped on the final colour, a
        /// panel that paints nothing inside (no fill, no glass) discards every interior fragment,
        /// writes no stencil, and clips its children away entirely — so the most useful form of the
        /// feature, an invisible rounded clipper, is the one form that could not work.
        /// </summary>
        [Test]
        public void MaskSelf_WithNoFill_StillClipsToTheShape()
        {
            var (centre, corner, _) = Sample("radius='60'", "promptugui-mask-nofill.png");

            Assert.Greater(centre, 0.9f,
                "a fill-less panel still has a shape, and the mask is the shape, not the paint");
            Assert.Less(corner, 0.1f, "and it is still rounded");
        }

        /// <summary>
        /// RED before the §9.3 shader change. An outer glow paints beyond the shape
        /// (<c>glow.a *= g*g*(1-inside)</c>) and the alpha clip threshold is 0.001, so the halo used
        /// to write stencil too and children bled into the glow.
        /// </summary>
        [Test]
        public void MaskSelf_WithGlow_DoesNotExtendTheMaskIntoTheHalo()
        {
            var (centre, _, belowEdge) =
                Sample("radius='60' color='#3366ff' glow='24' glowColor='#ffffff'",
                       "promptugui-mask-glow.png");

            Assert.Greater(centre, 0.9f, "guard: the mask still passes inside the shape");
            Assert.Less(belowEdge, 0.1f,
                "10px below the frame is inside the glow halo and inside the over-sized child, but "
                + "outside the shape — the mask must not follow the glow");
        }
    }
}
