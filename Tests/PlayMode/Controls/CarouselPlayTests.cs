using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    // Autoplay 依赖真实游戏循环（Update + 真实时间），只能在 PlayMode 验证。
    // 拖动行为是纯几何（屏幕→本地映射 + anchoredPosition）且 EditMode 下 GoTo 走 instant 分支，
    // 已整体迁到可靠的 EditMode 套件 CarouselDragTests，这里不再重复。
    public class CarouselPlayTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static Carousel Open(string attrs)
        {
            var xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Carousel id='car' size='200x100' {attrs}><Image/><Image/><Image/></Carousel>
</Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<Carousel>("car");
        }

        [UnityTest]
        public IEnumerator Autoplay_Advances_After_Interval()
        {
            var car = Open("interval='0.2' transition='0.01'");
            Assert.AreEqual(0, car.Current);
            yield return new UnityEngine.WaitForSecondsRealtime(0.5f);
            Assert.Greater(car.Current, 0, "autoplay advanced past 0");
        }

        [UnityTest]
        public IEnumerator Playing_False_Stops_Autoplay()
        {
            var car = Open("interval='0.2' transition='0.01'");
            car.Playing = false;
            yield return new UnityEngine.WaitForSecondsRealtime(0.5f);
            Assert.AreEqual(0, car.Current, "paused: no advance");
        }

        [UnityTest]
        public IEnumerator Interval_Zero_Disables_Autoplay()
        {
            var car = Open("interval='0'");
            yield return new UnityEngine.WaitForSecondsRealtime(0.4f);
            Assert.AreEqual(0, car.Current, "interval=0: no advance");
        }
    }
}
