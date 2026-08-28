using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Does the inner glow actually reach the pixels? Every parameter test in this feature reads
    /// back <c>PanelParams</c>, and all of them stay green if the shader ignores the two new
    /// uniforms — the same failure mode the corner treatments hit (grammar green, uniforms green,
    /// SDF unchanged).
    ///
    /// <para>Each probe is a predicate on the falloff rather than a golden image: the glow is
    /// <c>saturate(1 + d/size)²</c> over a dark fill, so brightness must fall monotonically inwards
    /// from the edge and be back to the fill by the time the band ends. Same explicit
    /// <c>Camera.Render()</c> harness as <see cref="CornerTreatmentRenderTests"/>.</para>
    /// </summary>
    public class InnerGlowRenderTests
    {
        private const int Size = 256;

        /// <summary>Rect size of the probed Frame, in canvas units. Probes are normalised to it.</summary>
        private const float W = 200f;
        private const float H = 160f;

        /// <summary>Glow band width used by most probes; wide enough to sample several points in.</summary>
        private const float Band = 40f;

        private Camera _ui;
        private RenderTexture _uiRt;
        private Texture2D _shot;
        private Rect _rect;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "InnerGlowUIRT" };
            _ui = new GameObject("InnerGlowUICamera").AddComponent<Camera>();
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
        private void Render(string attrs, string dumpName)
        {
            UI.UnloadAll();
            // A near-black fill so the glow is the only thing that can brighten a probe.
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' anchor='center' width='{W}' height='{H}' color='#101010' {attrs}/>
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
            Debug.Log($"PromptUGUI inner-glow render dump: {path}");

            var rt = (RectTransform)screen.Get<Frame>("f").GameObject.transform;
            var corners = new Vector3[4];
            rt.GetWorldCorners(corners);
            var a = RectTransformUtility.WorldToScreenPoint(_ui, corners[0]);
            var c = RectTransformUtility.WorldToScreenPoint(_ui, corners[2]);
            _rect = Rect.MinMaxRect(Mathf.Min(a.x, c.x), Mathf.Min(a.y, c.y),
                                    Mathf.Max(a.x, c.x), Mathf.Max(a.y, c.y));

            Assert.Greater(_rect.width, W * 0.5f,
                "the probed rect rendered far smaller than its canvas size — probes would be meaningless");
        }

        /// <summary>Samples in normalised rect coordinates (0,0 = bottom-left).</summary>
        private Color At(float u, float v)
            => _shot.GetPixel(Mathf.RoundToInt(Mathf.Lerp(_rect.xMin, _rect.xMax, u)),
                              Mathf.RoundToInt(Mathf.Lerp(_rect.yMin, _rect.yMax, v)));

        /// <summary>Samples on the vertical centre line, <paramref name="inset"/> units in from the left edge.</summary>
        private Color LeftInset(float inset) => At(inset / W, 0.5f);

        private static float Luma(Color c) => c.r * 0.299f + c.g * 0.587f + c.b * 0.114f;

        // ---- the falloff ------------------------------------------------------------------------

        [Test]
        public void InnerGlow_BrightensTheEdgeAndFadesInwards()
        {
            Render($"innerGlow='{Band}'", "pugui-inner-glow-falloff.png");

            var edge = Luma(LeftInset(3f));
            var mid = Luma(LeftInset(Band * 0.5f));
            var past = Luma(LeftInset(Band * 1.6f));
            var centre = Luma(At(0.5f, 0.5f));

            Assert.Greater(edge, 0.4f, $"the edge must be lit by the glow, got {edge:F3}");
            Assert.Less(mid, edge, $"…and fade inwards: mid {mid:F3} should be under edge {edge:F3}");
            Assert.Greater(mid, past, $"…monotonically: past-band {past:F3} should be under mid {mid:F3}");
            Assert.Less(past, 0.15f,
                $"…and be back to the near-black fill outside the band, got {past:F3}");
            Assert.AreEqual(centre, past, 0.03f, "past the band is indistinguishable from the centre");
        }

        [Test]
        public void WithoutInnerGlow_TheEdgeIsJustTheFill()
        {
            // The baseline the falloff test is measured against — and the guard that a stray
            // uniform default does not light every panel in the library.
            Render("", "pugui-inner-glow-none.png");
            Assert.Less(Luma(LeftInset(3f)), 0.15f, "no innerGlow ⇒ no rim");
        }

        [Test]
        public void InnerGlowColor_TintsTheRim()
        {
            Render($"innerGlow='{Band}' innerGlowColor='#ff0000'", "pugui-inner-glow-red.png");
            var edge = LeftInset(3f);
            Assert.Greater(edge.r, 0.4f, $"expected a red rim, got {edge}");
            Assert.Less(edge.g, 0.25f, $"…red, not white — got {edge}");
        }

        [Test]
        public void InnerGlowAlpha_ScalesTheStrength()
        {
            Render($"innerGlow='{Band}'", "pugui-inner-glow-full.png");
            var full = Luma(LeftInset(3f));
            Render($"innerGlow='{Band}' innerGlowColor='white/0.3'", "pugui-inner-glow-weak.png");
            var weak = Luma(LeftInset(3f));

            Assert.Less(weak, full * 0.7f,
                $"/alpha is the strength knob: {weak:F3} should be well under {full:F3}");
            Assert.Greater(weak, 0.05f, "…but still visible");
        }

        // ---- it stays inside, and under the border ----------------------------------------------

        [Test]
        public void InnerGlow_DrawsNothingOutsideTheShape()
        {
            // The mirror of the outer glow, and the reason the quad is not inflated: everything it
            // paints is within the rect.
            Render($"radius='40' innerGlow='{Band}'", "pugui-inner-glow-inside-only.png");
            Assert.Less(Luma(At(0.01f, 0.99f)), 0.1f,
                "the rect corner lies outside a radius-40 shape and must stay background");
        }

        [Test]
        public void OpaqueBorder_CoversTheOutermostPixelsOfTheBand()
        {
            // Measured from the shape edge (Photoshop Inner Glow), so a border painted afterwards
            // sits on top of the brightest part of it — spec §5.3.
            Render($"innerGlow='{Band}' borderWidth='10' borderColor='#0000ff'",
                   "pugui-inner-glow-under-border.png");

            var border = LeftInset(4f);
            Assert.Greater(border.b, 0.5f, $"the border must paint over the glow, got {border}");
            Assert.Less(border.r, 0.35f, $"…opaquely — got {border}");
            Assert.Greater(Luma(LeftInset(14f)), Luma(LeftInset(Band * 1.6f)),
                "…while the glow continues inwards past the border band");
        }

        // ---- it follows the outline, whatever the outline is -------------------------------------

        [Test]
        public void InnerGlow_FollowsAChamferedOutline()
        {
            // Derived from the same SDF, so the corner vocabulary needs no attributes of its own.
            // Probe just inside the chamfer's midpoint, where a rect-shaped glow would leave the
            // fill dark.
            Render($"radius='cut 60' innerGlow='{Band}'", "pugui-inner-glow-cut.png");

            var nearChamfer = At(36f / W, (H - 36f) / H);
            Assert.Greater(Luma(nearChamfer), 0.3f,
                $"the light must hug the chamfer, not the rect corner behind it, got {nearChamfer}");
        }

        [Test]
        public void InnerGlow_FollowsAHexagonOutline()
        {
            Render($"radius='hexagon' innerGlow='{Band}'", "pugui-inner-glow-hexagon.png");

            // Just inside the left tip: on a rect outline this column is 0.06W from the edge and
            // would be lit anyway, so the discriminating probe is the one further in along the
            // slanted edge, near the top where a hexagon has already cut the shape away.
            Assert.Greater(Luma(At(0.10f, 0.5f)), 0.3f, "the tip is lit");
            Assert.Greater(Luma(At(0.5f, 0.93f)), 0.3f, "…and so is the flat top edge between the tips");
        }
    }
}
