using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
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

        [Test]
        public void Static_Children_Become_Cards_Under_Strip()
        {
            var car = Open("<Carousel id='car' size='200x100'><Image/><Image/><Image/></Carousel>");
            Assert.AreEqual(3, car.Count);
            var strip = car.GameObject.transform.Find("Viewport/Strip");
            Assert.AreEqual(3, strip.childCount, "3 cards parented under Strip");
        }

        [Test]
        public void Cards_Sized_To_Viewport()
        {
            var car = Open("<Carousel id='car' size='200x100'><Image/><Image/></Carousel>");
            var strip = car.GameObject.transform.Find("Viewport/Strip");
            var card0 = (RectTransform)strip.GetChild(0);
            Assert.AreEqual(200f, card0.rect.width, 0.5f, "card width == viewport width");
            Assert.AreEqual(100f, card0.rect.height, 0.5f, "card height == viewport height");
        }

        [Test]
        public void GoTo_Updates_Current_And_Fires_Event()
        {
            var car = Open("<Carousel id='car' size='200x100'><Image/><Image/><Image/></Carousel>");
            int fired = -99;
            using var sub = car.OnCurrentChanged.Subscribe(i => fired = i);
            car.GoTo(2, animated: false);
            Assert.AreEqual(2, car.Current);
            Assert.AreEqual(2, fired);
        }

        [Test]
        public void Next_Loops_Past_Last_To_First()
        {
            var car = Open("<Carousel id='car' size='200x100' loop='true'><Image/><Image/></Carousel>");
            car.GoTo(1, animated: false);
            car.Next(animated: false);
            Assert.AreEqual(0, car.Current, "loop wraps last -> first");
        }

        [Test]
        public void Previous_Loops_From_First_To_Last()
        {
            var car = Open("<Carousel id='car' size='200x100' loop='true'><Image/><Image/><Image/></Carousel>");
            car.GoTo(0, animated: false);
            car.Previous(animated: false);
            Assert.AreEqual(2, car.Current, "loop wraps first -> last");
        }

        [Test]
        public void Clamp_Mode_Stops_At_Ends()
        {
            var car = Open("<Carousel id='car' size='200x100' loop='false'><Image/><Image/></Carousel>");
            car.GoTo(1, animated: false);
            car.Next(animated: false);
            Assert.AreEqual(1, car.Current, "clamp: stays at last");
            car.GoTo(0, animated: false);
            car.Previous(animated: false);
            Assert.AreEqual(0, car.Current, "clamp: stays at first");
        }

        [Test]
        public void Same_Index_Does_Not_Refire_Event()
        {
            var car = Open("<Carousel id='car' size='200x100'><Image/><Image/></Carousel>");
            int count = 0;
            using var sub = car.OnCurrentChanged.Subscribe(_ => count++);
            car.GoTo(1, animated: false);
            car.GoTo(1, animated: false);
            Assert.AreEqual(1, count, "repeated same index fires once");
        }
    }
}
