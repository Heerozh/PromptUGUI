using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.Controls
{
    public class ImageTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator Adds_UI_Image_component_on_attach()
        {
            var img = new Image();
            var go = new GameObject("img", typeof(RectTransform));
            img.AttachTo(go);

            Assert.IsNotNull(go.GetComponent<UnityImage>());
            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Color_property_parses_hex_and_applies()
        {
            var img = new Image();
            var go = new GameObject("img", typeof(RectTransform));
            img.AttachTo(go);

            img.Color = "#FF8800";

            var ui = go.GetComponent<UnityImage>();
            Assert.That(ui.color.r, Is.EqualTo(1f).Within(0.01f));
            Assert.That(ui.color.g, Is.EqualTo(0.533f).Within(0.01f));
            Assert.That(ui.color.b, Is.EqualTo(0f).Within(0.01f));

            Object.Destroy(go);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Type_auto_detects_Sliced_when_sprite_has_border()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='bg' anchor='stretch' sprite='PromptUGUI/Defaults/pugui.png#pugui_9slice_round'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var img = screen.Get<Image>("bg").GameObject.GetComponent<UnityImage>();

            Assert.AreEqual(UnityImage.Type.Sliced, img.type);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Type_stays_Simple_when_sprite_has_no_border()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='ic' anchor='center' size='32x32' sprite='PromptUGUI/Defaults/pugui.png#pugui_caret'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var img = screen.Get<Image>("ic").GameObject.GetComponent<UnityImage>();

            Assert.AreEqual(UnityImage.Type.Simple, img.type);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Explicit_type_simple_overrides_auto_detect_on_bordered_sprite()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Image id='bg' anchor='stretch' type='simple'
         sprite='PromptUGUI/Defaults/pugui.png#pugui_9slice_round'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var img = screen.Get<Image>("bg").GameObject.GetComponent<UnityImage>();

            Assert.AreEqual(UnityImage.Type.Simple, img.type);
            yield return null;
        }
    }
}
