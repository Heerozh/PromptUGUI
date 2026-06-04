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
        private readonly System.Collections.Generic.List<Object> _temp = new();

        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown]
        public void TearDown()
        {
            StateTintReactor.TestForceInstant = false;
            UI.ResetForTests();
            foreach (var o in _temp) if (o != null) Object.DestroyImmediate(o);
            _temp.Clear();
        }

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

        // A 30x10 bar so each horizontal third is exactly 10px wide — makes the slice math assertable.
        private Sprite MakeBarSprite()
        {
            var tex = new Texture2D(30, 10);
            var sp = Sprite.Create(tex, new Rect(0, 0, 30, 10), new Vector2(0.5f, 0.5f), 100f);
            _temp.Add(tex); _temp.Add(sp);
            return sp;
        }

        [Test]
        public void TriSlice_Splits_Sprite_Into_Left_Mid_Right()
        {
            var src = MakeBarSprite();
            UI.SpriteResolver = key => key == "test:bar" ? src : null;
            var car = Open("dots='bottom-center' dotSprite='test:bar' dotTriSlice='true'");

            var s0 = Indicator(car).GetChild(0).GetComponent<UnityImage>().sprite;
            var s1 = Indicator(car).GetChild(1).GetComponent<UnityImage>().sprite;
            var s2 = Indicator(car).GetChild(2).GetComponent<UnityImage>().sprite;

            Assert.AreNotSame(src, s0, "tri-slice assigns a sub-sprite, not the full source");
            Assert.AreEqual(0f, s0.rect.x, 0.5f, "left segment starts at x=0");
            Assert.AreEqual(10f, s1.rect.x, 0.5f, "mid segment starts at x=10");
            Assert.AreEqual(20f, s2.rect.x, 0.5f, "right segment starts at x=20");
            Assert.AreEqual(10f, s0.rect.width, 0.5f, "each segment is 1/3 of the sprite width");
        }

        [Test]
        public void Without_TriSlice_Dots_Use_Full_Sprite()
        {
            var src = MakeBarSprite();
            UI.SpriteResolver = key => key == "test:bar" ? src : null;
            var car = Open("dots='bottom-center' dotSprite='test:bar'");

            var s0 = Indicator(car).GetChild(0).GetComponent<UnityImage>().sprite;
            Assert.AreSame(src, s0, "no dotTriSlice => the full source sprite on every dot");
        }

        [Test]
        public void TriSlice_Middle_Dots_Share_The_Mid_Segment()
        {
            var src = MakeBarSprite();
            UI.SpriteResolver = key => key == "test:bar" ? src : null;
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Carousel id='car' size='200x100' dots='bottom-center' dotSprite='test:bar' dotTriSlice='true'>
    <Image/><Image/><Image/><Image/><Image/>
  </Carousel>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var ind = Indicator(UI.Open("S").Get<Carousel>("car"));
            Assert.AreEqual(5, ind.childCount);

            var s0 = ind.GetChild(0).GetComponent<UnityImage>().sprite;
            var s1 = ind.GetChild(1).GetComponent<UnityImage>().sprite;
            var s3 = ind.GetChild(3).GetComponent<UnityImage>().sprite;
            var s4 = ind.GetChild(4).GetComponent<UnityImage>().sprite;

            Assert.AreSame(s1, s3, "all middle dots share the one mid sub-sprite");
            Assert.AreNotSame(s0, s1, "left cap differs from mid");
            Assert.AreNotSame(s4, s1, "right cap differs from mid");
            Assert.AreEqual(0f, s0.rect.x, 0.5f, "dot 0 = left cap");
            Assert.AreEqual(20f, s4.rect.x, 0.5f, "last dot = right cap");
        }

        [Test]
        public void TriSlice_Sub_Sprites_Reused_Across_ReSolve()
        {
            var src = MakeBarSprite();
            UI.SpriteResolver = key => key == "test:bar" ? src : null;
            var car = Open("dots='bottom-center' dotSprite='test:bar' dotTriSlice='true'");
            var before = Indicator(car).GetChild(0).GetComponent<UnityImage>().sprite;

            UI.Variants.Set("x", true);   // forces Screen.ReSolve -> ConfigureDots + RebuildIndicator

            var after = Indicator(car).GetChild(0).GetComponent<UnityImage>().sprite;
            Assert.AreSame(before, after,
                "same source sprite => slices are reused across ReSolve (no destroy/recreate churn or leak)");
        }
    }
}
