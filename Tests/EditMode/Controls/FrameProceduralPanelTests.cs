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
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), p.CurrentParams.Fill.Start);
            Assert.AreEqual(p.CurrentParams.Fill.Start, p.CurrentParams.Fill.End,
                "a solid colour must not read as a gradient");
        }

        [Test]
        public void Panel_IsNotARaycastTarget()
        {
            // A drawn Frame stays click-through until the author writes raycastTarget="true"
            // (RaycastTargetTests has the rest of that contract).
            Assert.IsFalse(PanelOf(Load("color='#fff'")).raycastTarget);
        }

        [Test]
        public void Color_AcceptsAlphaSuffix()
        {
            var p = PanelOf(Load("color='#ffffff/0.5'"));
            Assert.AreEqual(0.5f, p.CurrentParams.Fill.Start.a, 0.001f);
        }

        [Test]
        public void Color_CommaValue_BecomesVerticalGradient()
        {
            var p = PanelOf(Load("color='#ff0000,#0000ff'"));
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), p.CurrentParams.Fill.Start);
            Assert.AreEqual(new Color(0f, 0f, 1f, 1f), p.CurrentParams.Fill.End);
        }

        [Test]
        public void Radius_FourValues_LandInCssOrder()
        {
            var p = PanelOf(Load("color='#fff' radius='1,2,3,4'"));
            Assert.AreEqual(new Vector4(1f, 2f, 3f, 4f), p.CurrentParams.CornerWidth);
            Assert.IsFalse(p.CurrentParams.Pill);
        }

        [Test]
        public void Radius_Pill_SetsSentinelNotANumber()
        {
            var p = PanelOf(Load("color='#fff' radius='pill'"));
            Assert.IsTrue(p.CurrentParams.Pill);
            Assert.AreEqual(Vector4.zero, p.CurrentParams.CornerWidth);
        }

        // ---- fillet (spec 2026-08-29) ----

        [Test]
        public void Radius_Fillet_LandsInCssOrder()
        {
            var p = PanelOf(Load("color='#fff' radius='cut 8 r2, cut 8 r3, 0, notch 8 r4'"));
            Assert.AreEqual(new Vector4(2f, 3f, 0f, 4f), p.CurrentParams.CornerFillet);
            Assert.AreEqual(new Vector4(8f, 8f, 0f, 8f), p.CurrentParams.CornerWidth,
                "the fillet must not disturb the size it follows");
        }

        [Test]
        public void Radius_HexagonFillet_RidesInAllFourCorners()
        {
            var p = PanelOf(Load("color='#fff' radius='hexagon 40 r6'"));
            Assert.AreEqual(new Vector4(6f, 6f, 6f, 6f), p.CurrentParams.CornerFillet);
            Assert.AreEqual(40f, p.CurrentParams.HexWidth);
        }

        [Test]
        public void SameFillet_SharesOneMaterial()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Style name='tab' color='#222' radius='cut 16x99 r8'/>
  <Screen name='S'>
    <Frame id='a' class='tab' height='40'/>
    <Frame id='b' class='tab' height='90'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            Assert.AreSame(PanelOf(s.Get<Frame>("a")).material, PanelOf(s.Get<Frame>("b")).material,
                "r is a material parameter resolved per-fragment; two sizes must still share");
        }

        [TestCase("radius='cut 12 r4'", "radius='cut 12 r6'")]
        [TestCase("radius='cut 12'", "radius='cut 12 r4'")]
        [TestCase("radius='hexagon 40'", "radius='hexagon 40 r6'")]
        public void DifferentFillet_SplitsTheMaterial(string a, string b)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='a' color='#222' {a}/>
  <Frame id='b' color='#222' {b}/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            Assert.AreNotSame(PanelOf(s.Get<Frame>("a")).material, PanelOf(s.Get<Frame>("b")).material);
        }

        [Test]
        public void BorderOnly_NoFill_IsVisible()
        {
            var p = PanelOf(Load("borderWidth='2' borderColor='#00ff00'"));
            Assert.IsTrue(p.IsPanelVisible, "a hollow outlined box is a legitimate look");
            Assert.AreEqual(2f, p.CurrentParams.BorderWidth);
            Assert.AreEqual(new Color(0f, 1f, 0f, 1f), p.CurrentParams.Border.Start);
        }

        [Test]
        public void GlowColor_DefaultsToFillColorAtFullAlpha()
        {
            var p = PanelOf(Load("color='#ff0000/0.5' glow='8'"));
            var glow = p.CurrentParams.Glow.Start;
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), glow,
                "glow='8' alone should read as 'this shape glows', not 'add a white halo'");
        }

        [Test]
        public void GlowColor_ExplicitWins()
        {
            var p = PanelOf(Load("color='#ff0000' glow='8' glowColor='#0000ff'"));
            Assert.AreEqual(new Color(0f, 0f, 1f, 1f), p.CurrentParams.Glow.Start);
        }

        // ---- inner glow (spec 2026-08-28) ----

        [Test]
        public void InnerGlow_ParsesPixels()
        {
            Assert.AreEqual(12f, PanelOf(Load("color='#fff' innerGlow='12'")).CurrentParams.InnerGlowSize);
        }

        [Test]
        public void InnerGlow_Empty_ResetsToZero()
        {
            // A Variant can only change a value, never remove the attribute — "" is the author's
            // only way back to none (same contract glow and radius have).
            Assert.AreEqual(0f, PanelOf(Load("color='#fff' innerGlow=''")).CurrentParams.InnerGlowSize);
        }

        [Test]
        public void InnerGlow_RejectsNonNumeric()
        {
            var ex = Assert.Throws<ParseException>(() => Load("innerGlow='soft'"));
            StringAssert.Contains("innerGlow", ex.Message);
        }

        [Test]
        public void InnerGlow_RejectsNegative()
        {
            Assert.Throws<ParseException>(() => Load("innerGlow='-4'"));
        }

        [Test]
        public void InnerGlow_RejectsNaN()
        {
            Assert.Throws<ParseException>(() => Load("innerGlow='NaN'"));
        }

        [Test]
        public void InnerGlowColor_DefaultsToWhite()
        {
            // Deliberately NOT the fill colour the way glowColor is: an inner glow in the fill's own
            // colour is invisible on an opaque fill, which is the overwhelmingly common case. White
            // reads as "the edge is lit" and shows up on any fill (spec §5.4).
            var p = PanelOf(Load("color='#ff0000' innerGlow='8'"));
            Assert.AreEqual(Color.white, p.CurrentParams.InnerGlow.Start,
                "following the fill would make innerGlow='8' alone draw nothing at all");
        }

        [Test]
        public void InnerGlowColor_ExplicitWins()
        {
            var p = PanelOf(Load("color='#ff0000' innerGlow='8' innerGlowColor='#0000ff/0.5'"));
            Assert.AreEqual(new Color(0f, 0f, 1f, 0.5f), p.CurrentParams.InnerGlow.Start);
        }

        [Test]
        public void InnerGlowColor_AcceptsGradient()
        {
            // Every SDF colour slot takes the full gradient grammar (spec 2026-09-17 LG-D4).
            var p = Load("innerGlow='8' innerGlowColor='#fff,#000'");
            Assert.IsTrue(PanelOf(p).CurrentParams.InnerGlow.IsGradient);
        }

        [Test]
        public void InnerGlowOnly_NoFill_IsVisible()
        {
            // A hollow ring of light, same standing as the border-only hollow box above.
            Assert.IsTrue(PanelOf(Load("innerGlow='8'")).IsPanelVisible);
        }

        [Test]
        public void InnerGlow_TransparentColour_IsNotVisible()
        {
            Assert.IsFalse(PanelOf(Load("innerGlow='8' innerGlowColor='#ffffff/0'")).IsPanelVisible,
                "an invisible panel must cost zero overdraw, whatever the size attribute says");
        }

        [Test]
        public void InnerGlow_DoesNotInflateMesh()
        {
            // The whole point of the mirror: it draws INSIDE the shape, so unlike glow it never
            // touches the geometry — no extra overdraw, and no ancestor RectMask2D interaction.
            var f = Load("color='#fff' innerGlow='20' width='100' height='50' anchor='top-left'");
            var vh = new VertexHelper();
            PanelOf(f).BuildMeshForTests(vh);

            var v = default(UIVertex);
            vh.PopulateUIVertex(ref v, 2);
            Assert.AreEqual(50f, v.uv0.x, 0.01f, "an inner glow must not grow the drawn quad");
            Assert.AreEqual(25f, v.uv0.y, 0.01f);
        }

        [Test]
        public void SameInnerGlow_SharesOneMaterial()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Style name='plate' color='#222' innerGlow='10' innerGlowColor='#fff3c4'/>
  <Screen name='S'>
    <Frame id='a' class='plate' height='40'/>
    <Frame id='b' class='plate' height='90'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            Assert.AreSame(PanelOf(s.Get<Frame>("a")).material, PanelOf(s.Get<Frame>("b")).material);
        }

        [TestCase("innerGlow='10'", "innerGlow='14'")]
        [TestCase("innerGlow='10'", "innerGlow='10' innerGlowColor='#f00'")]
        public void DifferentInnerGlow_SplitsTheMaterial(string a, string b)
        {
            // Both halves have to be in the cache key, or two panels that render differently would
            // be handed the same material.
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='a' color='#222' {a}/>
  <Frame id='b' color='#222' {b}/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            Assert.AreNotSame(PanelOf(s.Get<Frame>("a")).material, PanelOf(s.Get<Frame>("b")).material);
        }

        [Test]
        public void Disabled_DesaturatesTheInnerGlow()
        {
            // Greying happens inside the parameters (the material carries the shape and cannot be
            // swapped) — so every colour has to be walked, including this one.
            var p = PanelOf(Load("color='#ff0000' innerGlow='8' innerGlowColor='#00ff00'"));
            p.SetDisabledGrayscale(true);

            var c = p.CurrentParams.InnerGlow.Start;
            Assert.AreEqual(c.r, c.g, 0.001f, $"a disabled surface must not keep a coloured rim, got {c}");
            Assert.AreEqual(c.g, c.b, 0.001f);
        }

        // ---- intensity (spec 2026-09-12): the exposure knob ------------------------------------

        [Test]
        public void Intensity_DefaultsToOne()
        {
            Assert.AreEqual(1f, PanelOf(Load("color='#fff'")).CurrentParams.Intensity, 0.0001f,
                "1 means 'unchanged' — a stray default would light every panel in the library");
        }

        [Test]
        public void Intensity_ParsesNumber()
        {
            Assert.AreEqual(3f, PanelOf(Load("color='#fff' intensity='3'")).CurrentParams.Intensity, 0.0001f);
        }

        [Test]
        public void Intensity_Empty_ResetsToOne()
        {
            // Variant escape hatch: a value can be overridden but never removed.
            Assert.AreEqual(1f, PanelOf(Load("color='#fff' intensity=''")).CurrentParams.Intensity, 0.0001f);
        }

        [TestCase("intensity='0.5'")]
        [TestCase("intensity='0'")]
        [TestCase("intensity='NaN'")]
        [TestCase("intensity='bright'")]
        public void Intensity_BadValue_Rejected(string attrs)
        {
            var ex = Assert.Throws<ParseException>(() => Load($"color='#fff' {attrs}"));
            StringAssert.Contains("intensity", ex.Message);
        }

        [Test]
        public void Intensity_AloneIsNotVisible()
        {
            // Nothing painted, nothing to light: no fill, border or glow ⇒ no draw.
            Assert.IsFalse(PanelOf(Load("intensity='3'")).IsPanelVisible);
        }

        [Test]
        public void Intensity_DoesNotInflateMesh()
        {
            // Pure material parameter: the curve brightens what is already drawn, so the quad is
            // exactly what the glow (if any) needs — nothing more.
            var f = Load("color='#fff' intensity='8' width='100' height='50' anchor='top-left'");
            var vh = new VertexHelper();
            PanelOf(f).BuildMeshForTests(vh);

            var v = default(UIVertex);
            vh.PopulateUIVertex(ref v, 2);
            Assert.AreEqual(50f, v.uv0.x, 0.01f, "intensity must not grow the drawn quad");
            Assert.AreEqual(25f, v.uv0.y, 0.01f);
        }

        [Test]
        public void Glass_IgnoresIntensity()
        {
            // Glass paints the backdrop, which is not light the surface emits (spec §5.4). Zeroed
            // in the key so glass panels that differ only in intensity keep sharing one material.
            Assert.AreEqual(1f, PanelOf(Load("glass='true' intensity='3'")).CurrentParams.Intensity, 0.0001f);
        }

        [Test]
        public void Disabled_ResetsIntensityToOne()
        {
            // Greying is "no colour"; exposure is "more light". Stacked they give a white-hot grey
            // panel that reads as switched on — a disabled control has to read as inert (§5.5).
            var p = PanelOf(Load("color='#ff0000' glow='8' intensity='4'"));
            p.SetDisabledGrayscale(true);
            Assert.AreEqual(1f, p.CurrentParams.Intensity, 0.0001f, "a disabled surface must read as unlit");

            p.SetDisabledGrayscale(false);
            Assert.AreEqual(4f, p.CurrentParams.Intensity, 0.0001f, "…and light back up when re-enabled");
        }

        [Test]
        public void SameIntensity_SharesOneMaterial()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Style name='neon' color='#4f88ff' glow='12' intensity='4'/>
  <Screen name='S'>
    <Frame id='a' class='neon' height='40'/>
    <Frame id='b' class='neon' height='90'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            Assert.AreSame(PanelOf(s.Get<Frame>("a")).material, PanelOf(s.Get<Frame>("b")).material);
        }

        [TestCase("intensity='2'", "intensity='3'")]
        [TestCase("", "intensity='2'")]
        public void DifferentIntensity_SplitsTheMaterial(string a, string b)
        {
            // It has to be in the cache key, or two panels that render differently would be
            // handed the same material.
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='a' color='#222' {a}/>
  <Frame id='b' color='#222' {b}/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            Assert.AreNotSame(PanelOf(s.Get<Frame>("a")).material, PanelOf(s.Get<Frame>("b")).material);
        }

        [Test]
        public void BorderColor_AcceptsGradient()
        {
            // Every SDF colour slot takes the full gradient grammar (spec 2026-09-17 LG-D4).
            var p = Load("borderWidth='1' borderColor='#fff,#000'");
            Assert.IsTrue(PanelOf(p).CurrentParams.Border.IsGradient);
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
