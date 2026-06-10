using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class CarouselBindItemsTests
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
        public void BindItems_Default_Frame_Template_Instantiates_Cards()
        {
            var car = Open("<Carousel id='car' size='200x100'/>");
            using var sub = car.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "a", "b", "c" }),
                (IControl card, string s) => { });
            Assert.AreEqual(3, car.Count);
        }

        [Test]
        public void BindItems_Clears_Static_Cards()
        {
            var car = Open("<Carousel id='car' size='200x100'><Image/><Image/></Carousel>");
            Assert.AreEqual(2, car.Count);
            using var sub = car.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "only" }),
                (IControl card, string s) => { });
            Assert.AreEqual(1, car.Count);
        }

        [Test]
        public void BindItems_Empty_List_Clears_And_Current_Is_Minus_One()
        {
            var car = Open("<Carousel id='car' size='200x100'><Image/></Carousel>");
            using var sub = car.BindItems(
                Observable.Return<IReadOnlyList<string>>(new string[0]),
                (IControl card, string s) => { });
            Assert.AreEqual(0, car.Count);
            Assert.AreEqual(-1, car.Current);
        }

        [Test]
        public void BindItems_Custom_Template_Binds_Into_Body()
        {
            const string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='Card'><Frame id='wrap'><Text id='title'/></Frame></Template>
  <Screen name='S'><Carousel id='car' size='200x100' itemTemplate='Card'/></Screen>
</PromptUGUI>";
            UI.LoadDocument("t", xml);
            var car = UI.Open("S").Get<Carousel>("car");
            using var sub = car.BindItems<string>(
                Observable.Return<IReadOnlyList<string>>(new[] { "Hello" }),
                (slot, s) => slot.Get<Text>("title").TextValue = s);
            Assert.AreEqual(1, car.Count);
        }

        [Test]
        public void ReSolve_After_BindItems_Replaced_Static_Cards_Does_Not_Throw()
        {
            // 静态 XML 卡被 BindItems 重建销毁后，其 ElementNode 仍留在 Screen._nodeMap；
            // resize / Variant / Theme 触发的 ReSolve 不得对已销毁的 RectTransform 重新 Apply。
            var xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Carousel id='car' size='200x100'><Frame id='banner0'/><Frame id='banner1'/></Carousel>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            var screen = UI.Open("S");
            var car = screen.Get<Carousel>("car");
            using var sub = car.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "a" }),
                (IControl card, string s) => { });

            Assert.DoesNotThrow(() => screen.ReSolve());
            Assert.AreEqual(1, car.Count, "dynamic cards survive the ReSolve");
        }

        [Test]
        public void BindItems_Rebuild_That_Clamps_Current_Fires_OnCurrentChanged()
        {
            var car = Open("<Carousel id='car' size='200x100'><Image/><Image/><Image/></Carousel>");
            car.GoTo(2, animated: false);
            int fired = -99;
            using var sub = car.OnCurrentChanged.Subscribe(i => fired = i);
            car.BindItems(
                Observable.Return<IReadOnlyList<string>>(new[] { "only" }),
                (IControl card, string s) => { });
            Assert.AreEqual(0, car.Current, "current clamps into the 1-item deck");
            Assert.AreEqual(0, fired, "a rebuild that changes the committed page emits OnCurrentChanged");
        }
    }
}
