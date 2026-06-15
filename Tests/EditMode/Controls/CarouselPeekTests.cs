using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class CarouselPeekTests
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

        private static RectTransform Card(Carousel car, int i)
            => (RectTransform)car.GameObject.transform.Find("Viewport/Strip").GetChild(i);

        [Test]
        public void Peek_Honors_Card_Size_Not_Viewport()
        {
            var car = Open("<Carousel id='car' size='200x100' fill='false'>" +
                           "<Frame size='120x80'/><Frame size='120x80'/></Carousel>");
            Assert.AreEqual(120f, Card(car, 0).rect.width, 0.5f, "peek card keeps its own width (not viewport 200)");
            Assert.AreEqual(80f, Card(car, 0).rect.height, 0.5f);
        }

        [Test]
        public void Peek_Stride_Is_CardWidth_Plus_Spacing()
        {
            var car = Open("<Carousel id='car' size='200x100' fill='false' spacing='10'>" +
                           "<Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/></Carousel>");
            Assert.AreEqual(0f, Card(car, 0).anchoredPosition.x, 0.5f, "focus card centered at x=0");
            Assert.AreEqual(130f, Card(car, 1).anchoredPosition.x, 0.5f, "neighbour at cardWidth+spacing = 130");
        }

        [Test]
        public void Fill_Mode_Default_Unchanged()
        {
            var car = Open("<Carousel id='car' size='200x100'><Image/><Image/><Image/></Carousel>");
            Assert.AreEqual(200f, Card(car, 0).rect.width, 0.5f, "fill=true (default) still sizes cards to viewport");
            Assert.AreEqual(0f, Card(car, 0).anchoredPosition.x, 0.5f, "focus card centered");
            Assert.AreEqual(200f, Card(car, 1).anchoredPosition.x, 0.5f,
                "fill-mode neighbour at one full viewport (stride==viewport, regression lock)");
        }
    }
}
