using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.Rendering;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The only tests here that can say glass actually works.
    ///
    /// Every other glass test reads back C# state — parameters, material keys, vertex counts — and
    /// none of it touches a GPU, so all of it stays green while the shader fails to compile or the
    /// capture pass never runs. That is exactly the trap a missing
    /// <c>[RequireComponent(typeof(CanvasRenderer))]</c> walked through with 2088 tests passing
    /// (procedural-style spec §12.2). These render real URP frames and read the pixels back.
    ///
    /// Rendering is driven by explicit <c>Camera.Render()</c> calls rather than a PlayMode session:
    /// it is synchronous, needs no frame loop, and does not depend on the Editor having focus.
    ///
    /// The "world" is flat orange on purpose. Unity's shader-error magenta also has a high red
    /// channel, so red alone proves nothing — only the blue channel separates a working glass panel
    /// from a failed shader compile.
    /// </summary>
    public class GlassRenderTests
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
            // These render real URP frames, so they only mean anything where URP is the pipeline
            // actually rendering. A project can have the URP package installed and still render with
            // Built-in (no URP asset assigned) — there RenderPipelineManager.beginCameraRendering
            // never fires, glass correctly degrades, and asserting on blur would be asserting on the
            // host project's settings rather than on this package.
            // Matched by name so the test assembly needs no URP reference of its own.
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null || pipeline.GetType().Name != "UniversalRenderPipelineAsset")
                Assert.Ignore("Glass rendering needs URP to be the active render pipeline; this " +
                              "project renders with " +
                              (pipeline == null ? "the Built-in pipeline" : pipeline.GetType().Name) +
                              ". Assign a Universal Render Pipeline Asset in Project Settings > " +
                              "Graphics to run these.");

            UI.ResetForTests();
            GlassRuntime.RenderOutsidePlayModeForTests = true;

            _captureRt = new RenderTexture(Size, Size, 24) { name = "GlassCaptureRT" };
            _uiRt = new RenderTexture(Size, Size, 24) { name = "GlassUIRT" };

            _capture = NewCamera("GlassCaptureCamera", WorldColor, _captureRt, -10f);
            _ui = NewCamera("GlassUICamera", Color.black, _uiRt, 0f);
            UI.Glass.Camera = _capture;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            Destroy(_capture);
            Destroy(_ui);
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

        private static void Destroy(Camera cam)
        {
            if (cam != null) Object.DestroyImmediate(cam.gameObject);
        }

        private static void Release(RenderTexture rt)
        {
            if (rt == null) return;
            rt.Release();
            Object.DestroyImmediate(rt);
        }

        /// <summary>
        /// Opens a screen onto the UI camera. Deliberately not the capture camera: a glass panel
        /// drawn into the picture it samples would be reading itself.
        /// </summary>
        private void Open(string xml)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponentInParent<Canvas>();
            Assert.IsNotNull(canvas);
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _ui;
            canvas.planeDistance = 10f;
        }

        /// <summary>Renders the world, then the UI that samples it, and reads the centre pixel.</summary>
        private Color RenderAndSample(string dumpName = null)
        {
            _capture.Render();          // publishes the blurred backdrop
            Canvas.ForceUpdateCanvases();
            _ui.Render();               // draws the glass, sampling what the capture published

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = _uiRt;
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            if (dumpName != null)
            {
                // A diagnostic, not an assertion: the repo's pixel tooling already insists that
                // someone go and look at the image, and glass is exactly the kind of effect whose
                // parameters can all be "correct" while the result looks wrong.
                var path = Path.Combine(UnityEngine.Application.temporaryCachePath, dumpName);
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.Log($"PromptUGUI glass render dump: {path}");
            }

            var centre = tex.GetPixel(Size / 2, Size / 2);
            Object.DestroyImmediate(tex);
            return centre;
        }

        private const string GlassPanel = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' glass='true' anchor='center' width='160' height='120' radius='24'
         frost='0.6' depth='8' lightAngle='-35' borderWidth='1' borderColor='#ffffff/0.35'/>
</Screen></PromptUGUI>";

        private static void AssertNotTheErrorShader(Color c)
        {
            // Unity's shader-error magenta is (1, 0, 1): red as high as blue, green at zero.
            Assert.Less(c.b, c.r * 0.75f,
                $"blue as high as red means the shader failed to compile and Unity substituted its " +
                $"magenta error shader, got {c}");
        }

        [Test]
        public void GlassPanel_ShowsTheBlurredCapture()
        {
            Open(GlassPanel);
            var c = RenderAndSample("promptugui-glass.png");

            Assert.IsTrue(UI.Glass.IsActive, "the URP capture pass must have published a backdrop");
            Assert.Greater(c.r, 0.25f, $"the panel should be showing the orange world, got {c}");
            Assert.Greater(c.g, 0.05f, $"got {c}");
            AssertNotTheErrorShader(c);
        }

        [Test]
        public void OutsideTheShape_NothingIsDrawn()
        {
            // Guards the SDF: a panel that fills its whole quad instead of its rounded rect would
            // still pass the test above.
            Open(GlassPanel);
            _capture.Render();
            Canvas.ForceUpdateCanvases();
            _ui.Render();

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = _uiRt;
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            var corner = tex.GetPixel(4, 4);
            Object.DestroyImmediate(tex);
            Assert.Less(corner.r, 0.1f, $"the 160x120 panel must not cover the corner, got {corner}");
        }

        [Test]
        public void GlassDisabled_StopsSamplingTheBackdrop()
        {
            Open(GlassPanel);
            RenderAndSample();
            Assert.IsTrue(UI.Glass.IsActive);

            UI.Glass.Enabled = false;
            var c = RenderAndSample("promptugui-glass-fallback.png");

            Assert.IsFalse(UI.Glass.IsActive);
            // The panel carries no fill, so with no backdrop there is nothing left but the hairline
            // border — the centre must fall back to the camera's black clear.
            Assert.Less(c.r, 0.1f, $"fallback must not still be sampling a backdrop, got {c}");
            Assert.Less(c.g, 0.1f, $"got {c}");
        }

        [Test]
        public void TintedGlass_ShowsBothTheBackdropAndTheTint()
        {
            Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' glass='true' anchor='center' width='160' height='120' radius='24'
         color='#0044ff/0.45' frost='0.5'/>
</Screen></PromptUGUI>");
            var c = RenderAndSample("promptugui-glass-tinted.png");

            Assert.Greater(c.b, 0.15f, $"the blue tint must be visible, got {c}");
            Assert.Greater(c.r, 0.05f, $"the orange world must still show through it, got {c}");
        }

        [Test]
        public void WeldedGroup_Renders()
        {
            Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='grp' weld='14' frost='0.6' lightAngle='-35'
         anchor='center' width='220' height='150'>
    <Frame id='a' glass='true' anchor='top-stretch' height='80' radius='16' depth='8'/>
    <Frame id='b' glass='true' anchor='bottom-left' width='130' height='70' radius='12' depth='3'/>
  </Frame>
</Screen></PromptUGUI>");
            var c = RenderAndSample("promptugui-glass-weld.png");

            Assert.Greater(c.r, 0.2f, $"the fused pane should be showing the world, got {c}");
            AssertNotTheErrorShader(c);
        }

        [Test]
        public void NonGlassProceduralPanel_StillRenders()
        {
            // The opaque shader shares UI-PanelSDF.cginc with the glass one; this catches a broken
            // include taking both down at once.
            Open(@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' color='#00ff00' anchor='center' width='160' height='120' radius='24'/>
</Screen></PromptUGUI>");
            var c = RenderAndSample("promptugui-procedural.png");

            Assert.Greater(c.g, 0.5f, $"a solid green panel must draw green, got {c}");
            Assert.Less(c.r, 0.2f, $"got {c}");
        }
    }
}
