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
    }
}
