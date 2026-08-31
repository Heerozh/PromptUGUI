using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// <c>reverse-on=</c> (spec 2026-08-31-hug-reveal-flip-checked-design §2.4.5): playing an
    /// animation backwards from wherever it currently is, so an interrupted open turns around
    /// instead of snapping.
    /// </summary>
    public class AnimationReversePlayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Rows =
            "<VStack width='200' spacing='4'><Btn height='44'/><Btn height='44'/><Btn height='44'/></VStack>";

        private static PromptUGUI.Application.Screen OpenReveal(string animAttrs)
        {
            UI.LoadDocument("t", Header +
                "<VStack id='outer' anchor='top-left' width='200' height='400' spacing='0'>" +
                $"<Animation id='a' {animAttrs}>{Rows}</Animation>" +
                "</VStack>" + Footer);
            return UI.Open("S");
        }

        private static LayoutElement Le(PromptUGUI.Application.Screen s)
            => s.Get<PromptUGUI.Controls.Animation>("a").LayoutHost.GetComponent<LayoutElement>();

        [UnityTest]
        public IEnumerator Reverse_closes_the_reveal_again()
        {
            var s = OpenReveal("on='manual' reverse-on='manual' reveal='y' duration='0.2s'");
            var anim = s.Get<PromptUGUI.Controls.Animation>("a");
            anim.Fire();
            yield return new WaitForSeconds(0.35f);
            Assume.That(Le(s).preferredHeight, Is.EqualTo(140f).Within(0.5f), "guard: it opened");

            anim.Reverse();
            yield return new WaitForSeconds(0.35f);

            Assert.AreEqual(0f, Le(s).preferredHeight, 0.5f);
            var mask = anim.LayoutHost.GetComponent<RectMask2D>();
            Assert.IsTrue(mask.enabled, "closed again — the content is hidden, so the clip is back on");
        }

        [UnityTest]
        public IEnumerator Reversing_mid_flight_does_not_jump()
        {
            var s = OpenReveal("on='manual' reverse-on='manual' reveal='y' duration='0.4s'");
            var anim = s.Get<PromptUGUI.Controls.Animation>("a");

            anim.Fire();
            yield return new WaitForSeconds(0.15f);
            var atInterrupt = Le(s).preferredHeight;
            Assume.That(atInterrupt, Is.GreaterThan(5f).And.LessThan(135f), "guard: caught it mid-flight");

            anim.Reverse();
            yield return null;
            var justAfter = Le(s).preferredHeight;

            Assert.Less(Mathf.Abs(justAfter - atInterrupt), 25f,
                "the reversal starts from where the box was, not from an endpoint");
            Assert.LessOrEqual(justAfter, atInterrupt + 0.5f, "and it is heading back down");

            yield return new WaitForSeconds(0.5f);
            Assert.AreEqual(0f, Le(s).preferredHeight, 0.5f);
        }

        [UnityTest]
        public IEnumerator Reverse_raises_OnReverse()
        {
            var s = OpenReveal("on='manual' reverse-on='manual' reveal='y' duration='0.1s'");
            var anim = s.Get<PromptUGUI.Controls.Animation>("a");
            var count = 0;
            using var sub = anim.OnReverse.Subscribe(_ => count++);

            anim.Reverse();
            yield return null;

            Assert.AreEqual(1, count);
        }

        [UnityTest]
        public IEnumerator A_transform_channel_reverses_from_its_current_value()
        {
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<Animation id='a' anchor='top-left' width='100' height='40' " +
                "on='manual' reverse-on='manual' rotate='0:180' duration='0.3s'>" +
                "<Frame width='100' height='40'/></Animation></Frame>" + Footer);
            var s = UI.Open("S");
            var anim = s.Get<PromptUGUI.Controls.Animation>("a");
            var proxy = (RectTransform)anim.ChildHostTransform;

            anim.Fire();
            yield return new WaitForSeconds(0.15f);
            var mid = proxy.localEulerAngles.z;
            Assume.That(mid, Is.GreaterThan(5f).And.LessThan(175f), "guard: caught it mid-turn");

            anim.Reverse();
            yield return new WaitForSeconds(0.35f);

            Assert.AreEqual(0f, proxy.localEulerAngles.z, 1f, "it unwinds all the way back to the start");
        }

        [UnityTest]
        public IEnumerator A_reversible_animation_replays_forward_from_where_it_is()
        {
            var s = OpenReveal("on='manual' reverse-on='manual' reveal='y' duration='0.4s'");
            var anim = s.Get<PromptUGUI.Controls.Animation>("a");

            anim.Fire();
            yield return new WaitForSeconds(0.15f);
            var before = Le(s).preferredHeight;

            anim.Fire();          // fired again mid-flight
            yield return null;
            var after = Le(s).preferredHeight;

            Assert.GreaterOrEqual(after, before - 0.5f,
                "a reversible animation continues from the current box instead of snapping back to 0");
        }

        [UnityTest]
        public IEnumerator Without_reverse_on_a_replay_still_restarts_from_the_authored_start()
        {
            // Regression: the historic behaviour must not change for animations that cannot reverse.
            UI.LoadDocument("t", Header +
                "<Frame id='box' anchor='top-left' width='300' height='200'>" +
                "<Animation id='a' anchor='top-left' width='100' height='40' " +
                "on='manual' translate='-100,0:0,0' duration='0.4s'>" +
                "<Frame width='100' height='40'/></Animation></Frame>" + Footer);
            var s = UI.Open("S");
            var anim = s.Get<PromptUGUI.Controls.Animation>("a");
            var proxy = (RectTransform)anim.ChildHostTransform;

            anim.Fire();
            yield return new WaitForSeconds(0.2f);
            Assume.That(proxy.anchoredPosition.x, Is.GreaterThan(-95f).And.LessThan(-5f));

            anim.Fire();
            yield return null;

            // It was around -12 before the re-fire (0.2s into a 0.4s OutCubic); jumping back near the
            // authored -100 is the whole point of the classic restart.
            Assert.Less(proxy.anchoredPosition.x, -70f,
                "no reverse-on → the classic 'write from, then tween' restart");
        }
    }
}
