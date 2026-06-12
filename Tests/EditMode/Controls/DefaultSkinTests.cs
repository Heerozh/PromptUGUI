using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class DefaultSkinTests
    {
        [SetUp]
        public void SetUp() => UI.ResetForTests();

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [TestCase(ProceduralBuilders.SpriteRoundedRect)]
        [TestCase(ProceduralBuilders.SpriteMaskRoundedRect)]
        [TestCase(ProceduralBuilders.SpriteCaret)]
        [TestCase(ProceduralBuilders.SpriteCheckmark)]
        [TestCase(ProceduralBuilders.SpriteInset)]
        [TestCase(ProceduralBuilders.SpritePressed)]
        [TestCase(ProceduralBuilders.SpriteKnob)]
        public void GetDefaultSprite_ResolvesAllSkinSections(string name)
        {
            ProceduralBuilders.ResetDefaultSpriteCacheForTests();
            Assert.IsNotNull(ProceduralBuilders.GetDefaultSprite(name), name);
        }

        private static IScreen OpenScreen(string body)
        {
            string xml = "<?xml version='1.0' encoding='utf-8'?>" +
                "<PromptUGUI version='1'><Screen name='Skin'>" + body + "</Screen></PromptUGUI>";
            UI.LoadDocument("skin", xml);
            return UI.Open("Skin");
        }

        [Test]
        public void InputField_DefaultBg_IsInset()
        {
            var screen = OpenScreen("<InputField id='f' width='200' height='40'/>");
            var bg = screen.Get<PromptUGUI.Controls.InputField>("f").GameObject
                           .GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual("pugui_9slice_inset", bg.sprite.name);
        }

        [Test]
        public void Slider_DefaultTrackInset_HandleKnob()
        {
            var screen = OpenScreen("<Slider id='s' width='200' height='40'/>");
            var root = screen.Get<PromptUGUI.Controls.Slider>("s").GameObject.transform;
            var track = root.Find("Background").GetComponent<UnityEngine.UI.Image>();
            var handle = root.Find("Handle Slide Area/Handle").GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual("pugui_9slice_inset", track.sprite.name);
            Assert.AreEqual("pugui_knob", handle.sprite.name);
            Assert.AreEqual(UnityEngine.UI.Image.Type.Simple, handle.type);
        }

        [Test]
        public void ScrollList_DefaultBg_IsInset()
        {
            var screen = OpenScreen("<ScrollList id='l' width='200' height='100'/>");
            var bg = screen.Get<PromptUGUI.Controls.ScrollList>("l").GameObject
                           .GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual("pugui_9slice_inset", bg.sprite.name);
        }

        [Test]
        public void ScrollList_DefaultScrollbarBg_IsInset_HandleStaysRound()
        {
            var screen = OpenScreen("<ScrollList id='l' width='200' height='100'/>");
            var root = screen.Get<PromptUGUI.Controls.ScrollList>("l").GameObject.transform;
            var sb = root.Find("Scrollbar Vertical");
            Assert.IsNotNull(sb, "default direction is vertical → Scrollbar Vertical exists");
            var bg = sb.GetComponent<UnityEngine.UI.Image>();
            var handle = sb.Find("Sliding Area/Handle").GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual("pugui_9slice_inset", bg.sprite.name);
            Assert.AreEqual("pugui_9slice_round", handle.sprite.name);
        }

        [Test]
        public void Dropdown_PopupScrollbarBg_IsInset_BgAndHandleStayRound()
        {
            var screen = OpenScreen("<Dropdown id='d' width='200' height='40'/>");
            var root = screen.Get<PromptUGUI.Controls.Dropdown>("d").GameObject.transform;
            var bg = root.GetComponent<UnityEngine.UI.Image>();
            var sbBg = root.Find("Template/Scrollbar").GetComponent<UnityEngine.UI.Image>();
            var sbHandle = root.Find("Template/Scrollbar/Sliding Area/Handle")
                               .GetComponent<UnityEngine.UI.Image>();
            Assert.AreEqual("pugui_9slice_round", bg.sprite.name);
            Assert.AreEqual("pugui_9slice_inset", sbBg.sprite.name);
            Assert.AreEqual("pugui_9slice_round", sbHandle.sprite.name);
        }

        [Test]
        public void ApplyDefaultInsetSprite_SetsInsetSliced()
        {
            var go = new GameObject("img", typeof(RectTransform));
            try
            {
                var img = go.AddComponent<UnityEngine.UI.Image>();
                ProceduralBuilders.ApplyDefaultInsetSprite(img);
                Assert.IsNotNull(img.sprite, "default inset sprite must resolve");
                Assert.AreEqual("pugui_9slice_inset", img.sprite.name);
                Assert.AreEqual(UnityEngine.UI.Image.Type.Sliced, img.type);
            }
            finally { Object.DestroyImmediate(go); }
        }
    }
}
