using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Do the corner treatments actually change the pixels? The parser tests prove the grammar
    /// reaches <c>RadiusSpec</c> and the material tests prove it reaches the shader's uniforms —
    /// and both stay green if the SDF ignores what it is handed. Only a render catches that.
    ///
    /// <para>Every probe is a geometric predicate rather than a golden image: for a treatment of
    /// reach R at a corner, the diagonal boundary sits at 0.293R for <c>round</c>, 0.5R for
    /// <c>cut</c> and 1.0R for <c>notch</c>, so one probe at 0.4R and one at 0.7R tell the three
    /// apart with several pixels of margin either way. Same explicit <c>Camera.Render()</c> harness
    /// as <see cref="ProceduralSurfaceRenderTests"/>.</para>
    /// </summary>
    public class CornerTreatmentRenderTests
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
            _uiRt = new RenderTexture(Size, Size, 24) { name = "CornerTreatmentUIRT" };
            _ui = new GameObject("CornerTreatmentUICamera").AddComponent<Camera>();
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
            Debug.Log($"PromptUGUI corner-treatment render dump: {path}");

            var rt = (RectTransform)screen.Get<Frame>("f").GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            _rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y),
                                   Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));

            // A canvas scale surprise would shrink the rect until every probe lands on background
            // and every "is it painted" assertion passes for the wrong reason.
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

        /// <summary>Samples the rendered Frame in normalised rect coordinates (0,0 = bottom-left).</summary>
        private Color At(float u, float v)
            => _shot.GetPixel(Mathf.RoundToInt(Mathf.Lerp(_rect.xMin, _rect.xMax, u)),
                              Mathf.RoundToInt(Mathf.Lerp(_rect.yMin, _rect.yMax, v)));

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

        private const float R = 60f;

        // ---- the three treatments are told apart by the same two probes ----------------------

        [Test]
        public void Round_FillsBothDiagonalProbes()
        {
            // Guard as much as baseline: the round path must keep drawing exactly what it drew
            // before corner treatments existed.
            Render($"radius='{R}'", "pugui-corner-round.png");
            AssertPainted(AtCornerInset(0.4f * R, 0.4f * R, true, true),
                          "a round corner's diagonal boundary is at 0.293R, so 0.4R is inside");
            AssertPainted(AtCornerInset(0.7f * R, 0.7f * R, true, true), "…and so is 0.7R");
            AssertBackground(AtCornerInset(0.1f * R, 0.1f * R, true, true),
                             "…while 0.1R is still outside the arc");
        }

        [Test]
        public void Cut_RemovesTheCornerAlongAStraightLine()
        {
            Render($"radius='cut {R}'", "pugui-corner-cut.png");
            AssertBackground(AtCornerInset(0.4f * R, 0.4f * R, true, true),
                             "a 45° cut's diagonal boundary is at 0.5R, so 0.4R must be gone");
            AssertPainted(AtCornerInset(0.7f * R, 0.7f * R, true, true),
                          "…and 0.7R must survive — this is what separates cut from notch");
        }

        [Test]
        public void Notch_RemovesTheWholeCornerSquare()
        {
            Render($"radius='notch {R}'", "pugui-corner-notch.png");
            AssertBackground(AtCornerInset(0.7f * R, 0.7f * R, true, true),
                             "the whole R x R square is bitten out, so 0.7R is inside the bite");
            AssertPainted(AtCornerInset(1.3f * R, 1.3f * R, true, true),
                          "…and everything past it is untouched");
            AssertPainted(AtCornerInset(1.3f * R, 0.3f * R, true, true),
                          "…including the strip that runs along the top edge past the bite");
        }

        [Test]
        public void PerCornerMixing_TreatsEachCornerOnItsOwn()
        {
            Render($"radius='cut {R}, {R}, cut {R}, {R}'", "pugui-corner-mixed.png");
            AssertBackground(AtCornerInset(0.4f * R, 0.4f * R, true, true), "top-left is cut");
            AssertPainted(AtCornerInset(0.4f * R, 0.4f * R, false, true), "top-right is round");
            AssertBackground(AtCornerInset(0.4f * R, 0.4f * R, false, false), "bottom-right is cut");
            AssertPainted(AtCornerInset(0.4f * R, 0.4f * R, true, false), "bottom-left is round");
        }

        // ---- two axes, and which is which -----------------------------------------------------

        [Test]
        public void CutWidthByHeight_ReachesFurtherAlongTheAxisItNames()
        {
            // Swapping the axes would flip both probes, so this pins the order as well as the shape.
            Render("radius='cut 80x30'", "pugui-corner-cut-wxh.png");
            AssertBackground(AtCornerInset(40f, 8f, true, true),
                             "80 wide means 40 in from the left is still inside the cut near the top");
            AssertPainted(AtCornerInset(8f, 40f, true, true),
                          "…while 30 tall means 40 down the side is already past it");
        }

        // ---- whole-shape sentinels --------------------------------------------------------------

        [Test]
        public void Hexagon_DrawsTipsAtTheVerticalCentre()
        {
            Render("radius='hexagon'", "pugui-corner-hexagon.png");
            AssertPainted(At(0.06f, 0.5f), "the left tip is filled at mid height");
            AssertBackground(At(0.10f, 0.25f), "…and the same column is cut away below the tip");
            AssertBackground(At(0.06f, 0.75f), "…and above it — a point, not an edge");
            AssertPainted(At(0.5f, 0.95f), "…while the flat top edge between the two tips stays");
        }

        [Test]
        public void HexagonWithSize_ControlsHowFarTheTipReachesIn()
        {
            // The bare keyword takes half the height (80 units here), which cuts this probe away —
            // see the previous test's second assertion, which is the same point.
            Render("radius='hexagon 20'", "pugui-corner-hexagon-sized.png");
            AssertPainted(At(0.10f, 0.25f),
                          "a 20-unit tip leaves standing what the 80-unit default cuts off");
            AssertPainted(At(0.06f, 0.5f), "…and it is still a tip at mid height");
        }

        // ---- what the corner SDF has to be accurate about, not just shaped like -----------------

        [Test]
        public void CutBorder_KeepsItsWidthAlongTheChamfer()
        {
            // The chamfer runs from 60 in along the top to 60 down the side, so its midpoint is at
            // inset (30, 30) and its inward normal is the diagonal. Walking in from there crosses
            // the border band at 8 units — if the field were scaled anywhere along the chamfer, one
            // of these two probes would land on the wrong side.
            Render($"radius='cut {R}' borderWidth='8' borderColor='white'", "pugui-corner-cut-border.png");
            AssertBorder(AtCornerInset(32.83f, 32.83f), "4 units in from the chamfer is inside the border");
            AssertPainted(AtCornerInset(44.14f, 44.14f), "…and 20 units in is past it, back to the fill");
        }

        [Test]
        public void NotchBorder_DoesNotFattenAtTheReflexCorner()
        {
            // Union-by-min reads up to sqrt(2) too shallow at the inner corner of a notch, which
            // would push the border's inner edge out from 20 units to 28. The second probe sits in
            // that window: it is fill under the exact field and border under the sloppy one.
            Render($"radius='notch {R}' borderWidth='20' borderColor='white'",
                   "pugui-corner-notch-border.png");
            AssertBorder(AtCornerInset(67.07f, 67.07f), "10 units in from the reflex corner is border");
            AssertPainted(AtCornerInset(76.97f, 76.97f),
                          "…and 24 units in is fill — the border must not have stretched to 28");
        }

        [Test]
        public void Pill_StillRoundsBothEnds()
        {
            Render("radius='pill'", "pugui-corner-pill.png", 200f, 80f);
            AssertPainted(At(0.5f, 0.5f), "the middle of a pill is filled");
            AssertBackground(At(0.005f, 0.98f), "…and its corners are not");
        }
    }
}
