using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ImageTintTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string innerXml)
        {
            UI.LoadDocument("t",
                "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" +
                $"<Screen name='S'>{innerXml}</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private static UnityImage ImageOf(PromptUGUI.Application.Screen s, string id)
            => s.Get<Image>(id).GameObject.GetComponent<UnityImage>();

        [Test]
        public void NoTint_UsesDefaultMaterial()
        {
            var img = ImageOf(Open("<Image id='i' color='#ffffff'/>"), "i");
            Assert.AreEqual(img.defaultMaterial, img.material);
        }

        [Test]
        public void TintMultiply_UsesDefaultMaterial()
        {
            var img = ImageOf(Open("<Image id='i' color='#ffffff' tint='multiply'/>"), "i");
            Assert.AreEqual(img.defaultMaterial, img.material);
        }

        [Test]
        public void TintEmptyString_UsesDefaultMaterial()
        {
            // An explicit empty attribute (tint='') is passed through as "" by the
            // attribute applier; it must behave like multiply, not throw or warn.
            var img = ImageOf(Open("<Image id='i' color='#ffffff' tint=''/>"), "i");
            Assert.AreEqual(img.defaultMaterial, img.material);
        }

        [Test]
        public void TintLinear_UsesLinearLightTintMaterial()
        {
            var img = ImageOf(Open("<Image id='i' color='#ffffff' tint='linear'/>"), "i");
            Assert.AreNotEqual(img.defaultMaterial, img.material);
            Assert.AreEqual("UI/LinearLightTint", img.material.shader.name);
        }

        [Test]
        public void TintUnknown_WarnsAndFallsBackToDefault()
        {
            LogAssert.Expect(LogType.Warning, new Regex("tint"));
            var img = ImageOf(Open("<Image id='i' color='#ffffff' tint='glow'/>"), "i");
            Assert.AreEqual(img.defaultMaterial, img.material);
        }

        [Test]
        public void TintLinearThenMultiply_ResetsToDefault()
        {
            var s = Open("<Image id='i' color='#ffffff' tint='linear'/>");
            var img = ImageOf(s, "i");
            Assert.AreEqual("UI/LinearLightTint", img.material.shader.name);

            // Re-drive the setter to simulate a value change; img is the same live component.
            s.Get<Image>("i").Tint = "multiply";
            Assert.AreEqual(img.defaultMaterial, img.material);
        }

        [Test]
        public void TwoLinearImages_ShareSameMaterialInstance()
        {
            var s = Open("<Image id='a' tint='linear'/><Image id='b' tint='linear'/>");
            var a = ImageOf(s, "a");
            var b = ImageOf(s, "b");
            Assert.AreSame(a.material, b.material);
        }

        [Test]
        public void Btn_TintLinear_AppliesToBackground()
        {
            var s = Open("<Btn id='b' color='#ffffff' text='Go' tint='linear'/>");
            var bg = s.Get<Btn>("b").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual("UI/LinearLightTint", bg.material.shader.name);
        }

        [Test]
        public void Toggle_TintLinear_AppliesToBackgroundChild()
        {
            var s = Open("<Toggle id='t' color='#ffffff' text='On' tint='linear'/>");
            var bg = s.Get<Toggle>("t").GameObject.transform.Find("Background")
                      .GetComponent<UnityImage>();
            Assert.AreEqual("UI/LinearLightTint", bg.material.shader.name);
        }

        [Test]
        public void Tab_TintLinear_AppliesToBackground()
        {
            var s = Open("<TabBar id='bar'><Tab id='t' color='#ffffff' text='Edit' tint='linear'/></TabBar>");
            var bg = s.Get<Tab>("bar/t").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual("UI/LinearLightTint", bg.material.shader.name);
        }

        [Test]
        public void Icon_TintLinear_AppliesToImage()
        {
            // No sprite resolver registered → a sprite LogError is expected; tint is independent.
            LogAssert.Expect(LogType.Error, new Regex("SpriteResolver"));
            var s = Open("<Icon id='i' name='ui:gear' color='#ffffff' tint='linear'/>");
            var img = s.Get<Icon>("i").GameObject.GetComponent<UnityImage>();
            Assert.AreEqual("UI/LinearLightTint", img.material.shader.name);
        }

        [Test]
        public void Progress_TintLinear_AppliesToFillBgAndFrame()
        {
            var s = Open("<Progress id='p' value='0.5' color='#ffffff' " +
                         "bgColor='#222222' frameColor='#888888' tint='linear'/>");
            var p = s.Get<Progress>("p").GameObject.transform;
            var fill = p.Find("MaskWrapper/Fill").GetComponent<UnityImage>();
            var bg = p.Find("MaskWrapper/Bg").GetComponent<UnityImage>();
            var frame = p.Find("Frame").GetComponent<UnityImage>();
            Assert.AreEqual("UI/LinearLightTint", fill.material.shader.name, "fill");
            Assert.AreEqual("UI/LinearLightTint", bg.material.shader.name, "bg");
            Assert.AreEqual("UI/LinearLightTint", frame.material.shader.name, "frame");
        }

        [Test]
        public void Progress_TintReachesInactiveBgLayer_AndSurvivesActivation()
        {
            // _bg is created (inactive) in OnAttached, so Tint reaches it even before
            // BgColor activates the layer; activation must not lose the material.
            var prog = Open("<Progress id='p' value='0.5'/>").Get<Progress>("p");
            prog.Tint = "linear";          // _bg exists but is inactive here
            prog.BgColor = "#222222";      // activates _bg; material must persist
            var bg = prog.GameObject.transform.Find("MaskWrapper/Bg").GetComponent<UnityImage>();
            Assert.AreEqual("UI/LinearLightTint", bg.material.shader.name);
        }

        [Test]
        public void Variant_OverridesTint_OnReSolve()
        {
            var s = Open("<Image id='i' color='#ffffff' tint='multiply' tint.dark='linear'/>");
            var img = ImageOf(s, "i");
            Assert.AreEqual(img.defaultMaterial, img.material, "multiply before variant");

            UI.VariantStore.Set("dark", true);
            s.ReSolve();
            Assert.AreEqual("UI/LinearLightTint", img.material.shader.name, "linear after variant");

            UI.VariantStore.Set("dark", false);
            s.ReSolve();
            Assert.AreEqual(img.defaultMaterial, img.material, "back to multiply");
        }
    }
}
