using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;
using UnityMask = UnityEngine.UI.Mask;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ProgressTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Progress Open(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Progress>("p");
        }

        [Test]
        public void Empty_Progress_Has_MaskWrapper_Fill_Frame_Children()
        {
            var p = Open("<Progress id='p'/>");
            var maskWrapper = p.GameObject.transform.Find("MaskWrapper") as RectTransform;
            var bg = p.GameObject.transform.Find("MaskWrapper/Bg") as RectTransform;
            var fill = p.GameObject.transform.Find("MaskWrapper/Fill") as RectTransform;
            var frame = p.GameObject.transform.Find("Frame") as RectTransform;
            Assert.IsNotNull(maskWrapper, "MaskWrapper RT");
            Assert.IsNotNull(bg, "Bg RT (inside MaskWrapper)");
            Assert.IsNotNull(fill, "Fill RT (inside MaskWrapper)");
            Assert.IsNotNull(frame, "Frame RT");
            Assert.IsFalse(bg.gameObject.activeSelf, "Bg starts disabled");
            Assert.IsFalse(frame.gameObject.activeSelf, "Frame starts disabled");
            Assert.IsNull(maskWrapper.gameObject.GetComponent<UnityMask>(),
                "no mask= → no UI.Mask on MaskWrapper");
            Assert.IsNull(maskWrapper.gameObject.GetComponent<UnityImage>(),
                "no mask= → no UnityImage on MaskWrapper");
        }

        [Test]
        public void Progress_Root_Has_No_Image()
        {
            var p = Open("<Progress id='p'/>");
            Assert.IsNull(p.GameObject.GetComponent<UnityImage>(),
                "Progress root is a pure RectTransform host, no Graphic");
        }
    }
}
