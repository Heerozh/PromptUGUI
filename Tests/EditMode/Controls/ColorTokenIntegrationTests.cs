using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Parser;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;
using UnityToggleUI = UnityEngine.UI.Toggle;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ColorTokenIntegrationTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void SeedLight(string primaryHex)
        {
            var d = new System.Collections.Generic.Dictionary<string, Color>();
            ColorUtility.TryParseHtmlString(primaryHex, out var c);
            d["primary"] = c;
            ThemeStore.Instance.Register("light", null, d, "test");
            ThemeStore.Instance.ResolveBases();
            UI.Theme.Set("light");
        }

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                $"<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'>{innerXml}</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        [Test]
        public void Image_Hex_Literal_Still_Works()
        {
            var s = Open("<Image id='x' color='#00ff00'/>");
            var img = s.Get<Image>("x").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0, 0xff, 0, 0xff), (Color32)img.color);
        }

        [Test]
        public void Image_Token_Resolves()
        {
            SeedLight("#ff8800");
            var s = Open("<Image id='x' color='primary'/>");
            var img = s.Get<Image>("x").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)img.color);
        }

        [Test]
        public void Image_Unknown_Token_Throws_With_Node_Context()
        {
            SeedLight("#ff8800");
            var ex = Assert.Throws<ParseException>(() => Open("<Image id='avatar' color='primaru'/>"));
            StringAssert.Contains("Image", ex.Message);
            StringAssert.Contains("avatar", ex.Message);
            StringAssert.Contains("primaru", ex.Message);
        }

        [Test]
        public void Image_Token_With_Alpha_Suffix_End_To_End()
        {
            SeedLight("#ff8800");
            var s = Open("<Image id='x' color='primary/0.5'/>");
            var img = s.Get<Image>("x").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(1f, img.color.r, 0.005f);          // token RGB preserved
            Assert.AreEqual(0x88 / 255f, img.color.g, 0.005f);
            Assert.AreEqual(0.5f, img.color.a, 0.001f);        // alpha from suffix
        }

        [Test]
        public void Text_Token_Resolves()
        {
            SeedLight("#222222");
            var s = Open("<Text id='t' color='primary' text='Hi'/>");
            var tmp = s.Get<Text>("t").GameObject.GetComponent<TMPro.TMP_Text>();
            Assert.AreEqual(new Color32(0x22, 0x22, 0x22, 0xff), (Color32)tmp.color);
        }

        [Test]
        public void Btn_Token_Resolves()
        {
            SeedLight("#ff8800");
            var s = Open("<Btn id='b' color='primary' label='Buy'/>");
            var bg = s.Get<Btn>("b").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)bg.color);
        }

        [Test]
        public void Variant_Bad_Color_Throws_On_Resolve()
        {
            SeedLight("#ff8800");
            var s = Open("<Image id='x' color='primary' color.dark='#bagval'/>");
            var img = s.Get<Image>("x").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xff, 0x88, 0, 0xff), (Color32)img.color);
            // Set the variant and call ReSolve directly so the exception propagates.
            // LogAssert.Expect registered first to handle the R3-subscriber exception log
            // that fires when UI.VariantStore.Set triggers Changed → ReSolve → throws.
            LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("ParseException"));
            UI.VariantStore.Set("dark", true);
            Assert.Throws<ParseException>(() => s.ReSolve());
        }

        [Test]
        public void Icon_Color_Token_Resolves()
        {
            SeedLight("#aabbcc");
            // name requires "set:icon" format; no resolver registered so a LogError fires for the sprite,
            // but color is independent and must still resolve.
            LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("SpriteResolver"));
            var s = Open("<Icon id='i' name='ui:gear' color='primary'/>");
            var img = s.Get<Icon>("i").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xaa, 0xbb, 0xcc, 0xff), (Color32)img.color);
        }

        [Test]
        public void Toggle_Color_Token_Resolves()
        {
            SeedLight("#ff4400");
            var s = Open("<Toggle id='t' color='primary' text='On'/>");
            // Toggle.Color routes to the Background UnityImage child.
            var bgRt = s.Get<Toggle>("t").GameObject.transform.Find("Background");
            Assert.IsNotNull(bgRt, "Background child");
            var bg = bgRt.GetComponent<UnityImage>();
            Assert.AreEqual(new Color32(0xff, 0x44, 0, 0xff), (Color32)bg.color);
        }
    }
}
