using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Do the decorations actually draw the shapes they name? The control tests prove instances
    /// exist and sit in the right slots, and the material tests would prove the parameters reach the
    /// shader — both stay green if the SDF draws a plain box. Only a render tells an L-shaped
    /// bracket from a filled square, or catches a mirrored corner pointing the wrong way.
    ///
    /// <para>Every probe is a geometric predicate rather than a golden image: a bracket paints its
    /// two arms and leaves the elbow hollow, a tick's slanted sides cut its base corners away, a
    /// half-width line stops at the quarter marks. Same explicit <c>Camera.Render()</c> harness as
    /// <see cref="CornerTreatmentRenderTests"/>, PNG dumps and all.</para>
    /// </summary>
    public class DecorRenderTests
    {
        private const int Size = 256;
        private const float W = 200f;
        private const float H = 160f;

        private Camera _ui;
        private RenderTexture _uiRt;
        private Texture2D _shot;
        private Rect _rect;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "DecorUIRT" };
            _ui = new GameObject("DecorUICamera").AddComponent<Camera>();
            _ui.clearFlags = CameraClearFlags.SolidColor;
            _ui.backgroundColor = Color.black;
            _ui.targetTexture = _uiRt;
            _ui.cullingMask = ~0;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_shot != null) Object.DestroyImmediate(_shot);
            if (_ui != null) Object.DestroyImmediate(_ui.gameObject);
            if (_uiRt != null)
            {
                _uiRt.Release();
                Object.DestroyImmediate(_uiRt);
            }
        }

        /// <summary>Renders one host Frame carrying one <c>&lt;Decor&gt;</c>, ready for <see cref="At"/>.</summary>
        private void Render(string decorAttrs, string dumpName)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='host' anchor='center' width='{W}' height='{H}'>
    <Decor id='d' color='#3366ff' {decorAttrs}/>
  </Frame>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _ui;
            canvas.planeDistance = 10f;

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
            Debug.Log($"PromptUGUI decor render dump: {path}");

            var rt = (RectTransform)screen.Get<Frame>("host").GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            _rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y),
                                    Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));

            Assert.Greater(_rect.width, W * 0.5f,
                "the host rendered far smaller than its canvas size — probes would be meaningless");
        }

        /// <summary>Samples in normalised host coordinates (0,0 = bottom-left).</summary>
        private Color At(float u, float v)
            => _shot.GetPixel(Mathf.RoundToInt(Mathf.Lerp(_rect.xMin, _rect.xMax, u)),
                              Mathf.RoundToInt(Mathf.Lerp(_rect.yMin, _rect.yMax, v)));

        /// <summary>Samples at canvas-unit insets measured from a named corner of the host.</summary>
        private Color AtInset(float x, float y, bool left = true, bool top = true)
            => At((left ? x : W - x) / W, (top ? H - y : y) / H);

        private static void AssertPainted(Color c, string what)
            => Assert.Greater(c.b, 0.4f, $"{what} — expected the decoration, got {c}");

        private static void AssertBackground(Color c, string what)
            => Assert.Less(c.b, 0.2f, $"{what} — expected background, got {c}");

        // ---- bracket: an L, not a square ----

        private const float Arm = 40f;
        private const float Stroke = 8f;

        [Test]
        public void Bracket_PaintsTwoArms_AndLeavesTheElbowHollow()
        {
            Render($"kind='bracket' at='top-left' extent='{Arm}' thickness='{Stroke}'",
                   "pugui-decor-bracket-tl.png");

            AssertPainted(AtInset(Arm * 0.5f, Stroke * 0.5f), "halfway along the horizontal arm");
            AssertPainted(AtInset(Stroke * 0.5f, Arm * 0.5f), "halfway down the vertical arm");
            AssertBackground(AtInset(Arm * 0.6f, Arm * 0.6f),
                             "the inside of the elbow — a bracket is a stroke, not a filled corner");
            AssertBackground(AtInset(Arm * 1.6f, Stroke * 0.5f),
                             "past the end of the horizontal arm");
        }

        [Test]
        public void Bracket_MirrorsIntoEveryCorner()
        {
            // Orientation is folded into the vertex data, so this is the assertion that the flip
            // maths matches the canonical (top-left) shape the shader draws.
            Render($"kind='bracket' extent='{Arm}' thickness='{Stroke}'", "pugui-decor-bracket-4.png");

            AssertPainted(AtInset(Arm * 0.5f, Stroke * 0.5f, true, true), "top-left arm");
            AssertPainted(AtInset(Arm * 0.5f, Stroke * 0.5f, false, true), "top-right arm");
            AssertPainted(AtInset(Arm * 0.5f, Stroke * 0.5f, false, false), "bottom-right arm");
            AssertPainted(AtInset(Arm * 0.5f, Stroke * 0.5f, true, false), "bottom-left arm");
            AssertBackground(At(0.5f, 0.5f), "the middle of the host stays empty");
        }

        [Test]
        public void Bracket_SingleCorner_LeavesTheOthersEmpty()
        {
            Render($"kind='bracket' at='top-right' extent='{Arm}' thickness='{Stroke}'",
                   "pugui-decor-bracket-tr.png");

            AssertPainted(AtInset(Arm * 0.5f, Stroke * 0.5f, false, true), "the requested corner");
            AssertBackground(AtInset(Arm * 0.5f, Stroke * 0.5f, true, true),
                             "the opposite corner was not asked for");
        }

        // ---- tick: a triangle pointing away from the host ----

        private const float TickW = 48f;
        private const float TickH = 24f;

        [Test]
        public void Tick_NarrowsTowardsTheOutsideEdge()
        {
            Render($"kind='tick' at='bottom' extent='{TickW}x{TickH}'", "pugui-decor-tick-bottom.png");

            AssertPainted(At(0.5f, (TickH * 0.8f) / H), "the wide end, near the host's edge");
            AssertPainted(At(0.5f, (TickH * 0.2f) / H), "the tip, at the centre line");
            AssertBackground(At(0.5f - (TickW * 0.4f) / W, (TickH * 0.2f) / H),
                             "the tip's left flank — the slanted side has cut this away");
            AssertBackground(At(0.5f + (TickW * 0.4f) / W, (TickH * 0.2f) / H),
                             "the tip's right flank");
        }

        [Test]
        public void Tick_OnTheTopEdge_PointsTheOtherWay()
        {
            Render($"kind='tick' at='top' extent='{TickW}x{TickH}'", "pugui-decor-tick-top.png");

            AssertPainted(At(0.5f, (H - TickH * 0.8f) / H), "the wide end, near the top edge");
            AssertBackground(At(0.5f - (TickW * 0.4f) / W, (H - TickH * 0.2f) / H),
                             "the tip's flank, mirrored with the slot");
        }

        // ---- line: runs along its edge, and only as far as asked ----

        [Test]
        public void Line_HalfWidth_StopsAtTheQuarterMarks()
        {
            Render("kind='line' at='bottom' extent='50%' thickness='6'", "pugui-decor-line-half.png");

            AssertPainted(At(0.5f, 3f / H), "the middle of the line");
            AssertBackground(At(0.1f, 3f / H), "outside the 50% span");
            AssertBackground(At(0.5f, 20f / H), "above the line's thickness");
        }

        [Test]
        public void Line_Inset_MovesItInwards()
        {
            Render("kind='line' at='bottom' extent='100%' thickness='6' inset='30'",
                   "pugui-decor-line-inset.png");

            AssertPainted(At(0.5f, 33f / H), "the line, 30 units in from the bottom edge");
            AssertBackground(At(0.5f, 3f / H), "flush against the edge, where it no longer is");
        }

        // ---- glow ----

        [Test]
        public void Glow_LightsThePixelsOutsideTheStroke()
        {
            Render($"kind='line' at='bottom' extent='100%' thickness='4' glow='0' inset='40'",
                   "pugui-decor-noglow.png");
            var dark = At(0.5f, 52f / H);
            AssertBackground(dark, "with no glow, 8 units above the line is background");

            Render($"kind='line' at='bottom' extent='100%' thickness='4' glow='24' inset='40'",
                   "pugui-decor-glow.png");
            var lit = At(0.5f, 52f / H);
            Assert.Greater(lit.b, dark.b + 0.05f,
                           $"the same point should be lit by the glow (was {dark}, now {lit})");
        }

        // ---- batching ----

        [Test]
        public void FourCornerBrackets_ShareOneMaterial()
        {
            // The slot rides the vertex stream precisely so that identical instances stay on one
            // material and batch into one draw call; if orientation ever leaks into the material
            // key this count becomes four.
            Render($"kind='bracket' extent='{Arm}' thickness='{Stroke}'", "pugui-decor-batch.png");
            Assert.AreEqual(1, DecorMaterialCache.LiveMaterialCount);
        }
    }
}
