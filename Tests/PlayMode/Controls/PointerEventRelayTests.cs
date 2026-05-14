using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Image = PromptUGUI.Controls.Image;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class PointerEventRelayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PointerEventData NewEventData()
        {
            var es = EventSystem.current ?? new GameObject("ES").AddComponent<EventSystem>();
            return new PointerEventData(es);
        }

        [UnityTest]
        public IEnumerator Btn_exposes_pointer_streams_and_Button_still_works()
        {
            // Spec Risk #1: Unity Button implements IPointer* for state changes.
            // PointerEventRelay also implements them. Both must coexist — Unity's
            // EventSystem dispatches the event to ALL components that implement the
            // handler interface, so both Button (state change) and Relay (stream emit) fire.
            UI.LoadDocument("t", $"{Header}<Btn id='b'>OK</Btn>{Footer}");
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var src = (IPointerEventSource)btn;
            int enterCount = 0, clickCount = 0;
            src.OnPointerEnter.Subscribe(_ => enterCount++);
            btn.OnClick.Subscribe(_ => clickCount++);

            ExecuteEvents.Execute(btn.GameObject, NewEventData(), ExecuteEvents.pointerEnterHandler);
            yield return null;

            Assert.AreEqual(1, enterCount, "Relay must receive pointer enter");

            // Click stream should still work too (sanity — Button isn't disrupted).
            btn.GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();
            Assert.AreEqual(1, clickCount);
        }

        [UnityTest]
        public IEnumerator Image_exposes_pointer_streams()
        {
            UI.LoadDocument("t", $"{Header}<Image id='img' sprite='ui/dummy'/>{Footer}");
            var screen = UI.Open("S");
            var img = screen.Get<Image>("img");
            var src = (IPointerEventSource)img;
            int downCount = 0;
            src.OnPointerDown.Subscribe(_ => downCount++);

            ExecuteEvents.Execute(img.GameObject, NewEventData(), ExecuteEvents.pointerDownHandler);
            yield return null;

            Assert.AreEqual(1, downCount);
        }
    }
}
