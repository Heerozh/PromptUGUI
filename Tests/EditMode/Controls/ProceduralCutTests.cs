using System.IO;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The progress cut on a <see cref="ProceduralPanel"/> (spec 2026-09-18 §5.2): the shape is
    /// intersected with a half-plane INSIDE the SDF, so border / glow / haze follow the cut edge
    /// and nothing is clipped by a stencil — the glow escapes the rect on every side and wraps the
    /// leading edge. The cut rides the spare vertex channels (<c>uv1.zw</c>), never the material.
    /// </summary>
    public class ProceduralCutTests
    {
        private const int Size = 256;

        private Camera _ui;
        private RenderTexture _uiRt;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _uiRt = new RenderTexture(Size, Size, 24) { name = "ProceduralCutUIRT" };
            _ui = new GameObject("ProceduralCutUICamera").AddComponent<Camera>();
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

        // A 200×40 bar centred on a 256×256 canvas with no reference= (ConstantPixelSize at scale
        // 1: one unit is one pixel): x ∈ [28, 228], y ∈ [108, 148], centre (128, 128).
        private const int Left = 28, Right = 228, Bottom = 108, Top = 148, MidY = 128, MidX = 128;

        private ProceduralPanel Open(string frameAttrs)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' anchor='center' width='200' height='40' {frameAttrs}/>
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
            Debug.Log($"PromptUGUI procedural-cut render dump: {path}");
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

        // ───── vertex channels ─────

        [Test]
        public void SetCut_WritesTheAxisCodeAndOffsetIntoUv1()
        {
            var panel = Open("radius='8' color='#ff0000'");

            panel.SetCut(Vector2.right, 0.25f);
            var v = Mesh(panel);
            Assert.AreEqual(4, v.Length);
            foreach (var vert in v)
            {
                Assert.AreEqual(1f, vert.uv1.z, "horizontal = +x → code 1");
                // e = (2·value − 1) · b.x = −0.5 · 100
                Assert.AreEqual(-50f, vert.uv1.w, 1e-3f, "the cut line sits at a quarter of the width");
                Assert.AreEqual(100f, vert.uv1.x, 1e-3f, "the half-size channel is untouched");
                Assert.AreEqual(20f, vert.uv1.y, 1e-3f);
            }

            panel.SetCut(Vector2.left, 0.25f);
            Assert.AreEqual(-1f, Mesh(panel)[0].uv1.z, "reverse-horizontal = −x → code −1");

            panel.SetCut(Vector2.up, 0.75f);
            v = Mesh(panel);
            Assert.AreEqual(2f, v[0].uv1.z, "vertical = +y → code 2");
            Assert.AreEqual(10f, v[0].uv1.w, 1e-3f, "e = 0.5 · b.y");

            panel.SetCut(Vector2.down, 0.75f);
            Assert.AreEqual(-2f, Mesh(panel)[0].uv1.z, "reverse-vertical = −y → code −2");
        }

        [Test]
        public void ARoundCut_IsEncodedTwoCodesUp()
        {
            var panel = Open("radius='8' color='#ff0000'");

            panel.SetCut(Vector2.right, 0.25f, round: true);
            var v = Mesh(panel)[0];
            Assert.AreEqual(3f, v.uv1.z, "round along +x → code 3");
            Assert.AreEqual(-50f, v.uv1.w, 1e-3f, "same cut line as the flat cut");

            panel.SetCut(Vector2.left, 0.25f, round: true);
            Assert.AreEqual(-3f, Mesh(panel)[0].uv1.z);
            panel.SetCut(Vector2.up, 0.75f, round: true);
            Assert.AreEqual(4f, Mesh(panel)[0].uv1.z, "round along +y → code 4");
            panel.SetCut(Vector2.down, 0.75f, round: true);
            Assert.AreEqual(-4f, Mesh(panel)[0].uv1.z);

            panel.SetCut(Vector2.right, 1f, round: true);
            Assert.AreEqual(0f, Mesh(panel)[0].uv1.z, "a full bar is the uncut shape in either style");
            panel.SetCut(Vector2.right, 0f, round: true);
            Assert.IsFalse(panel.IsPanelVisible, "…and 0 % is nothing in either style");
        }

        [Test]
        public void APanelWithNoCut_LeavesTheChannelsAtZero()
        {
            var panel = Open("radius='8' color='#ff0000'");
            foreach (var vert in Mesh(panel))
            {
                Assert.AreEqual(0f, vert.uv1.z, "no cut → code 0, so every other panel is bit-for-bit what it was");
                Assert.AreEqual(0f, vert.uv1.w);
            }
        }

        [Test]
        public void ValueZero_EmitsNoGeometryAtAll()
        {
            var panel = Open("radius='8' color='#ff0000' glow='8'");
            panel.SetCut(Vector2.right, 0f);

            Assert.IsFalse(panel.IsPanelVisible, "0 % is nothing — not even the glow along the start edge");
            Assert.AreEqual(0, Mesh(panel).Length);

            panel.SetCut(Vector2.right, 0.1f);
            Assert.IsTrue(panel.IsPanelVisible, "…and it comes back the moment there is something to fill");
            Assert.AreEqual(4, Mesh(panel).Length);
        }

        [Test]
        public void ValueOne_IsEncodedAsNoCut()
        {
            var panel = Open("radius='8' color='#ff0000'");
            panel.SetCut(Vector2.right, 1f);
            Assert.AreEqual(0f, Mesh(panel)[0].uv1.z, "a full bar is the uncut shape, with none of the edge-on-edge float fuss");

            panel.SetCut(Vector2.right, 0.5f);
            Assert.AreEqual(1f, Mesh(panel)[0].uv1.z);
            panel.ClearCut();
            Assert.AreEqual(0f, Mesh(panel)[0].uv1.z, "ClearCut is the same spelling");
        }

        [Test]
        public void SetCut_NeverTouchesTheMaterial()
        {
            var panel = Open("radius='8' color='#ff0000' glow='6'");
            panel.FlushParams();
            var paramsBefore = panel.CurrentParams;
            var materialBefore = panel.material;

            panel.SetCut(Vector2.right, 0.3f);
            panel.FlushParams();
            panel.SetCut(Vector2.right, 0.6f);
            panel.FlushParams();

            Assert.AreEqual(paramsBefore, panel.CurrentParams, "the cut is vertex data, not a material parameter");
            Assert.AreSame(materialBefore, panel.material,
                "a value tween must not walk the material cache — that is the whole point of the vertex channel");
        }

        // ───── pixels ─────

        private const string GlowBar = "radius='20' color='#ff0000' glow='8' glowColor='#00ff00'";

        [Test]
        public void Cut_FillsUpToTheEdgeAndNothingPastIt()
        {
            var panel = Open(GlowBar);
            panel.SetCut(Vector2.right, 0.5f);
            var tex = Render("promptugui-cut-half.png");

            AssertRed(tex.GetPixel(Left + 50, MidY), "a quarter of the way in");
            AssertBlack(tex.GetPixel(Right - 50, MidY), "three quarters of the way in, past the cut");
        }

        [Test]
        public void Cut_GlowEscapesTheRect_OnEverySideOfTheFilledPart()
        {
            var panel = Open(GlowBar);
            panel.SetCut(Vector2.right, 0.5f);
            var tex = Render("promptugui-cut-glow.png");

            AssertGreenGlow(tex.GetPixel(Left + 50, Top + 4), "4 px above the filled quarter — outside the rect");
            AssertGreenGlow(tex.GetPixel(Left + 50, Bottom - 4), "4 px below the filled quarter");
            AssertGreenGlow(tex.GetPixel(Left - 4, MidY), "4 px before the start edge");
        }

        [Test]
        public void Cut_GlowWrapsTheLeadingEdge_ButNotBeyondTheCut()
        {
            var panel = Open(GlowBar);
            panel.SetCut(Vector2.right, 0.5f);
            var tex = Render("promptugui-cut-leading-edge.png");

            AssertGreenGlow(tex.GetPixel(MidX + 4, MidY), "4 px past the leading edge — the halo hugs the cut, no knife edge through it");
            AssertBlack(tex.GetPixel(Right - 50, Top + 4), "above the unfilled part there is no shape to glow");
            AssertBlack(tex.GetPixel(MidX + 20, MidY), "20 px past the edge is beyond an 8 px glow");
        }

        [Test]
        public void ValueZero_DrawsNothing_NotEvenTheGlow()
        {
            var panel = Open(GlowBar);
            panel.SetCut(Vector2.right, 0f);
            var tex = Render("promptugui-cut-zero.png");

            AssertBlack(tex.GetPixel(Left + 4, MidY), "just inside the start edge");
            AssertBlack(tex.GetPixel(Left - 4, MidY), "the glow that would sit before the start edge");
            AssertBlack(tex.GetPixel(Left + 50, Top + 4), "above the rect");
        }

        [Test]
        public void ValueOne_RendersExactlyLikeNoCut()
        {
            var panel = Open(GlowBar);
            panel.ClearCut();
            var uncut = Render("promptugui-cut-none.png");
            panel.SetCut(Vector2.right, 1f);
            var full = Render("promptugui-cut-one.png");

            foreach (var (x, y) in new[] { (Left + 50, MidY), (Right - 4, MidY), (Right + 4, MidY), (Right - 3, Top - 3), (MidX, Top + 4) })
                Assert.AreEqual(uncut.GetPixel(x, y), full.GetPixel(x, y), $"pixel ({x}, {y}) must match the uncut bar");
        }

        [Test]
        public void VerticalCut_FillsFromTheBottom()
        {
            var panel = Open(GlowBar);
            panel.SetCut(Vector2.up, 0.5f);
            var tex = Render("promptugui-cut-vertical.png");

            AssertRed(tex.GetPixel(MidX, Bottom + 10), "bottom half");
            AssertBlack(tex.GetPixel(MidX, Top - 10), "top half");
        }

        [Test]
        public void ReverseHorizontalCut_FillsFromTheRight()
        {
            var panel = Open(GlowBar);
            panel.SetCut(Vector2.left, 0.5f);
            var tex = Render("promptugui-cut-reverse.png");

            AssertRed(tex.GetPixel(Right - 50, MidY), "the right half");
            AssertBlack(tex.GetPixel(Left + 50, MidY), "the left half");
        }

        // ───── round cut: the shape itself shrinks to the cut line, corners and all ─────

        [Test]
        public void ARoundCut_KeepsTheRadiusOnTheLeadingEnd()
        {
            // A 200×40 pill cut at 50 %: the round style leaves a 100×40 pill, so the leading
            // top corner (2 px inside the cut, 2 px under the top) lies outside the shape; the
            // flat style fills it.
            var panel = Open("radius='20' color='#ff0000'");
            panel.SetCut(Vector2.right, 0.5f, round: true);
            var round = Render("promptugui-cut-round.png");
            AssertBlack(round.GetPixel(MidX - 3, Top - 3), "the leading corner is rounded away");
            AssertRed(round.GetPixel(MidX - 3, MidY), "…while the leading edge at mid-height is fill");
            AssertRed(round.GetPixel(Left + 50, Top - 3), "…and the body is untouched");

            panel.SetCut(Vector2.right, 0.5f, round: false);
            var flat = Render("promptugui-cut-flat.png");
            AssertRed(flat.GetPixel(MidX - 3, Top - 3), "the flat cut keeps the corner square");
        }

        [Test]
        public void ARoundCut_GlowWrapsTheRoundedEnd_AndStillEscapesTheRect()
        {
            var panel = Open(GlowBar);
            panel.SetCut(Vector2.right, 0.5f, round: true);
            var tex = Render("promptugui-cut-round-glow.png");

            AssertGreenGlow(tex.GetPixel(MidX + 4, MidY), "past the rounded end at mid-height");
            AssertGreenGlow(tex.GetPixel(MidX - 3, Top - 3), "the rounded-away corner is now inside the halo");
            AssertGreenGlow(tex.GetPixel(Left + 50, Top + 4), "above the body, outside the rect");
            AssertBlack(tex.GetPixel(Right - 50, MidY), "nothing past the cut");
        }

        [Test]
        public void ARoundCut_LaysTheRampAlongTheWholeBar()
        {
            // Left half red, right half blue along the bar. At 50 % the round cut shows red up to
            // the leading end — the ramp is cropped, not squeezed into the visible part.
            var panel = Open("radius='20' color='to right, #ff0000 50%, #0000ff 50%'");
            panel.SetCut(Vector2.right, 0.5f, round: true);
            var tex = Render("promptugui-cut-round-ramp.png");

            AssertRed(tex.GetPixel(Left + 20, MidY), "start of the bar");
            AssertRed(tex.GetPixel(MidX - 6, MidY), "just before the leading end — still the first half of the ramp");
        }

        [Test]
        public void ASmallRoundCut_IsACapsule_NotASliver()
        {
            // 5 % of a 200×40 pill is a 10×40 box whose radius clamps to 5: a thin capsule
            // hugging the start edge, corners rounded, nothing beyond its 10 px.
            var panel = Open("radius='20' color='#ff0000'");
            panel.SetCut(Vector2.right, 0.05f, round: true);
            var tex = Render("promptugui-cut-round-small.png");

            AssertRed(tex.GetPixel(Left + 5, MidY), "the capsule's centre line");
            AssertBlack(tex.GetPixel(Left + 1, Top - 1), "its corner is rounded away");
            AssertBlack(tex.GetPixel(Left + 14, MidY), "nothing past the capsule");
        }

        [Test]
        public void Border_FollowsTheCutEdge()
        {
            var panel = Open("color='#ff0000' borderWidth='6' borderColor='#0000ff'");
            panel.SetCut(Vector2.right, 0.5f);
            var tex = Render("promptugui-cut-border.png");

            var atEdge = tex.GetPixel(MidX - 3, MidY);
            Assert.Greater(atEdge.b, 0.5f, $"3 px inside the cut edge is border, got {atEdge}");
            AssertRed(tex.GetPixel(MidX - 30, MidY), "30 px inside is fill, not border");
        }
    }
}
