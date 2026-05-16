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
    }
}
