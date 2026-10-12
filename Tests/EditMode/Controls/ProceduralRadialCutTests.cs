using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The radial cut and the ring on a <see cref="ProceduralPanel"/> (spec 2026-10-13 §5.2–§5.3) —
    /// what <c>&lt;Cooldown&gt;</c> draws with. The shape is intersected with a sector INSIDE the
    /// SDF, so a square is swept corners and all and the border / glow follow the cut edges; the
    /// sector rides <c>uv1.zw</c> like the linear cut, the ring width and the radial switch ride the
    /// material.
    /// </summary>
    public class ProceduralRadialCutTests
    {
        private const int Size = 256;
        private const float Pi = Mathf.PI;

        private Camera _ui;
        private RenderTexture _uiRt;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "RadialCutUIRT" };
            _ui = new GameObject("RadialCutUICamera").AddComponent<Camera>();
            _ui.clearFlags = CameraClearFlags.SolidColor;
            _ui.backgroundColor = Color.black;
            _ui.targetTexture = _uiRt;
            _ui.cullingMask = ~0;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            if (_ui != null) Object.DestroyImmediate(_ui.gameObject);
            if (_uiRt != null)
            {
                _uiRt.Release();
                Object.DestroyImmediate(_uiRt);
            }
        }

        // A 160×160 square centred on a 256×256 canvas at one unit per pixel:
        // x, y ∈ [48, 208], centre (128, 128), half-size 80.
        private const int Left = 48, Right = 208, Bottom = 48, Top = 208, Mid = 128;

        private ProceduralPanel Open(string frameAttrs)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' anchor='center' width='160' height='160' {frameAttrs}/>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");

            var canvas = screen.RootGameObject.GetComponentInParent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = _ui;
            canvas.planeDistance = 10f;

            var panel = screen.Get<Frame>("f").GameObject.GetComponent<ProceduralPanel>();
            Assert.IsNotNull(panel, "the Frame must have gone procedural");
            return panel;
        }

        private Texture2D Render(string dumpName)
        {
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
            Debug.Log($"PromptUGUI radial-cut render dump: {path}");
            return tex;
        }

        private static void AssertRed(Color c, string where)
        {
            Assert.Greater(c.r, 0.5f, $"{where}: expected the red fill, got {c}");
            Assert.Less(c.g, 0.3f, $"{where}: expected the red fill, got {c}");
        }

        private static void AssertBlack(Color c, string where)
        {
            Assert.Less(c.r + c.g + c.b, 0.1f, $"{where}: expected bare background, got {c}");
        }

        private static void AssertGreenGlow(Color c, string where)
        {
            Assert.Greater(c.g, 0.12f, $"{where}: expected the green glow, got {c}");
            Assert.Less(c.r, c.g, $"{where}: expected the glow, not the fill, got {c}");
        }

        private static UIVertex[] Mesh(ProceduralPanel panel)
        {
            using var vh = new VertexHelper();
            panel.BuildMeshForTests(vh);
            var verts = new UIVertex[vh.currentVertCount];
            for (var i = 0; i < verts.Length; i++) vh.PopulateUIVertex(ref verts[i], i);
            return verts;
        }

        // ───── vertex channels / material ─────

        [Test]
        public void SetRadialCut_WritesTheCentreAndTheHalfAngleIntoUv1()
        {
            // A circular ring, so the round-capped form below is not demoted to flat ends.
            var panel = Open("radius='pill' color='#ff0000'");
            panel.SetRingWidth(8f);

            panel.SetRadialCut(0.125f, Pi / 4f);
            var v = Mesh(panel);
            Assert.AreEqual(4, v.Length);
            foreach (var vert in v)
            {
                Assert.AreEqual(5.125f, vert.uv1.z, 1e-5f, "flat radial = 5 + centre in turns");
                Assert.AreEqual(Pi / 4f, vert.uv1.w, 1e-5f, "e is the half-angle in radians");
                Assert.AreEqual(80f, vert.uv1.x, 1e-3f, "the half-size channel is untouched");
                Assert.AreEqual(80f, vert.uv1.y, 1e-3f);
            }

            panel.SetRadialCut(0.125f, Pi / 4f, roundCap: true);
            Assert.AreEqual(7.125f, Mesh(panel)[0].uv1.z, 1e-5f, "round-capped radial = 7 + centre");

            panel.SetRadialCut(1.25f, Pi / 2f);
            Assert.AreEqual(5.25f, Mesh(panel)[0].uv1.z, 1e-5f, "the centre wraps to one turn");
            panel.SetRadialCut(-0.25f, Pi / 2f);
            Assert.AreEqual(5.75f, Mesh(panel)[0].uv1.z, 1e-5f, "…in both directions");
        }

        [Test]
        public void ARoundCap_OffACircularRing_IsWrittenAsTheFlatCode()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("cap=\"round\""));
            var panel = Open("radius='12' color='#ff0000'");
            panel.SetRingWidth(8f);
            panel.SetRadialCut(0.125f, Pi / 4f, roundCap: true);
            Assert.AreEqual(5.125f, Mesh(panel)[0].uv1.z, 1e-5f,
                "sdArc is exact only for a circle; a rounded square keeps flat ends");

            panel.SetRingWidth(0f);
            Assert.AreEqual(5.125f, Mesh(panel)[0].uv1.z, 1e-5f, "…and a solid sector has no ends to cap");
        }

        [Test]
        public void AFullSector_IsNoCut_AndAnEmptyOne_IsNoGeometry()
        {
            var panel = Open("color='#ff0000' glow='8'");

            panel.SetRadialCut(0.5f, Pi);
            Assert.AreEqual(0f, Mesh(panel)[0].uv1.z, "a full turn is the uncut shape");

            panel.SetRadialCut(0.5f, 0f);
            Assert.IsFalse(panel.IsPanelVisible, "an empty sector draws nothing — not even the glow");
            Assert.AreEqual(0, Mesh(panel).Length);

            panel.SetRadialCut(0.5f, 0.1f);
            Assert.IsTrue(panel.IsPanelVisible);
        }

        [Test]
        public void TheRadialSwitch_IsAMaterialKey_SetOnceNotPerValue()
        {
            var panel = Open("color='#ff0000'");
            panel.FlushParams();
            Assert.IsFalse(panel.CurrentParams.Radial, "an ordinary panel never pays for the radial path");

            panel.SetRadialCut(0f, 1f);
            panel.FlushParams();
            Assert.IsTrue(panel.CurrentParams.Radial);
            var material = panel.material;

            panel.SetRadialCut(0.3f, 2f);
            panel.FlushParams();
            panel.SetRadialCut(0.6f, Pi);
            panel.FlushParams();
            Assert.AreSame(material, panel.material, "a sweep is vertex data — the material is untouched");

            panel.ClearCut();
            panel.FlushParams();
            Assert.IsFalse(panel.CurrentParams.Radial, "ClearCut takes the panel back to an ordinary one");
            Assert.AreEqual(0f, Mesh(panel)[0].uv1.z);
        }

        [Test]
        public void RingWidth_IsAMaterialParameter_SharedByEqualPanels()
        {
            var panel = Open("color='#ff0000'");
            panel.SetRingWidth(6f);
            panel.FlushParams();
            Assert.AreEqual(6f, panel.CurrentParams.RingWidth);

            var twin = new GameObject("twin", typeof(RectTransform)).AddComponent<ProceduralPanel>();
            twin.transform.SetParent(panel.transform.parent, false);
            twin.SetFill(panel.Fill);
            twin.SetRingWidth(6f);
            twin.FlushParams();
            Assert.AreSame(panel.material, twin.material, "two rings of one style share one material");

            twin.SetRingWidth(-3f);
            Assert.AreEqual(0f, twin.CurrentParams.RingWidth, "a negative width is no ring");
        }

        // ───── pixels: the sector ─────

        [Test]
        public void AQuarterSector_SweepsTheSquare_CornersIncluded()
        {
            // Centre at 45° clockwise from straight up, half-angle 45°: the sector spans 12 o'clock
            // to 3 o'clock — the top-right quadrant of the square, its far corner included.
            var panel = Open("color='#ff0000'");
            panel.SetRadialCut(0.125f, Pi / 4f);
            var tex = Render("promptugui-radial-quarter.png");

            AssertRed(tex.GetPixel(Mid + 30, Mid + 30), "inside the top-right quadrant");
            AssertRed(tex.GetPixel(Right - 4, Top - 4), "the top-right corner — 107 px out, past any inscribed circle");
            AssertBlack(tex.GetPixel(Mid - 30, Mid + 30), "top-left quadrant");
            AssertBlack(tex.GetPixel(Mid + 30, Mid - 30), "bottom-right quadrant");
            AssertBlack(tex.GetPixel(Mid - 30, Mid - 30), "bottom-left quadrant");
        }

        [Test]
        public void AHalfSector_FillsTheHalfItFaces()
        {
            var panel = Open("color='#ff0000'");
            panel.SetRadialCut(0.25f, Pi / 2f);   // centred on 3 o'clock, 180° wide
            var tex = Render("promptugui-radial-half.png");

            AssertRed(tex.GetPixel(Mid + 40, Mid + 60), "right half, upper");
            AssertRed(tex.GetPixel(Mid + 40, Mid - 60), "right half, lower");
            AssertBlack(tex.GetPixel(Mid - 40, Mid), "left half");
        }

        [Test]
        public void AReflexSector_KeepsAllButTheGap()
        {
            // 300° of a full turn, centred on 6 o'clock: the gap is the 60° wedge around 12 o'clock.
            var panel = Open("color='#ff0000'");
            panel.SetRadialCut(0.5f, Pi * 300f / 360f);
            var tex = Render("promptugui-radial-reflex.png");

            AssertBlack(tex.GetPixel(Mid, Top - 10), "straight up, inside the gap");
            AssertRed(tex.GetPixel(Mid + 60, Top - 10), "up and to the right, past the gap's edge");
            AssertRed(tex.GetPixel(Mid, Bottom + 10), "straight down");
            AssertRed(tex.GetPixel(Left + 10, Mid), "straight left");
        }

        [Test]
        public void AFullSector_RendersExactlyLikeNoCut()
        {
            var panel = Open("radius='20' color='#ff0000' glow='8' glowColor='#00ff00'");
            panel.ClearCut();
            var uncut = Render("promptugui-radial-none.png");
            panel.SetRadialCut(0.3f, Pi);
            var full = Render("promptugui-radial-full.png");

            foreach (var (x, y) in new[] { (Mid, Mid), (Right - 4, Mid), (Right + 4, Mid), (Mid, Top + 4), (Left + 3, Bottom + 3) })
                Assert.AreEqual(uncut.GetPixel(x, y), full.GetPixel(x, y), $"pixel ({x}, {y}) must match the uncut shape");
        }

        [Test]
        public void AnEmptySector_DrawsNothing_NotEvenTheGlow()
        {
            var panel = Open("color='#ff0000' glow='8' glowColor='#00ff00'");
            panel.SetRadialCut(0.125f, 0f);
            var tex = Render("promptugui-radial-empty.png");

            AssertBlack(tex.GetPixel(Mid + 2, Top - 4), "where the sector would start");
            AssertBlack(tex.GetPixel(Mid, Top + 4), "the glow above it");
        }

        [Test]
        public void TheGlow_WrapsTheSectorEdges_AndEscapesTheRect()
        {
            var panel = Open("color='#ff0000' glow='8' glowColor='#00ff00'");
            panel.SetRadialCut(0.125f, Pi / 4f);
            var tex = Render("promptugui-radial-glow.png");

            AssertGreenGlow(tex.GetPixel(Mid - 4, Mid + 40), "4 px past the 12 o'clock edge");
            AssertGreenGlow(tex.GetPixel(Mid + 40, Mid - 4), "4 px past the 3 o'clock edge");
            AssertGreenGlow(tex.GetPixel(Mid + 40, Top + 4), "above the filled quadrant, outside the rect");
            AssertBlack(tex.GetPixel(Mid - 40, Mid - 40), "deep in the unfilled part");
        }

        [Test]
        public void TheBorder_FollowsTheSectorEdge()
        {
            var panel = Open("color='#ff0000' borderWidth='6' borderColor='#0000ff'");
            panel.SetRadialCut(0.125f, Pi / 4f);
            var tex = Render("promptugui-radial-border.png");

            var atEdge = tex.GetPixel(Mid + 3, Mid + 40);
            Assert.Greater(atEdge.b, 0.5f, $"3 px inside the 12 o'clock edge is border, got {atEdge}");
            AssertRed(tex.GetPixel(Mid + 30, Mid + 40), "30 px inside is fill, not border");
        }

        // ───── pixels: the ring ─────

        [Test]
        public void ARing_OnACircle_IsABandInsideTheEdge()
        {
            var panel = Open("radius='pill' color='#ff0000'");
            panel.SetRingWidth(10f);
            var tex = Render("promptugui-ring-circle.png");

            AssertBlack(tex.GetPixel(Mid, Mid), "the centre is hollow");
            AssertBlack(tex.GetPixel(Mid, Mid + 60), "60 px out — inside the hole");
            AssertRed(tex.GetPixel(Mid, Mid + 75), "75 px out — inside the 70..80 band");
            AssertRed(tex.GetPixel(Mid - 75, Mid), "…on every side");
            AssertBlack(tex.GetPixel(Right - 4, Top - 4), "the square's corner is outside the circle");
        }

        [Test]
        public void ARing_OnARoundedSquare_KeepsOneWidthAllAround()
        {
            var panel = Open("radius='16' color='#ff0000'");
            panel.SetRingWidth(10f);
            var tex = Render("promptugui-ring-square.png");

            AssertRed(tex.GetPixel(Mid, Top - 5), "top band");
            AssertBlack(tex.GetPixel(Mid, Top - 15), "below the top band");
            AssertRed(tex.GetPixel(Right - 5, Mid), "right band");
            AssertBlack(tex.GetPixel(Right - 15, Mid), "inside the right band");
            AssertBlack(tex.GetPixel(Mid, Mid), "hollow centre");
        }

        [Test]
        public void ARingCutBySector_IsAnArc()
        {
            var panel = Open("radius='pill' color='#ff0000'");
            panel.SetRingWidth(10f);
            panel.SetRadialCut(0.125f, Pi / 4f);
            var tex = Render("promptugui-ring-arc.png");

            AssertRed(tex.GetPixel(Mid + 53, Mid + 53), "the arc at 45°, 75 px out");
            AssertBlack(tex.GetPixel(Mid - 53, Mid + 53), "the ring at 315° is cut away");
            AssertBlack(tex.GetPixel(Mid + 20, Mid + 20), "the hole stays hollow inside the sector");
        }

        [Test]
        public void ARoundCap_ExtendsTheArcPastTheSectorEdge()
        {
            // R = 80, ring 16: centre line at 72, half-width 8. The 12 o'clock end of a 0°..90° arc
            // sits at (0, 72); 5 px to its left is inside the round cap and outside the flat end.
            var panel = Open("radius='pill' color='#ff0000'");
            panel.SetRingWidth(16f);

            panel.SetRadialCut(0.125f, Pi / 4f, roundCap: true);
            var capped = Render("promptugui-ring-cap-round.png");
            AssertRed(capped.GetPixel(Mid - 5, Mid + 72), "inside the round cap");
            AssertRed(capped.GetPixel(Mid + 51, Mid + 51), "the arc body");
            AssertBlack(capped.GetPixel(Mid - 12, Mid + 72), "past the cap");

            panel.SetRadialCut(0.125f, Pi / 4f, roundCap: false);
            var flat = Render("promptugui-ring-cap-flat.png");
            AssertBlack(flat.GetPixel(Mid - 5, Mid + 72), "the flat end stops at the sector edge");
        }
    }
}
