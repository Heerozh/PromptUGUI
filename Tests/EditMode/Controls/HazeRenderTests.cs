using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Does <c>haze</c> actually reach the pixels? Parameter tests read back <c>PanelParams</c> and
    /// stay green if the shader ignores the uniforms, so these probe the frame buffer — the same
    /// explicit <c>Camera.Render()</c> harness as <see cref="IntensityRenderTests"/>, extended to
    /// render an arbitrary Screen body (two panels side by side) and to pin the fog clock.
    ///
    /// <para>Every probe is a predicate on the design (spec 2026-09-17 haze §5), never a golden
    /// image: fog is irregular but soft, it obeys the colour ramp, it stays inside the shape and
    /// under the border, two panels at different positions get different fog, and the clock moves
    /// it only while it drifts.</para>
    /// </summary>
    public class HazeRenderTests
    {
        private const int Size = 256;

        /// <summary>Rect size of the probed Frame, in canvas units. Probes are normalised to it.</summary>
        private const float W = 120f;
        private const float H = 80f;

        private const string Dark = "#101828";

        private Camera _ui;
        private RenderTexture _uiRt;
        private Texture2D _shot;
        private Rect _rect;
        private PromptUGUI.Application.Screen _screen;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "HazeUIRT" };
            _ui = new GameObject("HazeUICamera").AddComponent<Camera>();
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

        // ---- harness ----------------------------------------------------------------------------

        /// <summary>Renders one centred Frame with <paramref name="attrs"/>; probes address it.</summary>
        private Color32[] Render(string attrs, string dumpName)
            => RenderBody($"<Frame id='f' anchor='center' width='{W}' height='{H}' {attrs}/>", dumpName);

        /// <summary>Loads a Screen with <paramref name="body"/> and renders it; probes address <c>id='f'</c>.</summary>
        private Color32[] RenderBody(string body, string dumpName, string probeId = "f")
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  {body}
</Screen></PromptUGUI>");
            _screen = UI.Open("S");

            var canvas = _screen.RootGameObject.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _ui;
            canvas.planeDistance = 10f;

            return Snapshot(dumpName, probeId);
        }

        /// <summary>Re-renders the Screen loaded by the last <see cref="RenderBody"/> as it is now.</summary>
        private Color32[] Snapshot(string dumpName, string probeId = "f")
        {
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
            Debug.Log($"PromptUGUI haze render dump: {path}");

            _rect = ScreenRectOf(probeId);
            Assert.Greater(_rect.width, W * 0.5f,
                "the probed rect rendered far smaller than its canvas size — probes would be meaningless");
            return _shot.GetPixels32();
        }

        private Rect ScreenRectOf(string id)
        {
            var rt = (RectTransform)_screen.Get<Frame>(id).GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            return Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y),
                                   Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));
        }

        private ProceduralPanel PanelOf(string id)
            => _screen.Get<Frame>(id).GameObject.GetComponent<ProceduralPanel>();

        /// <summary>Samples in normalised rect coordinates (0,0 = bottom-left); values outside 0..1
        /// reach past the rect, which is where a glow lives.</summary>
        private Color At(float u, float v) => _shot.GetPixel(Px(u), Py(v));

        private int Px(float u) => Mathf.RoundToInt(Mathf.LerpUnclamped(_rect.xMin, _rect.xMax, u));
        private int Py(float v) => Mathf.RoundToInt(Mathf.LerpUnclamped(_rect.yMin, _rect.yMax, v));

        /// <summary>Samples on the vertical centre line, <paramref name="outset"/> units LEFT of the rect.</summary>
        private Color LeftOutset(float outset) => At(-outset / W, 0.5f);

        private static float Luma(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        /// <summary>Every pixel of the probed rect between the normalised bounds, row-major.</summary>
        private List<Color> Region(float u0, float v0, float u1, float v1)
        {
            var result = new List<Color>();
            for (var y = Py(v0); y <= Py(v1); y++)
                for (var x = Px(u0); x <= Px(u1); x++)
                    result.Add(_shot.GetPixel(x, y));
            return result;
        }

        /// <summary>The interior of the rect, inset enough to miss the AA edge and any border.</summary>
        private List<Color> Interior(float inset = 0.08f) => Region(inset, inset, 1f - inset, 1f - inset);

        private static (float mean, float stdDev) LumaStats(List<Color> pixels)
        {
            float sum = 0f;
            foreach (var c in pixels) sum += Luma(c);
            var mean = sum / pixels.Count;
            float sq = 0f;
            foreach (var c in pixels) { var d = Luma(c) - mean; sq += d * d; }
            return (mean, Mathf.Sqrt(sq / pixels.Count));
        }

        private static void AssertPixelIdentical(Color32[] a, Color32[] b, string why)
        {
            Assert.AreEqual(a.Length, b.Length);
            for (var i = 0; i < a.Length; i++)
            {
                if (a[i].r == b[i].r && a[i].g == b[i].g && a[i].b == b[i].b && a[i].a == b[i].a) continue;
                Assert.Fail($"{why}: pixel {i % Size},{i / Size} differs — {a[i]} vs {b[i]}");
            }
        }

        private static bool AnyPixelDiffers(Color32[] a, Color32[] b)
        {
            for (var i = 0; i < a.Length; i++)
                if (a[i].r != b[i].r || a[i].g != b[i].g || a[i].b != b[i].b || a[i].a != b[i].a) return true;
            return false;
        }

        /// <summary>Within one 8-bit step per channel — "the same" after quantisation.</summary>
        private static void AssertSameColor(Color expected, Color actual, string why)
        {
            const float lsb = 1.5f / 255f;
            Assert.AreEqual(expected.r, actual.r, lsb, why);
            Assert.AreEqual(expected.g, actual.g, lsb, why);
            Assert.AreEqual(expected.b, actual.b, lsb, why);
        }

        // ---- off is off ------------------------------------------------------------------------

        [Test]
        public void HazeOff_IsPixelIdenticalToUnset()
        {
            // The uniform branch: haze='0' (and a stray hazeColor / hazeDrift with it) must take
            // exactly the path a fog-less panel takes, so existing documents keep rendering bit for bit.
            var unset = Render($"color='{Dark}' borderWidth='2' glow='20' glowColor='#3b82f6/0.5'",
                               "pugui-haze-unset.png");
            var off = Render($"color='{Dark}' borderWidth='2' glow='20' glowColor='#3b82f6/0.5' haze='0' hazeColor='cyan' hazeDrift='6'",
                             "pugui-haze-off.png");
            AssertPixelIdentical(unset, off, "haze='0' must be a no-op");
        }

        // ---- the fog itself: irregular, soft, brighter ----------------------------------------

        [Test]
        public void Haze_IsIrregularAndSoft()
        {
            Render($"color='{Dark}'", "pugui-haze-plain.png");
            var (plainMean, plainDev) = LumaStats(Interior());
            Assert.Less(plainDev, 0.005f, "guard: a flat fill has no variation");

            Render($"color='{Dark}' haze='24' hazeColor='white'", "pugui-haze-fog.png");
            var pixels = Interior();
            var (mean, dev) = LumaStats(pixels);

            Assert.Greater(mean, plainMean + 0.05f, "fog adds light");
            Assert.Greater(dev, 0.04f, $"fog is patchy, not a flat wash (std dev {dev})");

            // Soft: no hard edges anywhere. A nearest-neighbour lattice or a step() coverage map
            // would jump most of the way from the fill to white (Δluma ≈ 0.9) between two neighbours;
            // the steepest flank of a small patch is about a third of that.
            var w = Px(1f - 0.08f) - Px(0.08f) + 1;
            for (var i = 1; i < pixels.Count; i++)
            {
                if (i % w == 0) continue;   // row start
                var step = Mathf.Abs(Luma(pixels[i]) - Luma(pixels[i - 1]));
                Assert.Less(step, 0.5f, $"a hard edge between neighbouring pixels (Δluma {step})");
            }
        }

        [Test]
        public void Haze_IsNotUniform_AcrossTheWholeRect()
        {
            // Both a bright patch and a dark gap have to exist — the coverage map is meant to leave
            // most of the surface clear (spec §5.1), so the minimum has to stay near the fill.
            Render($"color='{Dark}' haze='24' hazeColor='white'", "pugui-haze-coverage.png");
            var pixels = Interior();
            float min = 1f, max = 0f;
            foreach (var c in pixels) { var l = Luma(c); min = Mathf.Min(min, l); max = Mathf.Max(max, l); }
            Assert.Greater(max, 0.4f, $"somewhere the fog is clearly bright (max {max})");
            Assert.Less(min, 0.2f, $"somewhere the fill shows through almost clear (min {min})");
        }

        // ---- density: sparse patches ↔ the raw field (H-D7) ----------------------------------

        [Test]
        public void HazeDensity_FillsTheSurface()
        {
            // At 1 every pixel carries fog (the raw cloud field, black point 0); at 0 the black
            // point is above the noise's mean, so a good part of the fill shows through untouched.
            Render($"color='{Dark}'", "pugui-haze-density-plain.png");
            var (plain, _) = LumaStats(Interior());

            Render($"color='{Dark}' haze='24' hazeColor='white' hazeDensity='1'", "pugui-haze-density-1.png");
            var full = Interior();
            float fullMin = 1f;
            foreach (var c in full) fullMin = Mathf.Min(fullMin, Luma(c));
            Assert.Greater(fullMin, plain + 0.08f, $"at density 1 nothing is bare fill (min luma {fullMin} vs fill {plain})");

            Render($"color='{Dark}' haze='24' hazeColor='white' hazeDensity='0'", "pugui-haze-density-0.png");
            var sparse = Interior();
            var bare = 0;
            foreach (var c in sparse) if (Luma(c) < plain + 0.01f) bare++;
            Assert.Greater(bare, sparse.Count / 4, $"at density 0 a good part of the surface is bare fill ({bare} of {sparse.Count})");

            var (fullMean, _) = LumaStats(full);
            var (sparseMean, _) = LumaStats(sparse);
            Assert.Greater(fullMean, sparseMean + 0.15f, "…and it is far denser overall");
        }

        [Test]
        public void HazeDensity_DefaultSitsBetween()
        {
            Render($"color='{Dark}' haze='24' hazeColor='white' hazeDensity='0'", "pugui-haze-density-0.png");
            var (sparse, _) = LumaStats(Interior());
            Render($"color='{Dark}' haze='24' hazeColor='white'", "pugui-haze-density-default.png");
            var (mid, _) = LumaStats(Interior());
            Render($"color='{Dark}' haze='24' hazeColor='white' hazeDensity='1'", "pugui-haze-density-1.png");
            var (full, _) = LumaStats(Interior());
            Assert.Greater(mid, sparse + 0.05f, $"default is denser than 0 ({sparse} < {mid})");
            Assert.Greater(full, mid + 0.05f, $"…and thinner than 1 ({mid} < {full})");
        }

        // ---- the colour slot is the directional mask (H-D2) -----------------------------------

        [Test]
        public void HazeColorRamp_FadesFromAnEdge()
        {
            // "to top, white, white/0 60%": fog seeps in from the bottom and is gone above 60%.
            // The bottom HALF is averaged rather than a thin band: patches are ~24 units across, so
            // a band a few units tall can land between two of them by chance.
            var plain = Render($"color='{Dark}'", "pugui-haze-ramp-plain.png");
            var plainTop = Region(0.1f, 0.75f, 0.9f, 0.95f);

            Render($"color='{Dark}' haze='24' hazeColor='to top, white, white/0 60%'", "pugui-haze-ramp.png");
            var (bottom, _) = LumaStats(Region(0.1f, 0.05f, 0.9f, 0.5f));
            var top = Region(0.1f, 0.75f, 0.9f, 0.95f);
            var (topMean, _) = LumaStats(top);

            Assert.Greater(bottom, topMean + 0.02f, $"the bottom half is foggy ({bottom}), the top is not ({topMean})");
            for (var i = 0; i < top.Count; i++)
                AssertSameColor(plainTop[i], top[i], "above the ramp's transparent end the fill is untouched");
        }

        // ---- where the fog may and may not go --------------------------------------------------

        [Test]
        public void Haze_DoesNotReachPastTheShape()
        {
            const string pill = "radius='pill' glow='16' glowColor='#3b82f6/0.5'";
            Render($"color='{Dark}' {pill}", "pugui-haze-shape-plain.png");
            var plainGlowNear = LeftOutset(4f);
            var plainGlowFar = LeftOutset(12f);
            var plainCornerBL = At(0.03f, 0.03f);
            var plainCornerTR = At(0.97f, 0.97f);

            Render($"color='{Dark}' {pill} haze='24' hazeColor='white'", "pugui-haze-shape.png");
            AssertSameColor(plainGlowNear, LeftOutset(4f), "the glow just outside the edge carries no fog");
            AssertSameColor(plainGlowFar, LeftOutset(12f), "the glow tail carries no fog");
            AssertSameColor(plainCornerBL, At(0.03f, 0.03f), "outside a pill's rounded corner is not the shape");
            AssertSameColor(plainCornerTR, At(0.97f, 0.97f), "outside a pill's rounded corner is not the shape");
        }

        [Test]
        public void Haze_IsUnderTheBorder()
        {
            // The border is painted over the fog, so an opaque border stays crisp whatever the fog does.
            Render($"color='{Dark}' borderWidth='6' borderColor='red'", "pugui-haze-border-plain.png");
            var plain = new List<Color>();
            for (var u = 0.1f; u <= 0.9f; u += 0.1f) plain.Add(At(u, 1f - 3f / H));

            Render($"color='{Dark}' borderWidth='6' borderColor='red' haze='24' hazeColor='white'", "pugui-haze-border.png");
            var i = 0;
            for (var u = 0.1f; u <= 0.9f; u += 0.1f)
                AssertSameColor(plain[i++], At(u, 1f - 3f / H), "inside the border band the fog is covered");
        }

        // ---- position is the seed (H-D3) --------------------------------------------------------

        [Test]
        public void TwoPanels_AtDifferentPositions_GetDifferentFog()
        {
            var fog = $"width='{W}' height='{H}' color='{Dark}' haze='24' hazeColor='white'";
            var body = $@"<Frame id='f' anchor='top-left' margin='8,_,_,8' {fog}/>
  <Frame id='g' anchor='bottom-right' margin='_,8,8,_' {fog}/>";

            RenderBody(body, "pugui-haze-two.png", "f");
            var a = Interior();
            _rect = ScreenRectOf("g");
            var b = Interior();

            Assert.AreEqual(a.Count, b.Count, "guard: same rect size ⇒ same sample count");
            var differs = false;
            for (var i = 0; i < a.Count && !differs; i++)
                differs = Mathf.Abs(Luma(a[i]) - Luma(b[i])) > 2f / 255f;
            Assert.IsTrue(differs, "same parameters, different position ⇒ different fog — with one shared material");
            Assert.AreSame(PanelOf("f").material, PanelOf("g").material,
                "the two panels still share one material: position is not in the key");
        }

        [Test]
        public void SamePanel_RendersTheSameFogTwice()
        {
            // Deterministic: no per-instance seed, no frame counter — the same place at the same
            // time is always the same picture.
            var first = Render($"color='{Dark}' haze='24' hazeColor='white'", "pugui-haze-det-1.png");
            var second = Snapshot("pugui-haze-det-2.png");
            AssertPixelIdentical(first, second, "fog must be stable frame to frame");
        }

        // ---- lighting and glass ----------------------------------------------------------------

        [Test]
        public void Intensity_LightsTheFog()
        {
            // Fog is painted before exposure, so intensity whitens its brightest patches: cyan
            // has r = 0, and only the over-exposure crosstalk can raise it.
            const string fog = "color='#0b1a33' haze='24' hazeColor='#00e5ff'";
            Render(fog, "pugui-haze-lit-k1.png");
            float plainMaxR = 0f;
            foreach (var c in Interior()) plainMaxR = Mathf.Max(plainMaxR, c.r);

            Render(fog + " intensity='3'", "pugui-haze-lit-k3.png");
            float litMaxR = 0f;
            foreach (var c in Interior()) litMaxR = Mathf.Max(litMaxR, c.r);

            Assert.Less(plainMaxR, 0.1f, $"unlit cyan fog has no red (max r {plainMaxR})");
            Assert.Greater(litMaxR, 0.2f, $"lit fog whitens at its peaks (max r {litMaxR})");
        }

        [Test]
        public void Glass_IgnoresHaze()
        {
            var plain = Render("glass='true' color='white/0.1' borderWidth='1'", "pugui-haze-glass-plain.png");
            var fog = Render("glass='true' color='white/0.1' borderWidth='1' haze='24' hazeColor='white'", "pugui-haze-glass-fog.png");
            AssertPixelIdentical(plain, fog, "haze has no effect on a glass surface (H-D4)");
        }

        // ---- the clock (M1) --------------------------------------------------------------------

        [Test]
        public void HazeDrift_MovesWithTheClock()
        {
            Render($"color='{Dark}' haze='24' hazeColor='white' hazeDrift='20'", "pugui-haze-drift-t0.png");
            HazeClock.SetTimeForTests(0f);
            var t0 = Snapshot("pugui-haze-drift-t0.png");
            HazeClock.SetTimeForTests(2f);
            var t2 = Snapshot("pugui-haze-drift-t2.png");
            Assert.IsTrue(AnyPixelDiffers(t0, t2), "drifting fog moves when the clock moves");
        }

        [Test]
        public void NoDrift_IgnoresTheClock()
        {
            Render($"color='{Dark}' haze='24' hazeColor='white'", "pugui-haze-still-t0.png");
            HazeClock.SetTimeForTests(0f);
            var t0 = Snapshot("pugui-haze-still-t0.png");
            HazeClock.SetTimeForTests(2f);
            var t2 = Snapshot("pugui-haze-still-t2.png");
            AssertPixelIdentical(t0, t2, "still fog does not move with the clock");
        }

        [Test]
        public void Disabled_FreezesTheFog()
        {
            // Grey and still: a disabled surface reads as inert (H-D5).
            Render($"color='{Dark}' haze='24' hazeColor='#00e5ff' hazeDrift='20'", "pugui-haze-disabled.png");
            PanelOf("f").SetDisabledGrayscale(true);
            HazeClock.SetTimeForTests(0f);
            var t0 = Snapshot("pugui-haze-disabled-t0.png");
            HazeClock.SetTimeForTests(2f);
            var t2 = Snapshot("pugui-haze-disabled-t2.png");
            AssertPixelIdentical(t0, t2, "a disabled panel's fog does not flow");
        }
    }
}
