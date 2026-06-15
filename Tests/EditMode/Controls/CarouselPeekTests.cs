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

        [Test]
        public void Peek_EdgeScale_Shrinks_Neighbours_Focus_Full()
        {
            var car = Open("<Carousel id='car' size='400x100' fill='false' edgeScale='0.8' loop='true'>" +
                           "<Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/></Carousel>");
            Assert.AreEqual(1f, Card(car, 0).localScale.x, 0.001f, "focus card (off 0) full scale");
            Assert.AreEqual(0.8f, Card(car, 1).localScale.x, 0.001f, "neighbour (off 1) shrunk to edgeScale");
        }

        [Test]
        public void Peek_EdgeScale_Reapplied_On_GoTo_Self_Resets()
        {
            var car = Open("<Carousel id='car' size='400x100' fill='false' edgeScale='0.8' loop='true'>" +
                           "<Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/></Carousel>");
            Assert.AreEqual(0.8f, Card(car, 1).localScale.x, 0.001f, "initially a neighbour");
            car.GoTo(1, animated: false);   // card1 becomes focus
            Assert.AreEqual(1f, Card(car, 1).localScale.x, 0.001f, "scale re-written every Reposition (self-resets to 1)");
        }

        [Test]
        public void Peek_EdgeAlpha_Fades_Neighbours_Via_CanvasGroup()
        {
            var car = Open("<Carousel id='car' size='400x100' fill='false' edgeAlpha='0.4' loop='true'>" +
                           "<Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/></Carousel>");
            var cg = Card(car, 1).GetComponent<CanvasGroup>();
            Assert.IsTrue(cg != null, "neighbour gets a CanvasGroup for fading");
            Assert.AreEqual(0.4f, cg.alpha, 0.001f, "neighbour faded to edgeAlpha");
        }

        [Test]
        public void Peek_Focus_Card_Full_Alpha()
        {
            var car = Open("<Carousel id='car' size='400x100' fill='false' edgeAlpha='0.4' loop='true'>" +
                           "<Frame size='120x80'/><Frame size='120x80'/><Frame size='120x80'/></Carousel>");
            var cg = Card(car, 0).GetComponent<CanvasGroup>();
            Assert.IsTrue(cg != null, "card carries a CanvasGroup");
            Assert.AreEqual(1f, cg.alpha, 0.001f, "focus card fully opaque");
        }

        [Test]
        public void Fill_Mode_EdgeAlpha_Inert_Cards_Full_Alpha()
        {
            // fill=true, no edgeAlpha attr → _edgeAlpha=1f → Lerp(1,1,t)=1 for all t → alpha stays 1
            var car = Open("<Carousel id='car' size='200x100'><Image/><Image/></Carousel>");
            var cg0 = Card(car, 0).GetComponent<CanvasGroup>();
            var cg1 = Card(car, 1).GetComponent<CanvasGroup>();
            Assert.AreEqual(1f, cg0.alpha, 0.001f, "fill-mode focus card alpha is 1 (edgeAlpha inert)");
            Assert.AreEqual(1f, cg1.alpha, 0.001f, "fill-mode neighbour card alpha is 1 (edgeAlpha inert)");
        }
    }
}
