using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.Rendering;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The one thing about fillets that the opaque render cannot see: the glass edge light reads
    /// the analytic normal, and on a fillet arc that normal has to <em>rotate</em> from one edge's
    /// direction to the other's. A "nearest half-plane" normal keeps the distance field exactly
    /// right and still snaps the highlight from the top edge's brightness straight to the chamfer's
    /// half-way round the arc (spec 2026-08-29 §5.6 / §9).
    ///
    /// <para>Same URP-only harness as <see cref="GlassRenderTests"/>; the world is mid-grey so the
    /// highlight never clips a channel.</para>
    /// </summary>
    public class CornerFilletGlassRenderTests
    {
        private static readonly Color WorldColor = new(0.40f, 0.40f, 0.40f);
        private const int Size = 256;
        private const float W = 200f;
        private const float H = 160f;

        private Camera _capture;
        private Camera _ui;
        private RenderTexture _captureRt;
        private RenderTexture _uiRt;
        private Texture2D _shot;
        private Rect _rect;

        [SetUp]
        public void SetUp()
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null || pipeline.GetType().Name != "UniversalRenderPipelineAsset")
                Assert.Ignore("Glass rendering needs URP to be the active render pipeline.");

            UI.ResetForTests();
            GlassRuntime.RenderOutsidePlayModeForTests = true;

            _captureRt = new RenderTexture(Size, Size, 24) { name = "FilletGlassCaptureRT" };
            _uiRt = new RenderTexture(Size, Size, 24) { name = "FilletGlassUIRT" };
            _capture = NewCamera("FilletGlassCaptureCamera", WorldColor, _captureRt, -10f);
            _ui = NewCamera("FilletGlassUICamera", Color.black, _uiRt, 0f);
            UI.Glass.Camera = _capture;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_shot != null) Object.DestroyImmediate(_shot);
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

        private void Render(string attrs, string dumpName)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='g' glass='true' anchor='center' width='{W}' height='{H}' {attrs}/>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _ui;
            canvas.planeDistance = 10f;

            _capture.Render();
            Canvas.ForceUpdateCanvases();
            _ui.Render();

            if (_shot != null) Object.DestroyImmediate(_shot);
            _shot = new Texture2D(Size, Size, TextureFormat.RGBA32, false);
            var previous = RenderTexture.active;
            RenderTexture.active = _uiRt;
            _shot.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            _shot.Apply();
            RenderTexture.active = previous;

            var path = Path.Combine(UnityEngine.Application.temporaryCachePath, dumpName);
            File.WriteAllBytes(path, _shot.EncodeToPNG());
            Debug.Log($"PromptUGUI corner-fillet glass render dump: {path}");

            var rt = (RectTransform)screen.Get<Frame>("g").GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            _rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y),
                                   Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
            Assert.Greater(_rect.width, W * 0.5f, "the probed rect rendered far too small");
        }

        /// <summary>Luma at a point given in canvas units in from the top-left corner.</summary>
        private float LumaAtTopLeftInset(float insetX, float insetY)
        {
            var u = insetX / W;
            var v = (H - insetY) / H;
            var c = _shot.GetPixel(Mathf.RoundToInt(Mathf.LerpUnclamped(_rect.xMin, _rect.xMax, u)),
                                   Mathf.RoundToInt(Mathf.LerpUnclamped(_rect.yMin, _rect.yMax, v)));
            return 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;
        }

        [Test]
        public void EdgeHighlight_SweepsRoundTheFilletArc()
        {
            // cut 60 r40 on 200x160, lit from straight above at full intensity, depth 10, no grain.
            // Top-left corner: k = 0.609, eroded vertex (the arc centre) at inset (76.6, 40).
            // Three probes, all 5 units inside the outline (band = 0.5, bevel = 0.25):
            //   top edge, mid-way ............ normal straight up ......... spec = 1.00
            //   arc, half-way round ........... normal 22.5° off up ....... spec = cos⁴ ≈ 0.73
            //   chamfer, mid-way .............. normal 45° off up ......... spec = 0.25
            // A normal that snaps between the two edges makes the arc probe read 1.00 or 0.25.
            Render("radius='cut 60 r40' frost='0.6' depth='10' lightAngle='0' lightIntensity='1' " +
                   "noise='0' dispersion='0'", "pugui-fillet-glass.png");

            var top = LumaAtTopLeftInset(100f, 5f);
            var arc = LumaAtTopLeftInset(63.2f, 7.7f);
            var chamfer = LumaAtTopLeftInset(33.5f, 33.5f);

            Assert.Greater(top, chamfer + 0.08f,
                $"sanity: the top edge ({top:F3}) must be brighter than the chamfer ({chamfer:F3})");
            Assert.Greater(top, arc + 0.02f,
                $"half-way round the arc ({arc:F3}) must be dimmer than the top edge ({top:F3})");
            Assert.Greater(arc, chamfer + 0.03f,
                $"…and brighter than the chamfer ({chamfer:F3}) — the normal has to rotate, not snap");
        }
    }
}
