using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.PlayMode.Controls
{
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

        private static void Drag(Carousel car, float totalDx)
        {
            var view = (UnityEngine.EventSystems.IDragHandler)car.GameObject
                .GetComponent<PromptUGUI.Controls.Internal.CarouselView>();
            var begin = (UnityEngine.EventSystems.IBeginDragHandler)view;
            var end = (UnityEngine.EventSystems.IEndDragHandler)view;
            var es = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
            { delta = new UnityEngine.Vector2(totalDx, 0f) };
            begin.OnBeginDrag(es);
            view.OnDrag(es);
            end.OnEndDrag(es);
        }

        [UnityTest]
        public IEnumerator Drag_Left_Past_Threshold_Advances()
        {
            var car = Open("interval='0' transition='0.01'");   // 200px 宽视口
            Drag(car, -120f);                                   // 左拖 > 0.2*200=40
            yield return new UnityEngine.WaitForSecondsRealtime(0.1f);
            Assert.AreEqual(1, car.Current, "drag left advances one page");
        }

        [UnityTest]
        public IEnumerator Small_Drag_Snaps_Back()
        {
            var car = Open("interval='0' transition='0.01'");
            Drag(car, -10f);                                    // < 阈值
            yield return new UnityEngine.WaitForSecondsRealtime(0.1f);
            Assert.AreEqual(0, car.Current, "small drag returns to current");
        }

        [UnityTest]
        public IEnumerator Vertical_Drag_Is_Ignored()
        {
            var car = Open("interval='0' transition='0.01'");
            var view = car.GameObject.GetComponent<PromptUGUI.Controls.Internal.CarouselView>();
            var begin = (UnityEngine.EventSystems.IBeginDragHandler)view;
            var es = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
            { delta = new UnityEngine.Vector2(2f, 80f) };       // 竖向为主
            begin.OnBeginDrag(es);
            ((UnityEngine.EventSystems.IDragHandler)view).OnDrag(es);
            ((UnityEngine.EventSystems.IEndDragHandler)view).OnEndDrag(es);
            yield return null;
            Assert.AreEqual(0, car.Current, "vertical drag not consumed");
        }

        [UnityTest]
        public IEnumerator Vertical_Drag_Forwarded_To_Parent()
        {
            var car = Open("interval='0' transition='0.01'");
            // 在 carousel 的父 GameObject 上挂一个拖动录制器。
            var parentGo = car.GameObject.transform.parent.gameObject;
            var recorder = parentGo.AddComponent<DragRecorder>();

            var view = car.GameObject.GetComponent<PromptUGUI.Controls.Internal.CarouselView>();
            var begin = (UnityEngine.EventSystems.IBeginDragHandler)view;
            var drag = (UnityEngine.EventSystems.IDragHandler)view;
            var end = (UnityEngine.EventSystems.IEndDragHandler)view;

            var es = new UnityEngine.EventSystems.PointerEventData(UnityEngine.EventSystems.EventSystem.current)
            { delta = new UnityEngine.Vector2(2f, 80f) };       // 竖向为主 — 不属于轮播

            begin.OnBeginDrag(es);
            drag.OnDrag(es);
            end.OnEndDrag(es);

            yield return null;

            Assert.AreEqual(0, car.Current, "vertical drag: carousel must not advance");
            Assert.GreaterOrEqual(recorder.Begins, 1, "parent should receive OnBeginDrag");
            Assert.GreaterOrEqual(recorder.Drags, 1, "parent should receive OnDrag");
            Assert.GreaterOrEqual(recorder.Ends, 1, "parent should receive OnEndDrag");
        }
    }

    // 录制器：挂在父 GameObject 上，统计收到的拖动次数。
    internal class DragRecorder : MonoBehaviour,
        IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public int Begins;
        public int Drags;
        public int Ends;

        public void OnBeginDrag(PointerEventData e) => Begins++;
        public void OnDrag(PointerEventData e) => Drags++;
        public void OnEndDrag(PointerEventData e) => Ends++;
    }
}
