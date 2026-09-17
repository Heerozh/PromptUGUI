using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.Rendering;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The progress cut on a GLASS panel (spec 2026-09-18 §5.2): the glass shader lights its
    /// edge from an analytic normal, so the cut has to be visible to that normal too — otherwise
    /// the refraction band and the highlight along the leading edge point the wrong way. Same URP
    /// harness and the same flat-orange world as <see cref="GlassRenderTests"/>.
    /// </summary>
    public class ProceduralCutGlassRenderTests
    {
        private static readonly Color WorldColor = new(0.85f, 0.42f, 0.10f);
        private const int Size = 256;

        private Camera _capture;
        private Camera _ui;
        private RenderTexture _captureRt;
        private RenderTexture _uiRt;

        [SetUp]
        public void SetUp()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null || pipeline.GetType().Name != "UniversalRenderPipelineAsset")
                Assert.Ignore("Glass rendering needs URP to be the active render pipeline.");

            UI.ResetForTests();
            GlassRuntime.RenderOutsidePlayModeForTests = true;
            _captureRt = new RenderTexture(Size, Size, 24) { name = "CutGlassCaptureRT" };
            _uiRt = new RenderTexture(Size, Size, 24) { name = "CutGlassUIRT" };
            _capture = NewCamera("CutGlassCaptureCamera", WorldColor, _captureRt, -10f);
            _ui = NewCamera("CutGlassUICamera", Color.black, _uiRt, 0f);
            UI.Glass.Camera = _capture;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_capture != null) Object.DestroyImmediate(_capture.gameObject);
            if (_ui != null) Object.DestroyImmediate(_ui.gameObject);
            Release(_captureRt);
            Release(_uiRt);
        }

        private static Camera NewCamera(string name, Color background, RenderTexture target, float depth)
        {
            var cam = new GameObject(name).AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = background;
            cam.targetTexture = target;
            cam.depth = depth;
            cam.cullingMask = ~0;
            return cam;
        }

        private static void Release(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        /// <summary>
        /// A 200×40 square-cornered glass bar (ConstantPixelSize at scale 1 → one unit is one
        /// pixel; x ∈ [28, 228], y ∈ [108, 148]), lit from one side, cut in half.
        /// </summary>
        private ProceduralPanel Open(float lightAngle)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' glass='true' anchor='center' width='200' height='40'
         frost='0' depth='10' dispersion='0' noise='0' saturation='1'
         lightAngle='{lightAngle}' lightIntensity='1'/>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _ui;
            canvas.planeDistance = 10f;

            return screen.Get<Frame>("g").GameObject.GetComponent<ProceduralPanel>();
        }

        private Color RenderAndSampleAt(int x, int y, string dumpName)
        {
            _capture.Render();
            Canvas.ForceUpdateCanvases();
            _ui.Render();

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = _uiRt;
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            var path = Path.Combine(UnityEngine.Application.temporaryCachePath, dumpName);
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"PromptUGUI cut-glass render dump: {path}");

            var sample = tex.GetPixel(x, y);
            Object.DestroyImmediate(tex);
            return sample;
        }

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        /// <summary>
        /// The leading edge of a half-cut bar faces +x. Lit from the right (90°) its bevel band
        /// catches the highlight; lit from the left (−90°) it does not. Without the cut reaching
        /// the analytic normal, the band there would carry the uncut rect's normal — ±y at the
        /// middle of a wide bar — and the two lightings would come out identical.
        /// </summary>
        [Test]
        public void TheLeadingEdgeOfACutGlassBar_IsLitLikeAnEdgeFacingTheCut()
        {
            // 4 px inside the cut at x = 128, well within the 10 px bevel band.
            const int x = 124, y = 128;

            var lit = Open(lightAngle: 90f);
            lit.SetCut(Vector2.right, 0.5f);
            var facing = RenderAndSampleAt(x, y, "promptugui-cut-glass-lit.png");

            var unlit = Open(lightAngle: -90f);
            unlit.SetCut(Vector2.right, 0.5f);
            var away = RenderAndSampleAt(x, y, "promptugui-cut-glass-unlit.png");

            Assert.Less(facing.b, facing.r * 0.75f, $"not the shader-error magenta, got {facing}");
            Assert.Greater(Luma(facing) - Luma(away), 0.08f,
                $"the cut edge must catch the light from the side it faces: facing {facing} vs away {away}");
        }

        [Test]
        public void PastTheCut_AGlassBarShowsNothing()
        {
            var panel = Open(lightAngle: 90f);
            panel.SetCut(Vector2.right, 0.5f);
            var past = RenderAndSampleAt(178, 128, "promptugui-cut-glass-past.png");

            Assert.Less(past.r + past.g + past.b, 0.1f, $"no pane past the leading edge, got {past}");
        }
    }
}
