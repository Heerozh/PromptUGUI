using NUnit.Framework;
using PromptUGUI.Application;
using UnityEngine;
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
    }
}
