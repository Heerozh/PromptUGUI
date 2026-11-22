using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.EventSystems;
using static PromptUGUI.Tests.EditMode.Controls.SfxTestKit;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A disabled Carousel — <c>interactable="false"</c> on it or on a panel around it — does not turn on a swipe, the way
    /// its dots already did not; the gesture still goes to the scroller around it (spec 2026-11-23-input-correctness §4).
    /// </summary>
    public class CarouselDisabledTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private sealed class DragCounter : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public int Begins, Drags, Ends;
            public void OnBeginDrag(PointerEventData e) => Begins++;
            public void OnDrag(PointerEventData e) => Drags++;
            public void OnEndDrag(PointerEventData e) => Ends++;
        }

        private static Carousel Open(bool disableAncestor, bool disableSelf)
        {
            var s = OpenScreen(
                $"<Frame id='wrap' anchor='stretch' interactable='{(disableAncestor ? "false" : "true")}'>" +
                $"<Carousel id='car' size='200x100' interval='0' interactable='{(disableSelf ? "false" : "true")}'>" +
                "<Image/><Image/><Image/></Carousel></Frame>");
            return s.Get<Carousel>("car");
        }

        private static void Swipe(CarouselView view, float dx)
        {
            var viewport = (RectTransform)view.StripRect.parent;
            var p0 = RectTransformUtility.WorldToScreenPoint(null, viewport.TransformPoint(Vector2.zero));
            var p1 = RectTransformUtility.WorldToScreenPoint(null, viewport.TransformPoint(new Vector2(dx, 0f)));
            ((IBeginDragHandler)view).OnBeginDrag(new PointerEventData(EventSystem.current) { position = p0 });
            ((IDragHandler)view).OnDrag(new PointerEventData(EventSystem.current) { position = p1, delta = p1 - p0 });
            ((IEndDragHandler)view).OnEndDrag(new PointerEventData(EventSystem.current) { position = p1, delta = p1 - p0 });
        }

        [Test]
        public void A_disabled_carousel_does_not_turn_on_a_swipe([Values] bool viaAncestor)
        {
            var cues = Record();
            var car = Open(disableAncestor: viaAncestor, disableSelf: !viaAncestor);
            var view = car.GameObject.GetComponent<CarouselView>();

            Swipe(view, -150f);   // well past the snap threshold

            Assert.AreEqual(0, car.Current, "no page turned");
            Assert.IsEmpty(cues, Describe(cues));
        }

        [Test]
        public void The_swipe_on_a_disabled_carousel_still_reaches_the_scroller_around_it()
        {
            var car = Open(disableAncestor: false, disableSelf: true);
            var counter = car.GameObject.transform.parent.gameObject.AddComponent<DragCounter>();

            Swipe(car.GameObject.GetComponent<CarouselView>(), -150f);

            Assert.AreEqual((1, 1, 1), (counter.Begins, counter.Drags, counter.Ends));
        }

        [Test]
        public void An_enabled_carousel_still_turns()
        {
            var car = Open(disableAncestor: false, disableSelf: false);
            Swipe(car.GameObject.GetComponent<CarouselView>(), -150f);
            Assert.AreEqual(1, car.Current);
        }
    }
}
