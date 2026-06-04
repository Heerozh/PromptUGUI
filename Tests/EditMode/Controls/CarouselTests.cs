using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class CarouselTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Carousel Open(string innerXml)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>{innerXml}</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Carousel>("car");
        }

        [Test]
        public void Empty_Carousel_Builds_Viewport_And_Strip()
        {
            var car = Open("<Carousel id='car' size='200x100'/>");
            var root = car.GameObject.transform;
            Assert.IsNotNull(root.Find("Viewport"), "Viewport child exists");
            Assert.IsNotNull(root.Find("Viewport/Strip"), "Strip under Viewport exists");
            Assert.IsTrue(root.GetComponent<PromptUGUI.Controls.Internal.CarouselView>() != null,
                "CarouselView component on root");
            Assert.AreEqual(0, car.Count);
        }

        [Test]
        public void Viewport_Has_RectMask2D_And_Raycast_Catcher()
        {
            var car = Open("<Carousel id='car' size='200x100'/>");
            var viewport = car.GameObject.transform.Find("Viewport");
            Assert.IsTrue(viewport.GetComponent<RectMask2D>() != null, "RectMask2D clips cards");
            var img = viewport.GetComponent<UnityImage>();
            Assert.IsTrue(img != null && img.raycastTarget, "transparent raycast catcher for drag");
        }
    }
}
