using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityRawImage = UnityEngine.UI.RawImage;
using PromptUGUIRawImage = PromptUGUI.Controls.RawImage;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class RawImageTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUIRawImage Build(string attrs = "")
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'><RawImage id='r' {attrs}/></Screen></PromptUGUI>");
            return UI.Open("S").Get<PromptUGUIRawImage>("r");
        }

        [Test]
        public void Instantiates_With_UnityRawImage_Component()
        {
            var r = Build();
            Assert.IsNotNull(r.GameObject.GetComponent<UnityRawImage>());
        }

        [Test]
        public void Texture_Get_Set_Roundtrips()
        {
            var r = Build();
            var tex = new Texture2D(8, 8);
            r.Texture = tex;
            Assert.AreSame(tex, r.Texture);
            Assert.AreSame(tex, r.GameObject.GetComponent<UnityRawImage>().texture);
        }

        [Test]
        public void Color_Applies_To_RawImage()
        {
            var r = Build("color='#ff0000'");
            Assert.AreEqual(Color.red, r.GameObject.GetComponent<UnityRawImage>().color);
        }

        [Test]
        public void TypeContain_AddsFitter_FitInParent()
        {
            var r = Build("type='contain'");
            var arf = r.GameObject.GetComponent<AspectRatioFitter>();
            Assert.IsNotNull(arf);
            Assert.IsTrue(arf.enabled);
            Assert.AreEqual(AspectRatioFitter.AspectMode.FitInParent, arf.aspectMode);
        }

        [Test]
        public void TypeCover_AddsFitter_EnvelopeParent()
        {
            var r = Build("type='cover'");
            var arf = r.GameObject.GetComponent<AspectRatioFitter>();
            Assert.IsNotNull(arf);
            Assert.IsTrue(arf.enabled);
            Assert.AreEqual(AspectRatioFitter.AspectMode.EnvelopeParent, arf.aspectMode);
        }

        [Test]
        public void Texture_With_FitMode_ComputesAspectRatio()
        {
            var r = Build("type='cover'");
            var arf = r.GameObject.GetComponent<AspectRatioFitter>();
            r.Texture = new Texture2D(4, 2);
            Assert.AreEqual(2f, arf.aspectRatio, 0.001f);
            r.Texture = new Texture2D(2, 4);
            Assert.AreEqual(0.5f, arf.aspectRatio, 0.001f);
        }

        [Test]
        public void NoType_NoFitter()
        {
            Assert.IsNull(Build().GameObject.GetComponent<AspectRatioFitter>());
        }

        [Test]
        public void TypeSimple_Warns_NoFitter()
        {
            LogAssert.Expect(LogType.Warning, new Regex("type"));
            var r = Build("type='simple'");
            Assert.IsNull(r.GameObject.GetComponent<AspectRatioFitter>());
        }
    }
}
