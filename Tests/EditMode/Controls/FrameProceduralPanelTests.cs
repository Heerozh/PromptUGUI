using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Frame's procedural visual layer: the panel is attached only when the author asks for it, its
    /// parameters live in a shared material (so identical styles batch and colour changes never
    /// rebuild the canvas), and its geometry only leaves the layout rect for a glow.
    /// </summary>
    public class FrameProceduralPanelTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Frame Load(string frameAttrs, string extraTop = "")
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{extraTop}<Screen name='S'>
  <Frame id='f' {frameAttrs}/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Frame>("f");
        }

        private static ProceduralPanel PanelOf(Frame f) => f.GameObject.GetComponent<ProceduralPanel>();

        [Test]
        public void NoVisualAttrs_NoPanelComponent()
        {
            // The historical contract: a plain <Frame> is a bare RectTransform and costs nothing.
            var f = Load("height='40'");
            Assert.IsNull(PanelOf(f));
            Assert.IsNull(f.GameObject.GetComponent<Graphic>());
        }

        [Test]
        public void MaskOnly_StillNoPanelComponent()
        {
            var f = Load("mask='rect'");
            Assert.IsNull(PanelOf(f));
        }

        [Test]
        public void Color_AttachesPanel_AndSetsSolidFill()
        {
            var f = Load("color='#ff0000'");
            var p = PanelOf(f);
            Assert.IsNotNull(p);
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), p.CurrentParams.FillTop);
            Assert.AreEqual(p.CurrentParams.FillTop, p.CurrentParams.FillBottom,
                "a solid colour must not read as a gradient");
        }

        [Test]
        public void Panel_IsNotARaycastTarget()
        {
            // A Frame stays click-through; a tinted clickable region is what <Btn> is for.
            Assert.IsFalse(PanelOf(Load("color='#fff'")).raycastTarget);
        }

        [Test]
        public void Color_AcceptsAlphaSuffix()
        {
            var p = PanelOf(Load("color='#ffffff/0.5'"));
            Assert.AreEqual(0.5f, p.CurrentParams.FillTop.a, 0.001f);
        }

        [Test]
        public void Color_CommaValue_BecomesVerticalGradient()
        {
            var p = PanelOf(Load("color='#ff0000,#0000ff'"));
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), p.CurrentParams.FillTop);
            Assert.AreEqual(new Color(0f, 0f, 1f, 1f), p.CurrentParams.FillBottom);
        }

        [Test]
        public void Radius_FourValues_LandInCssOrder()
        {
            var p = PanelOf(Load("color='#fff' radius='1,2,3,4'"));
            Assert.AreEqual(new Vector4(1f, 2f, 3f, 4f), p.CurrentParams.Radius);
            Assert.IsFalse(p.CurrentParams.Pill);
        }

        [Test]
        public void Radius_Pill_SetsSentinelNotANumber()
        {
            var p = PanelOf(Load("color='#fff' radius='pill'"));
            Assert.IsTrue(p.CurrentParams.Pill);
            Assert.AreEqual(Vector4.zero, p.CurrentParams.Radius);
        }

        [Test]
        public void BorderOnly_NoFill_IsVisible()
        {
            var p = PanelOf(Load("borderWidth='2' borderColor='#00ff00'"));
            Assert.IsTrue(p.IsPanelVisible, "a hollow outlined box is a legitimate look");
            Assert.AreEqual(2f, p.CurrentParams.BorderWidth);
            Assert.AreEqual(new Color(0f, 1f, 0f, 1f), p.CurrentParams.BorderColor);
        }

        [Test]
        public void GlowColor_DefaultsToFillColorAtFullAlpha()
        {
            var p = PanelOf(Load("color='#ff0000/0.5' glow='8'"));
            var glow = p.CurrentParams.GlowColor;
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), glow,
                "glow='8' alone should read as 'this shape glows', not 'add a white halo'");
        }

        [Test]
        public void GlowColor_ExplicitWins()
        {
            var p = PanelOf(Load("color='#ff0000' glow='8' glowColor='#0000ff'"));
            Assert.AreEqual(new Color(0f, 0f, 1f, 1f), p.CurrentParams.GlowColor);
        }

        [Test]
        public void BorderColor_RejectsGradient()
        {
            // Solid-only, same rule *Modulate gets: report it rather than silently taking one stop.
            Assert.Throws<ParseException>(() => Load("borderWidth='1' borderColor='#fff,#000'"));
        }

        [Test]
        public void BorderWidth_RejectsNonNumeric()
        {
            var ex = Assert.Throws<ParseException>(() => Load("borderWidth='thick'"));
            StringAssert.Contains("borderWidth", ex.Message);
        }

        [Test]
        public void BorderWidth_RejectsNegative()
        {
            Assert.Throws<ParseException>(() => Load("borderWidth='-1'"));
        }

        [Test]
        public void Radius_SyntaxError_Throws()
        {
            Assert.Throws<ParseException>(() => Load("color='#fff' radius='1,2'"));
        }

        // ---- geometry ----

        [Test]
        public void NoGlow_MeshMatchesLayoutRect()
        {
            var f = Load("color='#fff' width='100' height='50' anchor='top-left'");
            var vh = new VertexHelper();
            PanelOf(f).BuildMeshForTests(vh);
            Assert.AreEqual(4, vh.currentVertCount);

            var v = default(UIVertex);
            vh.PopulateUIVertex(ref v, 2);   // top-right corner
            Assert.AreEqual(50f, v.uv0.x, 0.01f, "no glow ⇒ no transparent overdraw around the panel");
            Assert.AreEqual(25f, v.uv0.y, 0.01f);
            Assert.AreEqual(new Vector4(50f, 25f, 0f, 0f), v.uv1, "uv1 carries the half-size the SDF needs");
        }

        [Test]
        public void Glow_InflatesMeshByGlowRadius()
        {
            var f = Load("color='#fff' glow='8' width='100' height='50' anchor='top-left'");
            var vh = new VertexHelper();
            PanelOf(f).BuildMeshForTests(vh);

            var v = default(UIVertex);
            vh.PopulateUIVertex(ref v, 2);
            Assert.AreEqual(58f, v.uv0.x, 0.01f);
            Assert.AreEqual(33f, v.uv0.y, 0.01f);
            Assert.AreEqual(new Vector4(50f, 25f, 0f, 0f), v.uv1,
                "the half-size stays the layout rect — only the drawn quad grows");
        }

        [Test]
        public void FullyTransparent_EmitsNoGeometry()
        {
            var f = Load("color='#ffffff/0'");
            var vh = new VertexHelper();
            PanelOf(f).BuildMeshForTests(vh);
            Assert.AreEqual(0, vh.currentVertCount, "an invisible panel must cost zero overdraw");
        }

        [Test]
        public void CanvasRebuild_ReachesTheCanvasRenderer()
        {
            // Regression guard: Graphic's [RequireComponent(typeof(CanvasRenderer))] does not carry
            // over to a subclass added at runtime. Without our own attribute the panel throws
            // MissingComponentException on first rebuild and renders nothing — and every other test
            // here still passes, because none of them force a rebuild.
            var f = Load("color='#ff0000' width='100' height='50'");
            var panel = PanelOf(f);
            Assert.IsNotNull(f.GameObject.GetComponent<CanvasRenderer>());

            Canvas.ForceUpdateCanvases();

            var cr = panel.canvasRenderer;
            Assert.Greater(cr.materialCount, 0, "the rebuild must have pushed a material through");
            Assert.AreEqual("UI/ProceduralPanel", cr.GetMaterial(0).shader.name);
        }

        [Test]
        public void Canvas_OptsIntoTexCoord1()
        {
            // Without this the half-size input is stripped from the canvas mesh and every panel
            // collapses to nothing.
            var f = Load("color='#fff'");
            var canvas = f.GameObject.GetComponentInParent<Canvas>();
            Assert.IsNotNull(canvas);
            Assert.AreNotEqual(0,
                canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1);
        }

        // ---- material sharing ----

        [Test]
        public void IdenticalStyles_ShareOneMaterial()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Style name='card' color='#222' radius='16'/>
  <Screen name='S'>
    <Frame id='a' class='card' height='40'/>
    <Frame id='b' class='card' height='90'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var a = PanelOf(s.Get<Frame>("a"));
            var b = PanelOf(s.Get<Frame>("b"));
            Assert.AreSame(a.material, b.material,
                "different sizes, same style ⇒ one material ⇒ the two panels can still batch");
        }

        [Test]
        public void DifferentStyles_GetDifferentMaterials()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='a' color='#222'/>
  <Frame id='b' color='#333'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            Assert.AreNotSame(PanelOf(s.Get<Frame>("a")).material,
                              PanelOf(s.Get<Frame>("b")).material);
        }

        [Test]
        public void ClosingScreen_ReleasesMaterials()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='a' color='#222'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var before = ProceduralMaterialCache.LiveMaterialCount;
            var s = UI.Open("S");
            Assert.Greater(ProceduralMaterialCache.LiveMaterialCount, before);
            s.Close();
            Assert.AreEqual(before, ProceduralMaterialCache.LiveMaterialCount,
                "a closed screen must not pin materials");
        }
    }
}
