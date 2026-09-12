using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Does <c>intensity</c> actually reach the pixels? Every parameter test reads back
    /// <c>PanelParams</c> and stays green if the shader ignores the uniform — the failure mode the
    /// corner treatments and the inner glow both hit. So these probe the frame buffer.
    ///
    /// <para>Each probe is a predicate on the exposure curve (spec 2026-09-12 §5.1) rather than a
    /// golden image: at <c>k = 1</c> nothing may change, a lit fill must get brighter AND whiter
    /// (its weakest channel rises), the halo must keep its hue, and nothing may appear past the
    /// glow. Same explicit <c>Camera.Render()</c> harness as <see cref="InnerGlowRenderTests"/>,
    /// with the fill colour under test rather than a near-black one.</para>
    /// </summary>
    public class IntensityRenderTests
    {
        private const int Size = 256;

        /// <summary>Rect size of the probed Frame, in canvas units. Probes are normalised to it.</summary>
        private const float W = 120f;
        private const float H = 80f;

        /// <summary>Glow radius used by the halo probes; wide enough to sample inside its tail.</summary>
        private const float Glow = 40f;

        private Camera _ui;
        private RenderTexture _uiRt;
        private Texture2D _shot;
        private Rect _rect;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "IntensityUIRT" };
            _ui = new GameObject("IntensityUICamera").AddComponent<Camera>();
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

        /// <summary>Renders one Frame and leaves the frame buffer ready for <see cref="At"/>.</summary>
        private Color32[] Render(string attrs, string dumpName)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' anchor='center' width='{W}' height='{H}' {attrs}/>
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
            Debug.Log($"PromptUGUI intensity render dump: {path}");

            var rt = (RectTransform)screen.Get<Frame>("f").GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            _rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y),
                                    Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));

            Assert.Greater(_rect.width, W * 0.5f,
                "the probed rect rendered far smaller than its canvas size — probes would be meaningless");
            return _shot.GetPixels32();
        }

        /// <summary>Samples in normalised rect coordinates (0,0 = bottom-left); values outside 0..1
        /// reach past the rect, which is where a glow lives.</summary>
        private Color At(float u, float v)
            => _shot.GetPixel(Mathf.RoundToInt(Mathf.LerpUnclamped(_rect.xMin, _rect.xMax, u)),
                              Mathf.RoundToInt(Mathf.LerpUnclamped(_rect.yMin, _rect.yMax, v)));

        private Color Centre() => At(0.5f, 0.5f);

        /// <summary>Samples on the vertical centre line, <paramref name="outset"/> units LEFT of the rect.</summary>
        private Color LeftOutset(float outset) => At(-outset / W, 0.5f);

        private static float Luma(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        private static void AssertPixelIdentical(Color32[] a, Color32[] b, string why)
        {
            Assert.AreEqual(a.Length, b.Length);
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i].r == b[i].r && a[i].g == b[i].g && a[i].b == b[i].b && a[i].a == b[i].a) continue;
                Assert.Fail($"{why}: pixel {i % Size},{i / Size} differs — {a[i]} vs {b[i]}");
            }
        }

        // ---- k = 1 is today ----------------------------------------------------------------------

        [Test]
        public void IntensityOne_IsPixelIdenticalToUnset()
        {
            // The uniform branch: an explicit 1 must take exactly the path an unlit panel takes,
            // so existing documents keep rendering bit for bit.
            var unset = Render($"color='#3b82f6' glow='{Glow}' glowColor='#3b82f6/0.5' borderWidth='2'",
                               "pugui-intensity-unset.png");
            var one = Render($"color='#3b82f6' glow='{Glow}' glowColor='#3b82f6/0.5' borderWidth='2' intensity='1'",
                             "pugui-intensity-one.png");
            AssertPixelIdentical(unset, one, "intensity='1' must be a no-op");
        }

        // ---- the fill: brighter AND whiter -------------------------------------------------------

        [Test]
        public void Intensity_BrightensAndWhitensTheFill()
        {
            Render("color='#3b82f6'", "pugui-intensity-fill-k1.png");
            var plain = Centre();
            Render("color='#3b82f6' intensity='5'", "pugui-intensity-fill-k5.png");
            var lit = Centre();

            Assert.Greater(Luma(lit), Luma(plain) + 0.15f, $"k=5 must be clearly brighter: {plain} → {lit}");
            // Whitening is the point: the weakest channel of #3b82f6 (r ≈ 0.23) has to climb, not
            // just the strong ones — a plain multiply could never do this.
            Assert.Greater(lit.r, 0.55f, $"the red channel must rise towards white, got {lit}");
            Assert.Greater(lit.b, lit.r, $"…while the colour stays on the blue side of white, got {lit}");
        }

        [Test]
        public void PureBlue_WhitensThroughCrosstalk()
        {
            // A per-channel curve alone leaves #0000ff pure blue forever (its r and g are zero at
            // every exposure); the over-exposure crosstalk is what turns it into a hot lavender.
            Render("color='#0000ff' intensity='5'", "pugui-intensity-pure-blue.png");
            var lit = Centre();
            Assert.Greater(lit.r, 0.6f, $"crosstalk must bleed into the zero channels, got {lit}");
            Assert.AreEqual(lit.r, lit.g, 0.03f, "…symmetrically");
            Assert.Greater(lit.b, lit.r, "…without losing the hue entirely");
        }

        // ---- the halo: brighter, same hue, same reach --------------------------------------------

        [Test]
        public void Intensity_BrightensTheHaloAndKeepsItsHue()
        {
            const string halo = "color='#3b82f6' glow='40' glowColor='#3b82f6/0.5'";
            Render(halo, "pugui-intensity-halo-k1.png");
            var plain = LeftOutset(12f);
            Render(halo + " intensity='5'", "pugui-intensity-halo-k5.png");
            var lit = LeftOutset(12f);

            Assert.Greater(Luma(lit), Luma(plain) + 0.1f, $"the tail must get brighter: {plain} → {lit}");
            Assert.Greater(lit.b, lit.g, $"…and stay blue: {lit}");
            Assert.Greater(lit.g, lit.r, $"…blue, not white: {lit}");
            Assert.Less(lit.r / Mathf.Max(lit.b, 0.001f), 0.5f,
                $"the tail is low-energy, so its hue must survive exposure — got {lit}");
        }

        [Test]
        public void Intensity_DoesNotReachPastTheGlow()
        {
            // Exposure brightens what the glow already paints; it must not conjure light where the
            // falloff has reached zero (and nothing at all on a panel without a glow).
            Render("color='#3b82f6' glow='40' glowColor='#3b82f6/0.5' intensity='8'", "pugui-intensity-reach.png");
            Assert.Less(Luma(LeftOutset(Glow + 6f)), 0.02f, "past the glow radius is still background");

            Render("color='#3b82f6' intensity='8'", "pugui-intensity-no-glow.png");
            Assert.Less(Luma(LeftOutset(3f)), 0.02f, "without a glow, nothing outside the rect lights up");
        }

        // ---- glass is not a light --------------------------------------------------------------

        [Test]
        public void Glass_IgnoresIntensity()
        {
            var plain = Render("glass='true' color='white/0.1' borderWidth='1'", "pugui-intensity-glass-unset.png");
            var lit = Render("glass='true' color='white/0.1' borderWidth='1' intensity='5'", "pugui-intensity-glass-k5.png");
            AssertPixelIdentical(plain, lit, "intensity has no effect on a glass surface (spec §5.4)");
        }
    }
}
