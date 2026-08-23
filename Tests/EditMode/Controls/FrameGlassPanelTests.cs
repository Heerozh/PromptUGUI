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
    /// Frame's glass fill mode: the same procedural panel, but the fill samples a blurred backdrop
    /// instead of being a flat colour. What matters here is that the glass parameters ride the
    /// material (so identically-styled glass panels still share one material and batch), that a
    /// non-glass panel carries none of them (so the material cache never fragments over parameters
    /// nothing reads), and that the whole thing degrades to a plain translucent panel when no
    /// backdrop is available.
    /// </summary>
    public class FrameGlassPanelTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // Unloads first so a test can load twice to compare two shapes. PanelParams is a struct, so
        // values captured from the first panel stay valid after it is destroyed.
        private static Frame Load(string frameAttrs, string extraTop = "")
        {
            UI.UnloadAll();
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>{extraTop}<Screen name='S'>
  <Frame id='f' {frameAttrs}/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Frame>("f");
        }

        private static ProceduralPanel PanelOf(Frame f) => f.GameObject.GetComponent<ProceduralPanel>();

        // ---- mode switching ----

        [Test]
        public void Glass_AttachesPanel_AndFlipsMode()
        {
            var p = PanelOf(Load("glass='true'"));
            Assert.IsNotNull(p);
            Assert.IsTrue(p.CurrentParams.Glass);
        }

        [Test]
        public void Glass_WithNoFill_IsStillVisible()
        {
            // The blurred backdrop *is* the visual — a glass panel with no colour is the common case,
            // and must not be optimised away as "fully transparent".
            var f = Load("glass='true' width='100' height='50'");
            Assert.IsTrue(PanelOf(f).IsPanelVisible);

            var vh = new VertexHelper();
            PanelOf(f).BuildMeshForTests(vh);
            Assert.AreEqual(4, vh.currentVertCount);
        }

        [Test]
        public void GlassOff_ZeroesGlassParams_SoTheCacheDoesNotFragment()
        {
            // Parameters nothing reads must not reach the material key: two panels that render
            // identically have to hash to the same material or they stop batching.
            var withStrayParams = PanelOf(Load("color='#222' frost='0.9' depth='12'")).CurrentParams;
            var plain = PanelOf(Load("color='#222'")).CurrentParams;
            Assert.IsFalse(withStrayParams.Glass);
            Assert.AreEqual(plain, withStrayParams);
        }

        [Test]
        public void NonGlassPanel_KeepsUsingTheOpaqueShader()
        {
            var f = Load("color='#ff0000' width='100' height='50'");
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual("UI/ProceduralPanel",
                PanelOf(f).canvasRenderer.GetMaterial(0).shader.name);
        }

        // ---- defaults & round-trip ----

        [Test]
        public void Glass_Defaults()
        {
            var g = PanelOf(Load("glass='true'")).CurrentParams.GlassParams;
            Assert.AreEqual(GlassAttrParser.DefaultFrost, g.Frost, 1e-4f);
            Assert.AreEqual(GlassAttrParser.DefaultDepth, g.Depth, 1e-4f);
            Assert.AreEqual(GlassAttrParser.DefaultDispersion, g.Dispersion, 1e-4f);
            Assert.AreEqual(GlassAttrParser.DefaultLightAngle, g.LightAngle, 1e-4f);
            Assert.AreEqual(GlassAttrParser.DefaultLightIntensity, g.LightIntensity, 1e-4f);
            Assert.AreEqual(GlassAttrParser.DefaultSaturation, g.Saturation, 1e-4f);
            Assert.AreEqual(GlassAttrParser.DefaultNoise, g.Noise, 1e-4f);
        }

        [Test]
        public void Glass_AllParamsRoundTrip()
        {
            var g = PanelOf(Load(
                "glass='true' frost='0.8' depth='6' dispersion='0.3' " +
                "lightAngle='-30' lightIntensity='0.4' saturation='1.6' noise='0.05'"))
                .CurrentParams.GlassParams;
            Assert.AreEqual(0.8f, g.Frost, 1e-4f);
            Assert.AreEqual(6f, g.Depth, 1e-4f);
            Assert.AreEqual(0.3f, g.Dispersion, 1e-4f);
            Assert.AreEqual(-30f, g.LightAngle, 1e-4f);
            Assert.AreEqual(0.4f, g.LightIntensity, 1e-4f);
            Assert.AreEqual(1.6f, g.Saturation, 1e-4f);
            Assert.AreEqual(0.05f, g.Noise, 1e-4f);
        }

        [Test]
        public void EmptyValue_RevertsToDefault_NotAnError()
        {
            // A Variant can only overwrite a value, never remove the attribute — "" is the author's
            // only way back to the default.
            var g = PanelOf(Load("glass='true' frost=''")).CurrentParams.GlassParams;
            Assert.AreEqual(GlassAttrParser.DefaultFrost, g.Frost, 1e-4f);
        }

        [Test]
        public void GlassAttrs_AloneDoNotEnableGlass()
        {
            Assert.IsFalse(PanelOf(Load("color='#222' frost='0.9'")).CurrentParams.Glass,
                "frost without glass=\"true\" is an authoring mistake, not an implicit opt-in");
        }

        // ---- value errors ----

        [TestCase("frost='1.5'")]
        [TestCase("frost='-0.1'")]
        [TestCase("dispersion='2'")]
        [TestCase("lightIntensity='-1'")]
        [TestCase("noise='4'")]
        [TestCase("depth='-1'")]
        [TestCase("saturation='-0.5'")]
        public void OutOfRange_Throws(string attr)
        {
            Assert.Throws<ParseException>(() => Load($"glass='true' {attr}"));
        }

        [Test]
        public void NonNumeric_ThrowsNamingTheAttribute()
        {
            var ex = Assert.Throws<ParseException>(() => Load("glass='true' frost='heavy'"));
            StringAssert.Contains("frost", ex.Message);
        }

        [Test]
        public void Glass_RejectsNonBoolean()
        {
            // "yes" would silently read as false and leave the author staring at a plain box.
            var ex = Assert.Throws<ParseException>(() => Load("glass='yes'"));
            StringAssert.Contains("glass", ex.Message);
        }

        [Test]
        public void LightAngle_AcceptsTheWholeNumberLine()
        {
            // An angle is cyclic; clamping it would silently move the highlight somewhere else.
            var negative = PanelOf(Load("glass='true' lightAngle='-30'"))
                .CurrentParams.GlassParams.LightAngle;
            var overFullTurn = PanelOf(Load("glass='true' lightAngle='400'"))
                .CurrentParams.GlassParams.LightAngle;
            Assert.AreEqual(-30f, negative, 1e-4f);
            Assert.AreEqual(400f, overFullTurn, 1e-4f);
        }

        // ---- material sharing ----

        [Test]
        public void IdenticalGlassStyles_ShareOneMaterial()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Style name='glassy' glass='true' frost='0.6' radius='16'/>
  <Screen name='S'>
    <Frame id='a' class='glassy' height='40'/>
    <Frame id='b' class='glassy' height='90'/>
  </Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            Assert.AreSame(PanelOf(s.Get<Frame>("a")).material,
                           PanelOf(s.Get<Frame>("b")).material);
        }

        [Test]
        public void GlassAndOpaque_DoNotShareAMaterial()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='a' color='#222' glass='true'/>
  <Frame id='b' color='#222'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var glass = PanelOf(s.Get<Frame>("a")).material;
            var opaque = PanelOf(s.Get<Frame>("b")).material;
            Assert.AreNotSame(glass, opaque);
            Assert.AreEqual("UI/GlassPanel", glass.shader.name);
            Assert.AreEqual("UI/ProceduralPanel", opaque.shader.name);
        }

        [Test]
        public void ClosingScreen_ReleasesGlassMaterials()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='a' glass='true'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var before = ProceduralMaterialCache.LiveMaterialCount;
            var s = UI.Open("S");
            Assert.Greater(ProceduralMaterialCache.LiveMaterialCount, before);
            s.Close();
            Assert.AreEqual(before, ProceduralMaterialCache.LiveMaterialCount);
        }

        [Test]
        public void SpareMaterials_AreNotReusedAcrossShaders()
        {
            // The spare pool exists so a tweened colour never allocates — but a released glass
            // material must never come back as an opaque one, or the panel renders with the wrong
            // shader entirely.
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='a' glass='true'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            s.Close();

            const string xml2 = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='T'>
  <Frame id='b' color='#123456'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t2", xml2);
            var t = UI.Open("T");
            Assert.AreEqual("UI/ProceduralPanel",
                PanelOf(t.Get<Frame>("b")).material.shader.name);
        }

        // ---- rendering reaches the GPU (EditMode green is not evidence, see procedural-style §12.2) ----

        [Test]
        public void CanvasRebuild_ReachesTheCanvasRenderer()
        {
            var f = Load("glass='true' width='100' height='50'");
            Canvas.ForceUpdateCanvases();

            var cr = PanelOf(f).canvasRenderer;
            Assert.Greater(cr.materialCount, 0);
            Assert.AreEqual("UI/GlassPanel", cr.GetMaterial(0).shader.name);
        }

        [Test]
        public void Glass_OptsCanvasIntoTexCoord1()
        {
            var f = Load("glass='true'");
            var canvas = f.GameObject.GetComponentInParent<Canvas>();
            Assert.AreNotEqual(0,
                canvas.additionalShaderChannels & AdditionalCanvasShaderChannels.TexCoord1);
        }

        // ---- global availability flag ----

        [Test]
        public void BackdropUnavailable_ByDefault_InEditMode()
        {
            // No URP capture has run, so every glass panel must be drawing the fallback. The flag is
            // a global uniform rather than a shader keyword on purpose: flipping it costs nothing and
            // never re-keys a single material (see spec §12).
            Load("glass='true'");
            Assert.AreEqual(0f, Shader.GetGlobalFloat(GlassRuntime.BackdropAvailableProperty));
        }

        [Test]
        public void GlassEnabledToggle_DrivesTheGlobalFlag_NotTheMaterialKey()
        {
            var f = Load("glass='true'");
            var before = PanelOf(f).material;
            var keysBefore = ProceduralMaterialCache.LiveMaterialCount;

            GlassRuntime.SetBackdropAvailableForTests(true);
            Assert.AreEqual(1f, Shader.GetGlobalFloat(GlassRuntime.BackdropAvailableProperty));

            UI.Glass.Enabled = false;
            Assert.AreEqual(0f, Shader.GetGlobalFloat(GlassRuntime.BackdropAvailableProperty),
                "turning glass off must reach the shader");
            Assert.AreSame(before, PanelOf(f).material,
                "a quality toggle must not churn materials");
            Assert.AreEqual(keysBefore, ProceduralMaterialCache.LiveMaterialCount);
        }

        [Test]
        public void ResetForTests_RestoresGlassDefaults()
        {
            UI.Glass.Enabled = false;
            UI.ResetForTests();
            Assert.IsTrue(UI.Glass.Enabled);
            Assert.IsNull(UI.Glass.Camera);
        }

        // ---- the backdrop pipeline only runs when something needs it ----

        [Test]
        public void NoGlassOnScreen_LeavesTheBackdropSystemAsleep()
        {
            Load("color='#222'");
            Assert.AreEqual(0, GlassRuntime.ActivePanelCount);
        }

        [Test]
        public void GlassPanels_AreCounted_AndReleasedOnClose()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='a' glass='true'/>
  <Frame id='b' glass='true'/>
  <Frame id='c' color='#222'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            Assert.AreEqual(2, GlassRuntime.ActivePanelCount);
            s.Close();
            Assert.AreEqual(0, GlassRuntime.ActivePanelCount,
                "a closed screen must not keep the blur chain running every frame");
        }

        [Test]
        public void FlippingGlassOff_DecrementsTheCount()
        {
            var f = Load("glass='true'");
            Assert.AreEqual(1, GlassRuntime.ActivePanelCount);
            f.Glass = "false";
            Assert.AreEqual(0, GlassRuntime.ActivePanelCount);
        }
    }
}
