using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class CarouselDotsTests
    {
        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { StateTintReactor.TestForceInstant = false; UI.ResetForTests(); }

        private static Carousel Open(string attrs)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Carousel id='car' size='200x100' {attrs}><Image/><Image/><Image/></Carousel>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Carousel>("car");
        }

        private static Transform Indicator(Carousel car) => car.GameObject.transform.Find("Indicator");

        [Test]
        public void Dots_Count_Matches_Cards()
        {
            var car = Open("dots='bottom-center' dotColor='#444' dotSelectedColor='#0FF'");
            Assert.AreEqual(3, Indicator(car).childCount);
        }

        [Test]
        public void No_Dots_When_Dots_Unset()
        {
            var car = Open("");
            Assert.AreEqual(0, Indicator(car).childCount, "no dots= => no indicator");
        }

        [Test]
        public void Indicator_Hidden_When_One_Card()
        {
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Carousel id='car' size='200x100' dots='bottom-center' dotColor='#444' dotSelectedColor='#0FF'><Image/></Carousel>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var car = UI.Open("S").Get<Carousel>("car");
            Assert.IsFalse(Indicator(car).gameObject.activeSelf, "<=1 card hides indicator");
        }

        [Test]
        public void Current_Dot_Shows_Selected_Color()
        {
            // 用 bit-exact 通道值（0 / 1）避免 #404040=0.2509… 与 0.25f 的浮点不等。
            var car = Open("dots='bottom-center' dotColor='#0000FF' dotSelectedColor='#00FFFF'");
            car.GoTo(1, animated: false);
            var dot0 = Indicator(car).GetChild(0).GetComponent<UnityImage>();
            var dot1 = Indicator(car).GetChild(1).GetComponent<UnityImage>();
            Assert.AreEqual(new Color(0f, 1f, 1f, 1f), dot1.color, "current dot = dotSelectedColor");
            Assert.AreEqual(new Color(0f, 0f, 1f, 1f), dot0.color, "others = dotColor");
        }

        [Test]
        public void Click_Dot_Jumps_To_That_Card()
        {
            var car = Open("dots='bottom-center' dotColor='#444' dotSelectedColor='#0FF'");
            Indicator(car).GetChild(2).GetComponent<Button>().onClick.Invoke();
            Assert.AreEqual(2, car.Current);
        }
    }
}
