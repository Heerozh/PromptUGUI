using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Animation = PromptUGUI.Controls.Animation;  // disambiguates UnityEngine.Animation
using Screen = PromptUGUI.Application.Screen;      // disambiguates UnityEngine.Screen
using Text = PromptUGUI.Controls.Text;             // disambiguates UnityEngine.UI.Text

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// <c>on="close"</c> / <c>reverse-on="close"</c> end to end (spec 2026-09-16-close-transition-design
    /// §4 / §5.1): the Screen waits for exactly the animations the close event fired, the ghost is
    /// sealed against input, and the exit runs on unscaled time.
    ///
    /// <para>Fades are linear so a CanvasGroup alpha reads directly as progress.</para>
    /// </summary>
    public class CloseTriggerPlayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        private float _savedTimeScale;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            _savedTimeScale = Time.timeScale;
        }

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _savedTimeScale;
            UI.ResetForTests();
            foreach (var es in Object.FindObjectsByType<EventSystem>(FindObjectsSortMode.None))
                Object.Destroy(es.gameObject);
        }

        private static Screen Open(string body)
        {
            UI.LoadDocument("t", Header + body + Footer);
            return UI.Open("S");
        }

        private static CanvasGroup AlphaOf(Screen s, string id = "a")
            => s.Get<Animation>(id).GameObject.GetComponent<CanvasGroup>();

        [UnityTest]
        public IEnumerator Reverse_on_close_plays_the_entrance_backwards_and_the_screen_waits()
        {
            var screen = Open(
                "<Animation id='a' on='open' reverse-on='close' fade='0:1' duration='0.3s' easing='linear'>" +
                "<Frame id='f'/></Animation>");
            var cg = AlphaOf(screen);
            var root = screen.RootGameObject;
            Assert.AreEqual(0f, cg.alpha, 0.01f,
                "a reversible entrance rests at from — otherwise a reverse-on fade would tween 1 → 1 at open");
            yield return new WaitForSeconds(0.45f);
            Assert.AreEqual(1f, cg.alpha, 0.01f, "entrance finished");

            UI.Close("S");
            Assert.IsTrue(screen.IsClosing);
            Assert.IsNotNull(screen.RootGameObject, "the ghost lives while the exit plays");
            Assert.AreEqual(1f, cg.alpha, 0.01f, "the reverse starts from where it is");

            yield return new WaitForSeconds(0.15f);
            Assert.That(cg.alpha, Is.GreaterThan(0.1f).And.LessThan(0.9f), "half way back");

            yield return new WaitForSeconds(0.25f);
            yield return null;
            yield return null;
            Assert.IsTrue(root == null, "destroyed once the reverse landed on from");
            Assert.IsFalse(screen.IsClosing);
        }

        [UnityTest]
        public IEnumerator On_close_plays_forward_and_the_screen_waits()
        {
            var screen = Open(
                "<Animation id='a' on='close' fade='1:0' duration='0.3s' easing='linear'><Frame id='f'/></Animation>");
            var cg = AlphaOf(screen);
            yield return null;
            Assert.AreEqual(1f, cg.alpha, 0.01f, "nothing played at open");

            UI.Close("S");
            Assert.IsTrue(screen.IsClosing);
            yield return new WaitForSeconds(0.15f);
            Assert.That(cg.alpha, Is.GreaterThan(0.1f).And.LessThan(0.9f));
            yield return new WaitForSeconds(0.25f);
            yield return null;
            yield return null;
            Assert.IsNull(screen.RootGameObject);
        }

        [UnityTest]
        public IEnumerator A_plain_trigger_on_close_fires_but_does_not_delay_the_close()
        {
            var screen = Open("<Trigger id='t' on='close'><Frame id='f'/></Trigger>");
            var fired = 0;
            screen.Get<Trigger>("t").OnFire.Subscribe(_ => fired++);
            UI.Close("S");
            Assert.AreEqual(1, fired, "fired before Close returned");
            Assert.IsNull(screen.RootGameObject, "nothing to wait for → destroyed on this frame");
            yield return null;
            Assert.AreEqual(1, fired);
        }

        [UnityTest]
        public IEnumerator Closing_mid_entrance_turns_around_from_the_current_value()
        {
            var screen = Open(
                "<Animation id='a' on='open' reverse-on='close' fade='0:1' duration='1s' easing='linear'>" +
                "<Frame id='f'/></Animation>");
            var cg = AlphaOf(screen);
            yield return new WaitForSeconds(0.3f);
            var mid = cg.alpha;
            Assume.That(mid, Is.GreaterThan(0.15f).And.LessThan(0.6f), "guard: caught it mid-fade");

            UI.Close("S");
            Assert.AreEqual(mid, cg.alpha, 0.001f, "no snap on the closing frame");
            yield return null;
            yield return null;
            Assert.That(cg.alpha, Is.LessThan(mid).And.GreaterThan(0f), "heading back down from where it was");
        }

        [UnityTest]
        public IEnumerator The_ghost_does_not_take_raycasts()
        {
            var es = new GameObject("EventSystem", typeof(EventSystem));
            var screen = Open(
                "<Animation id='a' on='open' reverse-on='close' fade='0:1' duration='0.5s'>" +
                "<Btn id='b' anchor='center' size='200x100'>Hit me</Btn></Animation>");
            yield return null;
            var raycaster = screen.RootGameObject.GetComponent<GraphicRaycaster>();
            var btn = screen.Get<Btn>("b").RectTransform;
            var canvas = screen.RootGameObject.GetComponent<Canvas>();
            var centre = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, btn.TransformPoint(btn.rect.center));
            var hits = new List<RaycastResult>();

            raycaster.Raycast(new PointerEventData(es.GetComponent<EventSystem>()) { position = centre }, hits);
            Assume.That(hits.Count, Is.GreaterThan(0), "guard: the button is hit while the Screen is open");

            UI.Close("S");
            Assert.IsTrue(screen.IsClosing);
            hits.Clear();
            raycaster.Raycast(new PointerEventData(es.GetComponent<EventSystem>()) { position = centre }, hits);
            Assert.AreEqual(0, hits.Count, "sealed at Begin: nothing in the ghost is raycastable");
        }

        [UnityTest]
        public IEnumerator Selection_inside_the_ghost_is_cleared_at_begin()
        {
            var esGo = new GameObject("EventSystem", typeof(EventSystem));
            var es = esGo.GetComponent<EventSystem>();
            var screen = Open(
                "<Animation id='a' on='open' reverse-on='close' fade='0:1' duration='0.5s'>" +
                "<Btn id='b' anchor='center' size='200x100'>Hit me</Btn></Animation>");
            yield return null;
            var btnGo = screen.Get<Btn>("b").GameObject;
            es.SetSelectedGameObject(btnGo);
            Assume.That(es.currentSelectedGameObject, Is.SameAs(btnGo));

            UI.Close("S");
            Assert.IsNull(es.currentSelectedGameObject, "keyboard / gamepad input must not land on the ghost");
        }

        [UnityTest]
        public IEnumerator An_expanded_tab_menu_inside_the_ghost_is_collapsed_at_begin()
        {
            var screen = Open(
                "<Animation id='a' on='open' reverse-on='close' fade='0:1' duration='0.5s'>" +
                "<TabMenu id='m' transition='0.05s'><Tab id='x' text='X'/><Tab id='y' text='Y'/></TabMenu>" +
                "</Animation>");
            yield return null;
            var menu = screen.Get<TabMenu>("a/m");
            menu.Expand();
            Assume.That(TabMenu.HasExpandedMenu, "guard: the menu owns the process-wide slot");

            UI.Close("S");
            Assert.IsFalse(menu.IsExpanded, "a ghost's menu must not keep eating Escape");
            Assert.IsFalse(TabMenu.HasExpandedMenu);
        }

        [UnityTest]
        public IEnumerator The_exit_completes_with_timeScale_zero()
        {
            var screen = Open(
                "<Animation id='a' on='open' reverse-on='close' fade='0:1' duration='0.2s' easing='linear'>" +
                "<Frame id='f'/></Animation>");
            yield return new WaitForSecondsRealtime(0.3f);

            Time.timeScale = 0f;
            UI.Close("S");
            Assert.IsTrue(screen.IsClosing);
            yield return new WaitForSecondsRealtime(0.35f);
            yield return null;
            yield return null;
            Assert.IsNull(screen.RootGameObject, "a paused game must still be able to close its menus");
        }

        [UnityTest]
        public IEnumerator A_counting_exit_is_awaited_too()
        {
            var screen = Open(
                "<Animation id='a' on='close' count='100:0' format='{0:F0}' duration='0.3s' easing='linear'>" +
                "<Text id='label'>100</Text></Animation>");
            var tmp = screen.Get<Text>("a/label").GameObject.GetComponent<TMPro.TMP_Text>();
            yield return null;

            UI.Close("S");
            Assert.IsTrue(screen.IsClosing);
            yield return new WaitForSeconds(0.15f);
            Assert.AreNotEqual("100", tmp.text, "counting down");
            Assert.AreNotEqual("0", tmp.text);
            yield return new WaitForSeconds(0.25f);
            yield return null;
            yield return null;
            Assert.IsNull(screen.RootGameObject);
        }
    }
}
