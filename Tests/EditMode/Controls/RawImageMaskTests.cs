using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.UI;
using PromptUGUIRawImage = PromptUGUI.Controls.RawImage;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class RawImageMaskTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUIRawImage Build(string attrs)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><RawImage id='r' {attrs}/></Screen></PromptUGUI>");
            return UI.Open("S").Get<PromptUGUIRawImage>("r");
        }

        [Test]
        public void NoMaskAttr_NoMaskComponents()
        {
            var r = Build("");
            Assert.IsNull(r.GameObject.GetComponent<RectMask2D>());
            Assert.IsNull(r.GameObject.GetComponent<Mask>());
        }

        [Test]
        public void MaskRect_AddsRectMask2D()
        {
            var r = Build("mask='rect'");
            Assert.IsNotNull(r.GameObject.GetComponent<RectMask2D>());
            Assert.IsNull(r.GameObject.GetComponent<Mask>());
        }

        [Test]
        public void MaskRectWithPadding_AppliesPadding()
        {
            var rm = Build("mask='rect' maskPadding='1,2,3,4'").GameObject.GetComponent<RectMask2D>();
            Assert.AreEqual(new Vector4(4f, 3f, 2f, 1f), rm.padding);
        }

        [Test]
        public void MaskSelf_AddsStencilMask()
        {
            var r = Build("mask='self'");
            Assert.IsNotNull(r.GameObject.GetComponent<Mask>());
            Assert.IsNull(r.GameObject.GetComponent<RectMask2D>());
        }

        [Test]
        public void MaskSelf_ShowMaskFalse_HidesGraphic()
        {
            var m = Build("mask='self' showMask='false'").GameObject.GetComponent<Mask>();
            Assert.IsFalse(m.showMaskGraphic);
        }
    }
}
