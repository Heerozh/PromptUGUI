using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class BtnTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [Test]
        public void Visual_BgColorIsWhiteByDefault()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b'>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var img = btn.GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(Color.white, img.color);
        }

        [Test]
        public void Default_Bg_Has_Sprite()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b'>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var img = UI.Open("S").Get<Btn>("b").GameObject.GetComponent<UnityImage>();
            Assert.IsNotNull(img.sprite, "Btn ships a built-in 9-slice bg sprite by default");
        }

        [Test]
        public void Sprite_None_Clears_Default_Bg()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' sprite='none'>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var img = UI.Open("S").Get<Btn>("b").GameObject.GetComponent<UnityImage>();
            Assert.IsNull(img.sprite, "sprite='none' drops the built-in bg sprite");
        }

        [Test]
        public void Sprite_Empty_Clears_Default_Bg()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' sprite=''>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var img = UI.Open("S").Get<Btn>("b").GameObject.GetComponent<UnityImage>();
            Assert.IsNull(img.sprite, "sprite='' drops the built-in bg sprite, same effect as 'none'");
        }

        [Test]
        public void Visual_LabelFontSizeIsTwentyFour()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b'>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>();
            Assert.IsNotNull(label, "Btn auto-label should exist");
            Assert.AreEqual(24f, label.fontSize);
        }

        [Test]
        public void Visual_LabelColorIsDarkGrey()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b'>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>();
            Assert.AreEqual(ProceduralBuilders.DefaultLabelColor, label.color);
        }

        [Test]
        public void Attr_FontSizeAppliesToAutoLabel()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' fontSize='32'>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var label = btn.GameObject.GetComponentInChildren<TMP_Text>();
            Assert.AreEqual(32f, label.fontSize);
        }
    }
}
