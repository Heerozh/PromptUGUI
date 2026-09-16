using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Every SDF colour slot carries a full <see cref="ColorSpec"/> into the material key
    /// (spec 2026-09-17 §6.2 / §7): fill, border, outer glow and inner glow, on the panel and on
    /// &lt;Decor&gt;. The key is the only place a gradient can be honoured, and the key is also
    /// what keeps identically-styled panels on one material — so the defaults must hash exactly
    /// as they did before the slots widened.
    /// </summary>
    public class GradientSlotPanelTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Load(string body)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{body}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S");
        }

        private static ProceduralPanel FramePanel(string attrs)
            => Load($"<Frame id='f' {attrs}/>").Get<Frame>("f").GameObject.GetComponent<ProceduralPanel>();

        // ── fill: direction and a third stop reach the key ──────────────────────

        [Test]
        public void Fill_Direction_ReachesTheKey()
        {
            var p = FramePanel("color='to right, #ff0000, #0000ff'");
            Assert.AreEqual(2, p.CurrentParams.Fill.Count);
            Assert.AreEqual(90f, p.CurrentParams.Fill.Direction.AngleDeg, 1e-5f);
        }

        [Test]
        public void Fill_ThreeStops_ReachTheKey()
        {
            var p = FramePanel("color='to bottom right, #ff0000, #00ff00 50%, #0000ff'");
            Assert.AreEqual(3, p.CurrentParams.Fill.Count);
            Assert.AreEqual(GradientDirection.Corner(1, -1), p.CurrentParams.Fill.Direction);
            Assert.AreEqual(Color.green, p.CurrentParams.Fill.ColorAt(1));
        }

        // ── border / glow / inner glow accept the grammar ───────────────────────

        [Test]
        public void Border_Gradient_ReachesTheKey()
        {
            var p = FramePanel("borderWidth='2' borderColor='to right, #ff0000, #0000ff'");
            Assert.IsTrue(p.CurrentParams.Border.IsGradient);
            Assert.AreEqual(Color.red, p.CurrentParams.Border.Start);
            Assert.AreEqual(Color.blue, p.CurrentParams.Border.End);
            Assert.AreEqual(90f, p.CurrentParams.Border.Direction.AngleDeg, 1e-5f);
        }

        [Test]
        public void Border_Solid_IsASolidSpec()
        {
            var p = FramePanel("borderWidth='2' borderColor='#00ff00'");
            Assert.IsFalse(p.CurrentParams.Border.IsGradient);
            Assert.AreEqual(Color.green, p.CurrentParams.Border.Start);
        }

        [Test]
        public void Glow_Gradient_ReachesTheKey()
        {
            var p = FramePanel("glow='8' glowColor='#ff0000, #0000ff, #00ff00'");
            Assert.AreEqual(3, p.CurrentParams.Glow.Count);
        }

        [Test]
        public void InnerGlow_Gradient_ReachesTheKey()
        {
            var p = FramePanel("innerGlow='8' innerGlowColor='45deg, #ff0000, #0000ff'");
            Assert.IsTrue(p.CurrentParams.InnerGlow.IsGradient);
            Assert.AreEqual(45f, p.CurrentParams.InnerGlow.Direction.AngleDeg, 1e-5f);
        }

        // ── glow default follows the whole fill ramp ────────────────────────────

        [Test]
        public void Glow_Unset_FollowsTheFillGradient_Opaque()
        {
            var p = FramePanel("color='to right, #ff000080, #0000ff40 70%' glow='8'");
            var glow = p.CurrentParams.Glow;
            Assert.IsTrue(glow.IsGradient, "glow takes the whole ramp, not the first stop");
            Assert.AreEqual(90f, glow.Direction.AngleDeg, 1e-5f);
            Assert.AreEqual(0.7f, glow.EndStop, 1e-5f);
            Assert.AreEqual(1f, glow.Start.a, 1e-6f, "opaque");
            Assert.AreEqual(1f, glow.End.a, 1e-6f, "opaque");
            Assert.AreEqual(1f, glow.Start.r, 1e-6f);
            Assert.AreEqual(1f, glow.End.b, 1e-6f);
        }

        [Test]
        public void Glow_Unset_SolidFill_IsTheOpaqueFillAsBefore()
        {
            var p = FramePanel("color='#ff000080' glow='8'");
            Assert.IsFalse(p.CurrentParams.Glow.IsGradient);
            Assert.AreEqual(new Color(1f, 0f, 0f, 1f), p.CurrentParams.Glow.Start);
        }

        [Test]
        public void Glow_Unset_NoFill_IsWhite()
        {
            var p = FramePanel("glow='8'");
            Assert.AreEqual(Color.white, p.CurrentParams.Glow.Start);
        }

        // ── disabled greying hits every stop, keeps the shape ───────────────────

        [Test]
        public void Disabled_GreysEveryStop_OfEverySlot()
        {
            var screen = Load("<Btn id='b' radius='8' color='to right, #ff0000, #0000ff' borderWidth='2' borderColor='#00ff00, #ff00ff' interactable='false'>x</Btn>");
            var panel = screen.Get<Btn>("b").GameObject.GetComponentInChildren<ProceduralPanel>(true);
            Assert.IsNotNull(panel);
            var fill = panel.CurrentParams.Fill;
            Assert.AreEqual(fill.Start.r, fill.Start.g, 1e-6f);
            Assert.AreEqual(fill.End.r, fill.End.b, 1e-6f);
            Assert.AreEqual(90f, fill.Direction.AngleDeg, 1e-5f, "shape survives the greying");
            var border = panel.CurrentParams.Border;
            Assert.AreEqual(border.Start.r, border.Start.g, 1e-6f);
            Assert.IsTrue(border.IsGradient);
        }

        // ── the cache keys on every slot ────────────────────────────────────────

        [Test]
        public void PanelParams_DifferentBorderGradients_AreDifferentKeys()
        {
            var a = FramePanel("borderWidth='2' borderColor='to right, #ff0000, #0000ff'").CurrentParams;
            UI.ResetForTests();
            var b = FramePanel("borderWidth='2' borderColor='to left, #ff0000, #0000ff'").CurrentParams;
            Assert.AreNotEqual(a, b);
        }

        [Test]
        public void PanelParams_SolidSlots_HashTheSameAsBefore()
        {
            var a = FramePanel("color='#ff0000' borderWidth='2' borderColor='#00ff00' glow='4' innerGlow='4'").CurrentParams;
            UI.ResetForTests();
            var b = FramePanel("color='#ff0000' borderWidth='2' borderColor='#00ff00' glow='4' innerGlow='4'").CurrentParams;
            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        // ── the other hosts of the slots ────────────────────────────────────────

        [Test]
        public void Btn_Border_Gradient()
        {
            var screen = Load("<Btn id='b' radius='8' borderWidth='2' borderColor='to right, #ff0000, #0000ff'>x</Btn>");
            var panel = screen.Get<Btn>("b").GameObject.GetComponentInChildren<ProceduralPanel>(true);
            Assert.IsNotNull(panel);
            Assert.IsTrue(panel.CurrentParams.Border.IsGradient);
        }

        [Test]
        public void Scrollbar_HandleBorderAndGlow_Gradients()
        {
            var screen = Load(
                "<ScrollList id='sl' width='100' height='100'>" +
                "<Scrollbar id='sb' handleRadius='4' handleBorderWidth='1' handleBorderColor='to right, #ff0000, #0000ff' " +
                "handleGlow='4' handleGlowColor='#00ff00, #ff00ff, #ffff00'/>" +
                "<Frame height='300'/></ScrollList>");
            var bar = screen.Get<Scrollbar>("sb");
            var panels = bar.GameObject.GetComponentsInChildren<ProceduralPanel>(true);
            ProceduralPanel handle = null;
            foreach (var p in panels)
                if (p.CurrentParams.Border.IsGradient) handle = p;
            Assert.IsNotNull(handle, "the handle surface carries the gradient border");
            Assert.AreEqual(3, handle.CurrentParams.Glow.Count);
        }

        [Test]
        public void Decor_Glow_Gradient()
        {
            var screen = Load("<Frame id='f' width='100' height='60'><Decor id='d' kind='line' at='bottom' glow='6' glowColor='to right, #ff0000, #0000ff'/></Frame>");
            var panel = screen.Get<Decor>("d").GameObject.GetComponentInChildren<DecorPanel>(true);
            Assert.IsNotNull(panel);
            Assert.IsTrue(panel.CurrentParams.Glow.IsGradient);
            Assert.AreEqual(90f, panel.CurrentParams.Glow.Direction.AngleDeg, 1e-5f);
        }

        [Test]
        public void Decor_Glow_Unset_FollowsAGradientFill()
        {
            var screen = Load("<Frame id='f' width='100' height='60'><Decor id='d' kind='line' at='bottom' glow='6' color='to right, #ff0000, #0000ff'/></Frame>");
            var panel = screen.Get<Decor>("d").GameObject.GetComponentInChildren<DecorPanel>(true);
            Assert.IsTrue(panel.CurrentParams.Glow.IsGradient);
            Assert.AreEqual(1f, panel.CurrentParams.Glow.End.a, 1e-6f);
        }

        // ── the uniforms the shader reads ───────────────────────────────────────

        [Test]
        public void Configure_WritesEveryRampUniform()
        {
            var shader = Resources.Load<Shader>("PromptUGUI/Material/UI-ProceduralPanel");
            Assert.IsNotNull(shader, "shader missing from Resources");
            var mat = new Material(shader);
            try
            {
                var spec = ColorSpec.Gradient(GradientDirection.Corner(1, -1),
                                              new[] { Color.red, Color.green, Color.blue },
                                              new[] { 0f, 0.4f, 1f }, new[] { 1f, 2f });
                GradientUniforms.Write(mat, GradientUniforms.Border, spec);
                Assert.AreEqual(Color.red, mat.GetColor("_Border0"));
                Assert.AreEqual(Color.green, mat.GetColor("_Border1"));
                Assert.AreEqual(Color.blue, mat.GetColor("_Border2"));
                Assert.AreEqual(Color.blue, mat.GetColor("_Border3"), "unused slot repeats the last stop");
                Assert.AreEqual(new Vector4(0f, 0.4f, 1f, 1f), mat.GetVector("_BorderStops"));
                Assert.AreEqual(new Vector4(1f, 2f, 1f, 3f), mat.GetVector("_BorderCurves"), "xyz = curves, w = count");
                var dir = mat.GetVector("_BorderDir");
                Assert.AreEqual(1f, dir.z, "corner mode flag");
                Assert.AreEqual(2f, dir.w, "CSS corner index of bottom-right");

                GradientUniforms.Write(mat, GradientUniforms.Fill, ColorSpec.Gradient(Color.white, Color.black).WithDirection(GradientDirection.Angle(90f)));
                var fillDir = mat.GetVector("_FillDir");
                Assert.AreEqual(1f, fillDir.x, 1e-5f);
                Assert.AreEqual(0f, fillDir.y, 1e-5f);
                Assert.AreEqual(0f, fillDir.z);
                Assert.AreEqual(2f, mat.GetVector("_FillCurves").w);

                GradientUniforms.Write(mat, GradientUniforms.Glow, ColorSpec.Solid(Color.cyan));
                Assert.AreEqual(1f, mat.GetVector("_GlowCurves").w, "a solid is count 1");
                Assert.AreEqual(Color.cyan, mat.GetColor("_Glow0"));
            }
            finally
            {
                Object.DestroyImmediate(mat);
            }
        }
    }
}
