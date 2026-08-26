using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class FrameMaskTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void NoMaskAttr_NoRectMask2D()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var f = s.Get<Frame>("f");
            Assert.IsNull(f.GameObject.GetComponent<RectMask2D>(),
                "Frame without mask attr should not auto-add RectMask2D");
        }

        [Test]
        public void MaskRect_AddsRectMask2D()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' mask='rect'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var f = s.Get<Frame>("f");
            Assert.IsNotNull(f.GameObject.GetComponent<RectMask2D>());
        }

        [Test]
        public void MaskRectWithPadding_AppliesPadding_TRBL_Flipped()
        {
            // Author "1,2,3,4" (T,R,B,L) → Unity Vector4(L,B,R,T) = (4,3,2,1)
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' mask='rect' maskPadding='1,2,3,4'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var f = s.Get<Frame>("f");
            var rm = f.GameObject.GetComponent<RectMask2D>();
            Assert.IsNotNull(rm);
            Assert.AreEqual(new Vector4(4f, 3f, 2f, 1f), rm.padding);
        }

        [Test]
        public void MaskPaddingWithoutMaskRect_NoRectMask2D()
        {
            // PUI-MASK-PADDING-NO-RECT 已 warn,但 runtime 仍要"安全":
            // 只写 maskPadding 没写 mask=rect → 不挂 RectMask2D。
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' maskPadding='8'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);

            // Swallow the PUI-MASK-PADDING-NO-RECT warning so the test framework
            // doesn't flag it as an unexpected log.
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(
                    @"maskPadding only takes effect with mask=""rect"""));

            var s = UI.Open("S");
            var f = s.Get<Frame>("f");
            Assert.IsNull(f.GameObject.GetComponent<RectMask2D>());
        }

        // ===== mask="self": stencil-clip children to the procedural shape (spec §9) =====
        //
        // Only reachable when the Frame draws something of its own. `Mask` needs a Graphic on its
        // own GameObject, and a Frame only grows one — a lazily attached ProceduralPanel — when the
        // author writes a procedural visual attribute. That is the whole rule (spec §9.2), and it is
        // why the three negative cases below all end in "no Mask component".

        private static Frame Load(string frameAttrs, string children = "")
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Frame id='f' {frameAttrs}>{children}</Frame>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Frame>("f");
        }

        private static void ExpectFrameSelfWarning() =>
            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning,
                new System.Text.RegularExpressions.Regex(@"mask=""self"""));

        [Test]
        public void MaskSelf_WithProceduralAttr_AddsStencilMaskBackedByThePanel()
        {
            var f = Load("radius='16' color='#3366ff' mask='self'");

            var mask = f.GameObject.GetComponent<UnityEngine.UI.Mask>();
            Assert.IsNotNull(mask, "a procedural Frame has a Graphic, so it can be a stencil mask");
            Assert.IsNull(f.GameObject.GetComponent<RectMask2D>(),
                "mask=\"self\" is the stencil mask; RectMask2D is mask=\"rect\"");
            Assert.AreSame(f.GameObject.GetComponent<ProceduralPanel>(), mask.graphic,
                "the SDF panel must be the mask source — that is what makes the clip rounded");
            Assert.IsTrue(mask.MaskEnabled());
        }

        [Test]
        public void MaskSelf_WithoutProceduralAttr_NoStencilMask()
        {
            // The historical case PUI-MASK-FRAME-SELF was written for: nothing to mask with.
            ExpectFrameSelfWarning();
            var f = Load("mask='self'");

            Assert.IsNull(f.GameObject.GetComponent<UnityEngine.UI.Mask>());
            Assert.IsNull(f.GameObject.GetComponent<Graphic>());
        }

        [Test]
        public void MaskSelf_OnAWeldCarrier_NoStencilMask()
        {
            // The fused pane lives on a GlassWeld child, so the carrier itself still has no Graphic
            // and a Mask here would clip everything away (spec §9.2).
            ExpectFrameSelfWarning();
            var f = Load("weld='16' mask='self'",
                         "<Frame glass='true' radius='8'/><Frame glass='true' radius='8'/>");

            Assert.IsNull(f.GameObject.GetComponent<UnityEngine.UI.Mask>());
        }

        [Test]
        public void MaskSelf_ShowMaskFalse_HidesTheMaskGraphic()
        {
            var f = Load("radius='16' color='#3366ff' mask='self' showMask='false'");

            Assert.IsFalse(f.GameObject.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic,
                "an invisible rounded clipper is the most useful form of this");
        }

        [Test]
        public void MaskSelf_ShowMaskDefaults_ToDrawingThePanel()
        {
            var f = Load("radius='16' color='#3366ff' mask='self'");

            Assert.IsTrue(f.GameObject.GetComponent<UnityEngine.UI.Mask>().showMaskGraphic,
                "the author drew a panel; masking with it must not make it disappear");
        }

        [Test]
        public void MaskRect_WithProceduralAttr_StaysRectMask2D()
        {
            var f = Load("radius='16' color='#3366ff' mask='rect'");

            Assert.IsNotNull(f.GameObject.GetComponent<RectMask2D>());
            Assert.IsNull(f.GameObject.GetComponent<UnityEngine.UI.Mask>(),
                "rect clipping is cheaper and must not silently become a stencil mask");
        }

        [Test]
        public void MaskSelf_SurvivesReSolve_WithoutStackingComponents()
        {
            // ReSolve replays every attribute; mask reconciliation has to be idempotent or a
            // variant flip would pile up Mask components (spec §8: compute, do not latch).
            var f = Load("radius='16' color='#3366ff' mask='self'");

            UI.Variants.Set("mobile", true);
            UI.Variants.Set("mobile", false);

            Assert.AreEqual(1, f.GameObject.GetComponents<UnityEngine.UI.Mask>().Length);
            Assert.IsTrue(f.GameObject.GetComponent<UnityEngine.UI.Mask>().MaskEnabled());
        }
    }
}
