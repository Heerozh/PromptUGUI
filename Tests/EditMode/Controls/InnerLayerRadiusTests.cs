using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Lint;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// M3: shape for the layers <em>inside</em> a control — a Slider's fill and handle, a Progress's
    /// fill and frame (procedural-surface spec §6).
    ///
    /// <para><b>Shape only.</b> Inner layers get <c>&lt;layer&gt;Radius</c> and nothing else. Not to
    /// save attributes — glass on an inner layer is semantically wrong: the backdrop capture excludes
    /// the UI itself, so a glass fill sitting on a glass track samples the <em>same</em> backdrop and
    /// the two come out identical, erasing the bar. The colour half is already covered by the
    /// existing <c>fillColor</c> / <c>handleColor</c> pairs.</para>
    ///
    /// <para><b>Progress rounds through its mask, not its fill.</b> <c>radius=</c> shapes the bg, and
    /// the fill is a separate square-cornered Image on top of it, so a bar shaped that way is rounded
    /// only at its trailing end. The fix is to clip bg and fill together, which is what the mask on
    /// <c>MaskWrapper</c> is for — so <c>maskRadius</c> auto-tracks <c>radius</c>, exactly the way
    /// <c>&lt;ScrollList mask&gt;</c> auto-tracks its bg sprite and <c>&lt;Dropdown popupMask&gt;</c>
    /// tracks <c>popupSprite</c>.</para>
    /// </summary>
    public class InnerLayerRadiusTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Control Load(string tag, string attrs)
        {
            UI.UnloadAll();
            UI.LoadDocument("t", $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <{tag} id='x' anchor='center' width='200' height='30' {attrs}/>
</Screen></PromptUGUI>");
            return (Control)(object)UI.Open("S").Get<Control>("x");
        }

        /// <summary>The procedural surface inside the named descendant layer, if any.</summary>
        private static ProceduralPanel SurfaceUnder(Control c, string layerPath)
        {
            var layer = c.GameObject.transform.Find(layerPath);
            Assert.IsNotNull(layer, $"no layer at '{layerPath}'");
            var node = layer.Find(ProceduralSurface.NodeName);
            return node == null ? null : node.GetComponent<ProceduralPanel>();
        }

        private static System.Collections.Generic.List<LintIssue> Walk(string body)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
{body}
</Screen></PromptUGUI>";
            return IRWalker.Walk(UIDocumentParser.Parse(xml)).ToList();
        }

        private static bool Has(System.Collections.Generic.List<LintIssue> issues, string code)
            => issues.Any(i => i.Code == code);

        // ===== Slider =====

        [Test]
        public void Slider_FillRadius_ShapesTheFill()
        {
            var s = Load("Slider", "fillRadius='6' fillColor='#ffcc33'");

            var panel = SurfaceUnder(s, "Fill Area/Fill");
            Assert.IsNotNull(panel, "fillRadius must give the filled segment its own surface");
            Assert.AreEqual(6f, panel.CurrentParams.CornerWidth.x);
            Assert.IsTrue(panel.IsPanelVisible, "fillColor has to reach it, or the bar disappears");
        }

        [Test]
        public void Slider_HandleRadius_ShapesTheHandle()
        {
            var s = Load("Slider", "handleRadius='pill' handleColor='#fff'");

            var panel = SurfaceUnder(s, "Handle Slide Area/Handle");
            Assert.IsNotNull(panel);
            Assert.IsTrue(panel.CurrentParams.Pill, "'pill' is the whole point of a round knob");
        }

        [Test]
        public void Slider_InnerRadius_LeavesTheTrackAlone()
        {
            var s = Load("Slider", "fillRadius='6'");

            Assert.IsNull(SurfaceUnder(s, "Background"),
                "fillRadius says nothing about the track; only radius= does");
        }

        [Test]
        public void Slider_TrackAndInnerLayers_AreIndependent()
        {
            var s = Load("Slider", "radius='4' fillRadius='6' handleRadius='pill'");

            Assert.AreEqual(4f, SurfaceUnder(s, "Background").CurrentParams.CornerWidth.x);
            Assert.AreEqual(6f, SurfaceUnder(s, "Fill Area/Fill").CurrentParams.CornerWidth.x);
            Assert.IsTrue(SurfaceUnder(s, "Handle Slide Area/Handle").CurrentParams.Pill);
        }

        // ===== Progress =====

        [Test]
        public void Progress_FillRadius_ShapesTheFill()
        {
            var p = Load("Progress", "fillRadius='6' fillColor='#ffcc33' value='0.6'");

            var panel = SurfaceUnder(p, "MaskWrapper/Fill");
            Assert.IsNotNull(panel);
            Assert.AreEqual(6f, panel.CurrentParams.CornerWidth.x);
        }

        [Test]
        public void Progress_FrameRadius_ShapesTheFrame()
        {
            var p = Load("Progress", "frameRadius='10' frameColor='#ffd56b'");

            Assert.IsNotNull(SurfaceUnder(p, "Frame"));
            Assert.IsTrue(p.GameObject.transform.Find("Frame").gameObject.activeSelf,
                "asking the frame for a shape has to switch the layer on, same as frameColor does");
        }

        // ===== Progress: the mask is how a bar gets rounded end to end =====

        [Test]
        public void Progress_Radius_AutoTracksTheMask()
        {
            var p = Load("Progress", "radius='12' bgColor='#22345a' fillColor='#ffcc33' value='0.6'");
            var wrapper = p.GameObject.transform.Find("MaskWrapper").gameObject;

            var mask = wrapper.GetComponent<UnityEngine.UI.Mask>();
            Assert.IsNotNull(mask, "without this the fill's square corner covers the rounded end");
            Assert.IsInstanceOf<ProceduralPanel>(mask.graphic,
                "the clip shape comes from the SDF, so no sprite is needed for a rounded bar");
            Assert.IsFalse(mask.showMaskGraphic, "it is a clipper, not a layer — bg is the track");
            Assert.IsTrue(mask.MaskEnabled());
        }

        [Test]
        public void Progress_ExplicitMaskRadius_WinsOverRadius()
        {
            var p = Load("Progress", "radius='4' maskRadius='12' bgColor='#22345a'");
            var mask = p.GameObject.transform.Find("MaskWrapper").GetComponent<UnityEngine.UI.Mask>();

            Assert.AreEqual(12f, ((ProceduralPanel)mask.graphic).CurrentParams.CornerWidth.x);
        }

        [Test]
        public void Progress_EmptyMaskRadius_OptsOutOfAutoTracking()
        {
            // A Variant can change a value but never remove an attribute, so "" is the only spelling
            // an author has for "no mask, thanks" — same contract as <ScrollList mask="">.
            var p = Load("Progress", "radius='12' maskRadius='' bgColor='#22345a'");

            Assert.IsNull(p.GameObject.transform.Find("MaskWrapper").GetComponent<UnityEngine.UI.Mask>());
        }

        [Test]
        public void Progress_SpriteMask_OptsOutOfAutoTracking()
        {
            // mask= already puts an Image on MaskWrapper, and Graphic is [DisallowMultipleComponent],
            // so an auto-tracked procedural mask could not attach even if it wanted to.
            var p = Load("Progress", "radius='12' mask='pugui_9slice_round' bgColor='#22345a'");
            var wrapper = p.GameObject.transform.Find("MaskWrapper").gameObject;

            Assert.IsInstanceOf<UnityEngine.UI.Image>(
                wrapper.GetComponent<UnityEngine.UI.Mask>().graphic,
                "the authored sprite mask stands");
            Assert.AreEqual(1, wrapper.GetComponents<Graphic>().Length);
        }

        [Test]
        public void Progress_NoRadius_StillHasNoMask()
        {
            Assert.IsNull(Load("Progress", "bgColor='#22345a'")
                .GameObject.transform.Find("MaskWrapper").GetComponent<UnityEngine.UI.Mask>(),
                "auto-tracking must not conjure a mask nobody asked for");
        }

        // ===== the attribute surface stays narrow =====

        [Test]
        public void InnerLayers_TakeRadiusOnly()
        {
            var slider = UI.Registry.Resolve("Slider").Meta;
            var progress = UI.Registry.Resolve("Progress").Meta;

            Assert.IsTrue(slider.HasAttribute("fillRadius"));
            Assert.IsTrue(slider.HasAttribute("handleRadius"));
            Assert.IsTrue(progress.HasAttribute("fillRadius"));
            Assert.IsTrue(progress.HasAttribute("frameRadius"));
            Assert.IsTrue(progress.HasAttribute("maskRadius"));

            foreach (var bad in new[] { "fillGlass", "fillBorderWidth", "handleGlass", "frameGlow" })
            {
                Assert.IsFalse(slider.HasAttribute(bad), $"Slider.{bad} would be attribute explosion");
                Assert.IsFalse(progress.HasAttribute(bad), $"Progress.{bad} would be attribute explosion");
            }
        }

        // ===== lint =====

        [Test]
        public void Progress_ModeFill_WithFillRadius_IsAConflict()
        {
            Assert.IsTrue(
                Has(Walk("<Progress id='p' mode='fill' fillRadius='6'/>"),
                    ProgressAttributeRules.FillRadiusModeCode),
                "mode='fill' drives Image.type=Filled + fillAmount, which a ProceduralPanel has no "
                + "equivalent for — this one genuinely cannot work");
        }

        [Test]
        public void Progress_ModeScale_WithFillRadius_IsFine()
        {
            Assert.IsFalse(
                Has(Walk("<Progress id='p' fillRadius='6'/>"), ProgressAttributeRules.FillRadiusModeCode),
                "the default mode anchors the rect instead, which a panel handles perfectly");
        }

        [Test]
        public void Progress_MaskAndMaskRadius_IsAConflict()
        {
            Assert.IsTrue(
                Has(Walk("<Progress id='p' mask='ui:pill' maskRadius='12'/>"),
                    ProgressAttributeRules.MaskRadiusConflictCode),
                "one Graphic per GameObject — the two mask sources cannot coexist");
        }
    }
}
