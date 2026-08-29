using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Does <c>rN</c> actually round the vertices? The parser tests prove the number reaches
    /// <c>RadiusSpec</c>, the material tests prove it reaches <c>_CornerFillet</c> — and both stay
    /// green while the SDF ignores it. Only a render catches that.
    ///
    /// <para>Every probe is a geometric predicate worked out from the spec's construction
    /// (2026-08-29 §5 / §10.1): a fillet of radius r replaces the vertex with an arc centred on the
    /// eroded polygon's vertex, so the pixels between that arc and the old sharp vertex are the ones
    /// that flip. Same explicit <c>Camera.Render()</c> harness as
    /// <see cref="CornerTreatmentRenderTests"/>.</para>
    /// </summary>
    public class CornerFilletRenderTests
    {
        private const int Size = 256;

        /// <summary>Rect size of the probed Frame, in canvas units. Probes are normalised to it.</summary>
        private const float W = 200f;
        private const float H = 160f;

        private Camera _ui;
        private RenderTexture _uiRt;
        private Texture2D _shot;
        private Rect _rect;
        private float _w = W;
        private float _h = H;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "CornerFilletUIRT" };
            _ui = new GameObject("CornerFilletUICamera").AddComponent<Camera>();
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
        private void Render(string attrs, string dumpName, float width = W, float height = H)
        {
            _w = width;
            _h = height;
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' anchor='center' width='{width}' height='{height}' color='#3366ff' {attrs}/>
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
            Debug.Log($"PromptUGUI corner-fillet render dump: {path}");

            var rt = (RectTransform)screen.Get<Frame>("f").GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            _rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y),
                                   Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));

            Assert.Greater(_rect.width, width * 0.5f,
                "the probed rect rendered far smaller than its canvas size — probes would be meaningless");
        }

        /// <summary>Samples the rendered Frame at a point given in canvas units in from a corner.</summary>
        private Color AtCornerInset(float insetX, float insetY, bool left = true, bool top = true)
        {
            var u = (left ? insetX : _w - insetX) / _w;
            var v = (top ? _h - insetY : insetY) / _h;
            return At(u, v);
        }

        /// <summary>
        /// Samples the rendered Frame in normalised rect coordinates (0,0 = bottom-left). Unclamped
        /// on purpose: the "just outside the edge" probes go slightly past 0 / 1, and
        /// <see cref="Mathf.Lerp"/> would silently snap them back onto the edge pixel.
        /// </summary>
        private Color At(float u, float v)
            => _shot.GetPixel(Mathf.RoundToInt(Mathf.LerpUnclamped(_rect.xMin, _rect.xMax, u)),
                              Mathf.RoundToInt(Mathf.LerpUnclamped(_rect.yMin, _rect.yMax, v)));

        private Color32[] Pixels() => _shot.GetPixels32();

        /// <summary>FNV-1a over the RGBA bytes: a fingerprint for the zero-regression measurement.</summary>
        private ulong PixelHash()
        {
            const ulong prime = 1099511628211UL;
            var h = 14695981039346656037UL;
            foreach (var px in Pixels())
            {
                h = (h ^ px.r) * prime;
                h = (h ^ px.g) * prime;
                h = (h ^ px.b) * prime;
                h = (h ^ px.a) * prime;
            }
            return h;
        }

        // Both halves matter: the white border is as blue as the fill is, so a blue-only test
        // cannot tell "filled" from "the border grew over this point".
        private static void AssertPainted(Color c, string what)
        {
            Assert.Greater(c.b, 0.5f, $"{what} — expected the blue fill, got {c}");
            Assert.Less(c.r, 0.5f, $"{what} — expected the fill, but this is the white border: {c}");
        }

        private static void AssertBackground(Color c, string what)
            => Assert.Less(c.b, 0.2f, $"{what} — expected background, got {c}");

        private static void AssertBorder(Color c, string what)
            => Assert.Greater(c.r, 0.6f, $"{what} — expected the white border, got {c}");

        // ---- the vertex goes, the edges stay -----------------------------------------------------

        // A 20-wide, 80-tall chamfer on the top-left corner only. 80 is half the height, so the
        // chamfer reaches the vertical centre line — the exact configuration of the nav tab in the
        // spec's §1, which exercises the extents rule (§5.3) as well as the fillet itself.
        //
        // Geometry (canvas insets from the top-left corner): the chamfer meets the top edge at
        // (20, 0) with a 104° interior angle. With r=40: k = 0.56, s' = (11.2, 44.9), the extents
        // rule pulls it to (10, 40), and the eroded vertex — the arc's centre — sits at (50, 40).
        // Along the vertex's bisector (0.616, 0.788) the arc is 9+ units from the old vertex.
        private const string SteepCut = "cut 20x80";
        private const string SteepCutFilleted = "cut 20x80 r40";

        [Test]
        public void Fillet_RoundsTheChamferVertex()
        {
            Render($"radius='{SteepCut}, 0, 0, 0'", "pugui-fillet-sharp.png");
            AssertPainted(AtCornerInset(22.5f, 3.2f),
                          "4 units inside the sharp 104° vertex is fill (3.2 from either edge)");

            Render($"radius='{SteepCutFilleted}, 0, 0, 0'", "pugui-fillet-cut.png");
            AssertBackground(AtCornerInset(22.5f, 3.2f),
                             "…and the fillet arc (46 units from its centre, r=40) has taken it away");
            AssertPainted(AtCornerInset(29.9f, 12.6f),
                          "…while 16 units along the bisector is 34 from the centre: inside the arc");
        }

        [Test]
        public void Fillet_LeavesTheStraightEdgesInPlace()
        {
            // Opening = erode then dilate: every straight edge comes back exactly where it was.
            Render($"radius='{SteepCutFilleted}, 0, 0, 0'", "pugui-fillet-edges.png");
            AssertPainted(At(0.5f, 1f - 2f / H), "2 units inside the top edge, mid-way, is fill");
            AssertBackground(At(0.5f, 1f + 2f / H), "…and 2 units above it is background");
            AssertPainted(At(2f / W, 0.25f), "2 units inside the left edge below the centre line is fill");
            AssertBackground(At(-2f / W, 0.25f), "…and 2 units outside it is background");
            AssertPainted(At(0.5f, 2f / H), "the untouched bottom edge is where it was");
        }

        [Test]
        public void Fillet_BeyondTheChamfersCapacity_IsExactlyARoundCorner()
        {
            // §5.2: once r ≥ W·H / (W+H−L) the two arcs have eaten the whole chamfer and the corner
            // is the plain round corner of radius r — same instructions, so pixel-identical.
            Render("radius='60'", "pugui-fillet-round-ref.png");
            var round = Pixels();
            Render("radius='cut 20 r60'", "pugui-fillet-degenerate.png");
            var degenerate = Pixels();

            var differing = 0;
            for (var i = 0; i < round.Length; i++)
                if (!round[i].Equals(degenerate[i])) differing++;
            Assert.AreEqual(0, differing,
                "'cut 20 r60' must degrade to exactly radius='60' — this is the continuum promise");
        }

        // ---- extents are preserved (§5.3) --------------------------------------------------------

        [Test]
        public void HexagonFillet_KeepsTheTipOnTheRectEdge()
        {
            // The bare hexagon on 200x160 has 45° tips. Rounding with r=30 keeps the apex on the
            // rect edge (the chamfers move out a hair instead), so at 8 units above the centre the
            // rounded outline is at inset 1.1 where the sharp one was at inset 8.
            Render("radius='hexagon'", "pugui-fillet-hexagon-sharp.png");
            AssertPainted(At(2f / W, 0.5f), "the sharp tip reaches the edge at mid height");
            AssertBackground(At(4f / W, 0.5f + 8f / H), "…and 8 up, 4 in is outside the 45° flank");

            Render("radius='hexagon r30'", "pugui-fillet-hexagon.png");
            AssertPainted(At(2f / W, 0.5f), "the rounded tip still reaches the edge at mid height");
            AssertPainted(At(4f / W, 0.5f + 8f / H),
                          "…and the blunter tip now covers the point the sharp flank left bare");
        }

        // ---- notch: both the mouth and the reflex corner (§5.4) -----------------------------------

        [Test]
        public void NotchFillet_RoundsTheReflexCorner()
        {
            // r=16 on a 40x40 bite: the concave arc is centred 24 in from either wall. (37, 37) is
            // inside the sharp bite but 18.4 from that centre — outside the arc, so it is now fill.
            Render("radius='notch 40'", "pugui-fillet-notch-sharp.png");
            AssertBackground(AtCornerInset(37f, 37f), "3 units inside both walls is in the sharp bite");

            Render("radius='notch 40 r16'", "pugui-fillet-notch.png");
            AssertPainted(AtCornerInset(37f, 37f), "…and the concave fillet has filled it in");
            AssertBackground(AtCornerInset(30f, 30f), "…while the bite's middle is still bitten out");
        }

        [Test]
        public void NotchFillet_RoundsTheMouth()
        {
            // The mouth vertex (40, 0) becomes an arc centred at (56, 16): at 2 units below the top
            // edge the wall has retreated from inset 40 to inset 48.
            Render("radius='notch 40'", "pugui-fillet-notch-sharp.png");
            AssertPainted(AtCornerInset(44f, 2f), "4 units past the sharp wall, near the top, is fill");

            Render("radius='notch 40 r16'", "pugui-fillet-notch.png");
            AssertBackground(AtCornerInset(44f, 2f), "…and the mouth fillet has opened it up");
            AssertPainted(AtCornerInset(60f, 2f), "…but the edge past the fillet's tangent point stays");
        }

        // ---- the border follows the arc at full width -------------------------------------------

        [Test]
        public void FilletBorder_KeepsItsWidthAroundTheArc()
        {
            // Arc centre (50, 40), r=40; the arc's mid-normal (between 'up' and the chamfer normal)
            // is (−0.615, −0.788) in inset space. 4 units in along it is border, 12 units in is fill.
            Render($"radius='{SteepCutFilleted}, 0, 0, 0' borderWidth='8' borderColor='white'",
                   "pugui-fillet-border.png");
            AssertBorder(AtCornerInset(27.9f, 11.6f), "4 units inside the arc is border");
            AssertPainted(AtCornerInset(32.8f, 17.9f), "…and 12 units inside is past it, back to fill");
        }

        // ---- zero-regression measurement ---------------------------------------------------------

        [Test]
        public void Baseline_LogsPixelHashes_ForZeroRegressionMeasurement()
        {
            // Not an assertion: a fingerprint per untouched shape, logged so the same run before and
            // after a shader change can be compared (corner spec §11.1 did this by hand). Anything
            // without rN must hash identically across the fillet change.
            var cases = new (string attrs, float w, float h)[]
            {
                ("radius='60'", W, H),
                ("radius='cut 60'", W, H),
                ("radius='cut 80x30'", W, H),
                ("radius='notch 60'", W, H),
                ("radius='hexagon'", W, H),
                ("radius='hexagon 20'", W, H),
                ("radius='pill'", W, 80f),
                ("radius='cut 60' borderWidth='8' borderColor='white'", W, H),
                ("radius='notch 60' borderWidth='20' borderColor='white'", W, H),
                ("radius='cut 60, 60, notch 30, 0' glow='12' innerGlow='10'", W, H),
            };
            foreach (var (attrs, w, h) in cases)
            {
                Render(attrs, "pugui-fillet-baseline.png", w, h);
                Debug.Log($"PUGUI-PIXHASH {attrs} => {PixelHash():x16}");
            }
        }
    }
}
