using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using Animation = PromptUGUI.Controls.Animation;
using Image = PromptUGUI.Controls.Image;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    public class PointerTriggerPlayTests
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
        public IEnumerator HoverEnter_fires_on_Btn_pointer_enter()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Trigger id='t' on='hover-enter'><Btn id='b'>OK</Btn></Trigger>" +
                $"{Footer}");
            var screen = UI.Open("S");
            int fires = 0;
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
            var btn = screen.Get<Btn>("t/b");

            ExecuteEvents.Execute(btn.GameObject, NewEventData(), ExecuteEvents.pointerEnterHandler);
            yield return null;

            Assert.AreEqual(1, fires);
        }

        [UnityTest]
        public IEnumerator HoverExit_fires_on_Btn_pointer_exit()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Trigger id='t' on='hover-exit'><Btn id='b'>OK</Btn></Trigger>" +
                $"{Footer}");
            var screen = UI.Open("S");
            int fires = 0;
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
            var btn = screen.Get<Btn>("t/b");

            ExecuteEvents.Execute(btn.GameObject, NewEventData(), ExecuteEvents.pointerExitHandler);
            yield return null;

            Assert.AreEqual(1, fires);
        }

        [UnityTest]
        public IEnumerator Press_fires_on_Btn_pointer_down()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Trigger id='t' on='press'><Btn id='b'>OK</Btn></Trigger>" +
                $"{Footer}");
            var screen = UI.Open("S");
            int fires = 0;
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
            var btn = screen.Get<Btn>("t/b");

            ExecuteEvents.Execute(btn.GameObject, NewEventData(), ExecuteEvents.pointerDownHandler);
            yield return null;

            Assert.AreEqual(1, fires);
        }

        [UnityTest]
        public IEnumerator HoverEnter_fires_on_Image_pointer_enter()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Trigger id='t' on='hover-enter'><Image id='i' sprite='ui/dummy'/></Trigger>" +
                $"{Footer}");
            var screen = UI.Open("S");
            int fires = 0;
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
            var img = screen.Get<Image>("t/i");

            ExecuteEvents.Execute(img.GameObject, NewEventData(), ExecuteEvents.pointerEnterHandler);
            yield return null;

            Assert.AreEqual(1, fires);
        }

        [UnityTest]
        public IEnumerator Press_fires_on_Image_pointer_down()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Trigger id='t' on='press'><Image id='i' sprite='ui/dummy'/></Trigger>" +
                $"{Footer}");
            var screen = UI.Open("S");
            int fires = 0;
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fires++);
            var img = screen.Get<Image>("t/i");

            ExecuteEvents.Execute(img.GameObject, NewEventData(), ExecuteEvents.pointerDownHandler);
            yield return null;

            Assert.AreEqual(1, fires);
        }

        [UnityTest]
        public IEnumerator Press_triggers_Animation_scale_change()
        {
            UI.LoadDocument("t", $"{Header}" +
                "<Animation id='a' scale='1:0.5' duration='0.1s' on='press'>" +
                "  <Btn id='b'>OK</Btn>" +
                "</Animation>" +
                $"{Footer}");
            var screen = UI.Open("S");
            var anim = screen.Get<Animation>("a");
            var btn = screen.Get<Btn>("a/b");

            ExecuteEvents.Execute(btn.GameObject, NewEventData(), ExecuteEvents.pointerDownHandler);
            yield return new WaitForSeconds(0.2f);

            var proxy = (RectTransform)anim.GameObject.transform.Find("_offsetProxy");
            Assert.AreEqual(0.5f, proxy.localScale.x, 0.01f);
        }
    }
}
