using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
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
    }
}
