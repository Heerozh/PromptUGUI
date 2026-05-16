using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;
using PromptUGUIImage = PromptUGUI.Controls.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ImageMaskTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void NoMaskAttr_NoMaskComponents()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='pugui'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            Assert.IsNull(img.GameObject.GetComponent<RectMask2D>());
            Assert.IsNull(img.GameObject.GetComponent<Mask>());
        }

        [Test]
        public void MaskRect_AddsRectMask2D_NotStencilMask()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' mask='rect'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            Assert.IsNotNull(img.GameObject.GetComponent<RectMask2D>());
            Assert.IsNull(img.GameObject.GetComponent<Mask>());
        }

        [Test]
        public void MaskRectWithPadding_AppliesPadding()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' mask='rect' maskPadding='1,2,3,4'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            var rm = img.GameObject.GetComponent<RectMask2D>();
            Assert.IsNotNull(rm);
            Assert.AreEqual(new Vector4(4f, 3f, 2f, 1f), rm.padding);
        }

        [Test]
        public void MaskSelf_WithSprite_AddsStencilMask_NotRectMask()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='pugui#pugui_9slice_round' mask='self'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            Assert.IsNotNull(img.GameObject.GetComponent<Mask>());
            Assert.IsNull(img.GameObject.GetComponent<RectMask2D>());
        }

        [Test]
        public void MaskSelf_DefaultShowMaskGraphicTrue()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='pugui#pugui_9slice_round' mask='self'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            var m = img.GameObject.GetComponent<Mask>();
            Assert.IsTrue(m.showMaskGraphic, "default showMask=true (FIM-D5)");
        }

        [Test]
        public void MaskSelf_ShowMaskFalse_HidesGraphic()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='i' sprite='pugui#pugui_9slice_round' mask='self' showMask='false'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var img = s.Get<PromptUGUIImage>("i");
            var m = img.GameObject.GetComponent<Mask>();
            Assert.IsFalse(m.showMaskGraphic);
        }

        [Test]
        public void NestedMaskShape_OuterAndInnerImageBoth()
        {
            // §4 用例 5: 外层装饰 + 内层 stencil mask
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='outer' sprite='pugui#pugui_9slice_round'>
    <Image id='inner' sprite='pugui#pugui_9slice_mask' mask='self' showMask='false'
           anchor='stretch' margin='8'/>
  </Image>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var s = UI.Open("S");
            var outer = s.Get<PromptUGUIImage>("outer");
            var inner = s.Get<PromptUGUIImage>("inner");
            // Outer: no mask
            Assert.IsNull(outer.GameObject.GetComponent<Mask>());
            Assert.IsNull(outer.GameObject.GetComponent<RectMask2D>());
            // Inner: stencil mask, graphic hidden
            var innerMask = inner.GameObject.GetComponent<Mask>();
            Assert.IsNotNull(innerMask);
            Assert.IsFalse(innerMask.showMaskGraphic);
        }
    }
}
