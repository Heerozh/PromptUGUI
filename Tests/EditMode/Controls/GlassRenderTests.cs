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
            => RenderAndSampleAt(Size / 2, Size / 2, dumpName);

        /// <summary>
        /// The same render, read at one chosen pixel. Coordinates are bottom-left origin device
        /// pixels, and the canvas has no <c>reference=</c>, so it runs ConstantPixelSize at
        /// scaleFactor 1 — one XML unit is one pixel here, and a centred box's centre is
        /// <c>(Size / 2, Size / 2)</c>.
        /// </summary>
        private Color RenderAndSampleAt(int x, int y, string dumpName = null)
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

            var sample = tex.GetPixel(x, y);
            Object.DestroyImmediate(tex);
            return sample;
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

        /// <summary>
        /// Mean luminance of the top and bottom halves of a rendered frame. Everything about the
        /// panel except the edge lighting is vertically symmetric, so the difference between the two
        /// isolates the lighting term.
        /// </summary>
        private (float Top, float Bottom) RenderHalfLuma(string xml)
        {
            Open(xml);
            _capture.Render();
            Canvas.ForceUpdateCanvases();
            _ui.Render();

            var tex = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = _uiRt;
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = previous;

            var pixels = tex.GetPixels();
            Object.DestroyImmediate(tex);

            float top = 0f, bottom = 0f;
            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var c = pixels[y * Size + x];
                    var luma = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
                    // GetPixels is bottom-up.
                    if (y >= Size / 2) top += luma; else bottom += luma;
                }
            }
            var half = Size * Size * 0.5f;
            return (top / half, bottom / half);
        }

        private static string GlassPanelLitFrom(float angle) => $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' glass='true' anchor='center' width='160' height='120' radius='24'
         frost='0.6' depth='10' lightAngle='{angle}' lightIntensity='1'/>
</Screen></PromptUGUI>";

        [Test]
        public void EdgeHighlight_LandsOnTheSideTheLightComesFrom()
        {
            // lightAngle is defined in canvas space — 0 is straight up. Deriving the edge normal from
            // raster-space ddx/ddy instead would satisfy every other test here while putting the
            // highlight on the wrong edge on any platform whose raster Y runs the other way
            // (GL / GLES / WebGL, all shipping targets for this package). Comparing the two angles
            // against each other keeps this independent of the panel's exact pixel bounds.
            var litFromAbove = RenderHalfLuma(GlassPanelLitFrom(0f));
            var litFromBelow = RenderHalfLuma(GlassPanelLitFrom(180f));

            Assert.Greater(litFromAbove.Top, litFromAbove.Bottom,
                "lightAngle=0 is straight up, so the top bevel must be the bright one");
            Assert.Greater(litFromBelow.Bottom, litFromBelow.Top,
                "lightAngle=180 must move the highlight to the bottom bevel");
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

        /// <summary>
        /// A thin block sitting wholly inside a thick one, so the step runs along the inner block's
        /// own contour — the layout the fusion draws a groove for, and the one that isolates the
        /// step cleanly.
        ///
        /// <para>Deliberately not two blocks meeting edge to edge. There the fused SDF is only a
        /// few px from zero (min of two fields is not the union's distance across a shared face),
        /// so the OUTER bevel and the smin crease are both live at the junction and both answer the
        /// light — a light-flip comparison there measures those, not the step. Deep inside the big
        /// block <c>band</c> and <c>crease</c> are zero and the step is the only term left.</para>
        /// </summary>
        private const string StepPair = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='grp' weld='14' seam='12' frost='0.6' lightAngle='{0}'
         anchor='center' width='220' height='150'>
    <Frame id='a' glass='true' anchor='center' width='220' height='150' depth='8'/>
    <Frame id='b' glass='true' anchor='center' width='80'  height='80'  depth='{1}'/>
  </Frame>
</Screen></PromptUGUI>";

        /// <summary>The same scene with the seam left open, for the sign-of-seam test.</summary>
        private const string StepPairSeam = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='grp' weld='14' seam='{2}' frost='0.6' lightAngle='{0}'
         anchor='center' width='220' height='150'>
    <Frame id='a' glass='true' anchor='center' width='220' height='150' depth='8'/>
    <Frame id='b' glass='true' anchor='center' width='80'  height='80'  depth='{1}'/>
  </Frame>
</Screen></PromptUGUI>";

        // Just outside the inner block's left contour (40px left of centre), halfway up. The step is
        // one-sided: the block keeps its full height up to its own contour and the drop happens in a
        // skirt OUTSIDE it, steepest at the contour — so the pixel that sees the face is the one
        // sitting on the skirt, not the one inside the block.
        private const int StepProbeX = Size / 2 - 40 - 2;
        private const int StepProbeY = Size / 2;
        // Deeper into the same skirt (seam is 12 here): the fade must have taken most of it away.
        private const int StepFarProbeX = Size / 2 - 40 - 9;

        private static float Luma(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        [Test]
        public void ThicknessStep_LightsTheFaceTheLightFallsOn()
        {
            // The inner block is thinner, so its contour is a groove: the step's face looks inwards,
            // and light arriving from that side lands on it.
            Open(string.Format(StepPair, "90", "2"));    // light from the right — onto the face
            var lit = RenderAndSampleAt(StepProbeX, StepProbeY, "promptugui-glass-seam-lit.png");

            Open(string.Format(StepPair, "-90", "2"));   // light from the left — away from it
            var away = RenderAndSampleAt(StepProbeX, StepProbeY, "promptugui-glass-seam-away.png");

            AssertNotTheErrorShader(lit);
            Assert.Greater(Luma(lit), Luma(away) + 0.05f,
                $"the thickness step must catch the light on the side it faces, got lit={lit} " +
                $"away={away}");
        }

        [Test]
        public void ThicknessStep_IsBrightAtTheContourAndFadesOutwards()
        {
            // The design brief is a fine line hugging the raised block with a soft glow beyond it,
            // not a uniform band across the whole transition. That is the skirt profile: steepest
            // at the contour, cubic falloff outside. Two pixels on the same skirt, both lit from
            // the side the face looks at — the one at the contour must be clearly brighter.
            Open(string.Format(StepPair, "90", "2"));
            var near = RenderAndSampleAt(StepProbeX, StepProbeY);
            var far = RenderAndSampleAt(StepFarProbeX, StepProbeY, "promptugui-glass-seam-profile.png");

            Assert.Greater(Luma(near), Luma(far) + 0.05f,
                $"the step highlight should peak at the block's contour and fade across the seam, " +
                $"got near={near} far={far}");
        }

        [Test]
        public void SeamSign_ChoosesWhichSideOfTheContourTheRampFallsOn()
        {
            // Same scene, same light, same |seam| — only the sign differs. Positive puts the ramp
            // outside the raised block's contour (the glow lands on what surrounds it), negative
            // puts it inside. Two probes 3px either side of that contour therefore swap places.
            const int outsideX = Size / 2 - 40 - 3;
            const int insideX = Size / 2 - 40 + 3;

            Open(string.Format(StepPairSeam, "90", "2", "12"));
            var outOut = RenderAndSampleAt(outsideX, StepProbeY, "promptugui-glass-seam-outward.png");
            var outIn = RenderAndSampleAt(insideX, StepProbeY);

            Open(string.Format(StepPairSeam, "90", "2", "-12"));
            var inOut = RenderAndSampleAt(outsideX, StepProbeY);
            var inIn = RenderAndSampleAt(insideX, StepProbeY, "promptugui-glass-seam-inward.png");

            AssertNotTheErrorShader(inIn);
            Assert.Greater(Luma(outOut), Luma(outIn) + 0.03f,
                $"seam=12 must light the skirt outside the block, got outside={outOut} inside={outIn}");
            Assert.Greater(Luma(inIn), Luma(inOut) + 0.03f,
                $"seam=-12 must light the ramp inside the block, got inside={inIn} outside={inOut}");
        }

        [Test]
        public void EqualThickness_DrawsNoStep()
        {
            // Same geometry, same seam, one depth: the height field is constant, its gradient is
            // exactly zero, and the two renders have to come out identical. This is what keeps the
            // feature from quietly drawing the dividing line weld exists to remove.
            Open(string.Format(StepPair, "90", "8"));
            var lit = RenderAndSampleAt(StepProbeX, StepProbeY);

            Open(string.Format(StepPair, "-90", "8"));
            var away = RenderAndSampleAt(StepProbeX, StepProbeY);

            Assert.Less(Mathf.Abs(Luma(lit) - Luma(away)), 2f / 255f,
                $"two blocks of equal depth have no step to light, got lit={lit} away={away}");
        }

        [Test]
        public void CutCornerOnAWeldedBlock_IsActuallyCut()
        {
            // Members run the same corner solver as a single panel, so a chamfer survives the
            // fusion. The probe sits 12px in from both edges of the top-left corner: a 40px cut
            // removes it (12 + 12 < 40), a 40px round corner keeps it (its centre is 39.6px away).
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='grp' weld='4' frost='0.6' anchor='center' width='220' height='150'>
    <Frame id='a' glass='true' anchor='top-left' width='110' height='150' radius='{0}' depth='6'/>
    <Frame id='b' glass='true' anchor='bottom-right' width='40' height='40' depth='2'/>
  </Frame>
</Screen></PromptUGUI>";

            // Container spans 220x150 about the centre, so its top-left is (Size/2 - 110, Size/2 + 75).
            const int probeX = Size / 2 - 110 + 12;
            const int probeY = Size / 2 + 75 - 12;

            Open(string.Format(xml, "40"));
            var round = RenderAndSampleAt(probeX, probeY, "promptugui-glass-weld-round.png");
            Open(string.Format(xml, "cut 40"));
            var cut = RenderAndSampleAt(probeX, probeY, "promptugui-glass-weld-cut.png");

            // The UI camera clears to opaque black, so alpha says nothing — the world behind the
            // glass is orange and the background is not.
            Assert.Greater(round.r, 0.3f, $"a 40px round corner keeps this pixel, got {round}");
            Assert.Less(cut.r, 0.1f,
                $"a 40px chamfer removes it — if this pixel is still drawn the group flattened the " +
                $"corner back to a round one, got {cut}");
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

        // ---- Blur kernel ------------------------------------------------------------------------
        //
        // The capture chain is a stack of sparse 4-tap Kawase blits, and a sparse kernel only reads
        // as a blur while every tap's bilinear footprint covers the gap to the next tap — the same
        // rule the ImageFx mip prefilter enforces (image-fx spec §14.1). Taps further apart than
        // their footprints do not smear a point light, they copy it, once per tap per pass, into a
        // lattice of dots. These tests light a few pixels behind the glass and look at the result.

        private const string FlatGlass = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' glass='true' anchor='stretch' frost='{0}' depth='0' lightIntensity='0'
         noise='0' saturation='1'/>
</Screen></PromptUGUI>";

        /// <summary>
        /// A black world with each camera kept to its own canvas. The orange world of the colour
        /// tests would light every pixel of the row these tests measure; and both cameras sit at
        /// the origin, so by default the capture camera also photographs the glass panel (a
        /// harmless smear above, but these measure the exact shape of one blob) while the UI camera
        /// would see the lit square. Moving the capture camera back past its own far plane
        /// separates the two frustums.
        /// </summary>
        private void DarkIsolatedWorld()
        {
            _capture.backgroundColor = Color.black;
            _capture.transform.position = new Vector3(0f, 0f, -_capture.farClipPlane - 100f);
        }

        /// <summary>
        /// A white square drawn straight into the capture camera's picture, on a canvas of its own.
        /// No scaler, so one unit is one device pixel and <paramref name="x"/> / <paramref name="y"/>
        /// are the bottom-left pixel it covers.
        /// </summary>
        private GameObject NewWorldSquare(int x, int y, int size)
        {
            var root = new GameObject("WorldSquare");
            var canvas = root.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _capture;
            canvas.planeDistance = 10f;

            var square = new GameObject("Square").AddComponent<UnityEngine.UI.Image>();
            square.transform.SetParent(root.transform, false);
            square.color = Color.white;
            var rt = square.rectTransform;
            rt.anchorMin = rt.anchorMax = Vector2.zero;
            rt.pivot = Vector2.zero;
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(size, size);
            return root;
        }

        /// <summary>Renders as <see cref="RenderAndSampleAt"/> does and returns one row's luminance.</summary>
        private float[] RenderRow(int y, string dumpName)
        {
            Canvas.ForceUpdateCanvases();   // the square's canvas has to be laid out before it is captured
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
            Debug.Log($"PromptUGUI glass render dump: {path}");

            var row = new float[Size];
            for (var x = 0; x < Size; x++) row[x] = Luma(tex.GetPixel(x, y));
            Object.DestroyImmediate(tex);
            return row;
        }

        private static int ArgMax(float[] row)
        {
            var best = 0;
            for (var i = 1; i < row.Length; i++) if (row[i] > row[best]) best = i;
            return best;
        }

        /// <summary>
        /// Walks from the peak out to each edge and returns the largest rise after a fall: zero for
        /// a kernel that only ever descends, most of the peak for a lattice of copies.
        /// </summary>
        private static float LargestClimbAwayFromPeak(float[] row, int peak)
        {
            var climb = 0f;
            foreach (var step in new[] { 1, -1 })
            {
                var floor = row[peak];
                for (var x = peak; x >= 0 && x < row.Length; x += step)
                {
                    floor = Mathf.Min(floor, row[x]);
                    climb = Mathf.Max(climb, row[x] - floor);
                }
            }
            return climb;
        }

        private static int WidthAbove(float[] row, float threshold)
        {
            int first = -1, last = -1;
            for (var x = 0; x < row.Length; x++)
            {
                if (row[x] < threshold) continue;
                if (first < 0) first = x;
                last = x;
            }
            return first < 0 ? 0 : last - first + 1;
        }

        private static string Describe(float[] row, int around)
        {
            var sb = new System.Text.StringBuilder();
            for (var x = Mathf.Max(0, around - 48); x < Mathf.Min(row.Length, around + 49); x++)
                sb.Append(Mathf.RoundToInt(row[x] * 255f)).Append(' ');
            return sb.ToString().TrimEnd();
        }

        [Test]
        public void HeavyBlur_SmearsAPointIntoOneBlob_NotALattice()
        {
            // A 4x4 white square aligned to the capture's 4x4 downsample blocks — exactly one texel
            // of the quarter-resolution backdrop — under the heaviest frost. Along the row through
            // its middle a blur falls away from the peak monotonically on both sides. A kernel whose
            // taps sit further apart than their footprints instead lays down copies of the square:
            // the row dips and climbs back up, and that is the 4x4 grid of dots one bright star
            // turns into over the world map.
            DarkIsolatedWorld();
            var square = NewWorldSquare(Size / 2, Size / 2, 4);
            try
            {
                Open(string.Format(FlatGlass, "1"));
                var row = RenderRow(Size / 2 + 1, "promptugui-glass-blur-point.png");
                Assert.IsTrue(UI.Glass.IsActive, "the URP capture pass must have published a backdrop");

                var peak = ArgMax(row);
                Assert.Greater(row[peak], 0.02f, "the lit square must show through the glass, got a dark row");

                var climb = LargestClimbAwayFromPeak(row, peak);
                Assert.Less(climb, 2f / 255f,
                    $"a blur only falls away from its peak, but this row climbs back by " +
                    $"{climb * 255f:F1}/255 — the square is being copied per tap, not smeared. " +
                    $"Row around the peak: {Describe(row, peak)}");
                Assert.That(peak, Is.InRange(Size / 2 - 8, Size / 2 + 12),
                    $"the blob must sit on the square, peak at x={peak}");

                // And it has to be a blur, not the square drawn sharp.
                Assert.Greater(WidthAbove(row, row[peak] * 0.1f), 24,
                    $"the heavy frost should spread a 4px square well past 24px, row: {Describe(row, peak)}");
            }
            finally
            {
                Object.DestroyImmediate(square);
            }
        }

        [Test]
        public void Capture_SeesEveryPixel_NotJustBlockCorners()
        {
            // A 2x2 white square on the two middle columns and rows of a 4x4 downsample block. The
            // downsample's four bilinear taps have to cover the whole block between them; taps that
            // only read the block's corner pixels never see these two, and a star that small blinks
            // in and out as the map scrolls it across block boundaries.
            DarkIsolatedWorld();
            var square = NewWorldSquare(Size / 2 + 1, Size / 2 + 1, 2);
            try
            {
                Open(string.Format(FlatGlass, "0"));
                var row = RenderRow(Size / 2 + 1, "promptugui-glass-blur-coverage.png");
                Assert.IsTrue(UI.Glass.IsActive, "the URP capture pass must have published a backdrop");

                var peak = ArgMax(row);
                Assert.Greater(row[peak], 0.03f,
                    $"a 2x2 square on a block's inner pixels never reached the backdrop; row around " +
                    $"the square: {Describe(row, Size / 2 + 1)}");
            }
            finally
            {
                Object.DestroyImmediate(square);
            }
        }

        // ---- HDR display decode -----------------------------------------------------------------
        //
        // On an HDR display URP hands the capture over already prepared for the display — rotated
        // into its gamut and scaled to paper-white nits — and the overlay UI the glass draws into
        // gets that same treatment again at compositing. The capture undoes it once with a matrix
        // on the downsample blit. GlassBackdropDecodeTests derive the matrix; this proves the blit
        // applies whatever matrix it is given, which no HDR-less test rig can otherwise observe.

        [Test]
        public void BackdropDecode_IsAppliedToTheCapture()
        {
            // The capture camera moved back past its own far plane photographs nothing but its
            // flat orange background — a picture whose every channel is known exactly.
            _capture.transform.position = new Vector3(0f, 0f, -_capture.farClipPlane - 100f);
            Open(string.Format(FlatGlass, "0"));

            var plain = RenderAndSample().linear;
            Assert.IsTrue(UI.Glass.IsActive, "the URP capture pass must have published a backdrop");
            Assert.Greater(plain.r, plain.b, $"the orange world has red over blue, got {plain}");

            // Swap red with blue and halve green: every channel has to move, each by its own rule.
            var swapAndHalve = Matrix4x4.identity;
            swapAndHalve.m00 = 0f; swapAndHalve.m02 = 1f;
            swapAndHalve.m11 = 0.5f;
            swapAndHalve.m20 = 1f; swapAndHalve.m22 = 0f;
            GlassRuntime.BackdropDecodeOverrideForTests = swapAndHalve;

            var decoded = RenderAndSample("promptugui-glass-decode.png").linear;

            Assert.AreEqual(plain.r, decoded.b, 0.03f,
                $"red should have moved into blue; plain {plain}, decoded {decoded}");
            Assert.AreEqual(plain.b, decoded.r, 0.03f,
                $"blue should have moved into red; plain {plain}, decoded {decoded}");
            Assert.AreEqual(plain.g * 0.5f, decoded.g, 0.03f,
                $"green should have halved; plain {plain}, decoded {decoded}");
        }
    }
}
