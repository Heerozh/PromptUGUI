using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// The parts of <c>&lt;Animation reveal&gt;</c> that need a running player loop: LitMotion never
    /// ticks in EditMode, so the box only ever moves here. Spec
    /// 2026-08-31-hug-reveal-flip-checked-design §2.4.
    /// </summary>
    public class AnimationRevealPlayTests
    {
        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        // Three 44-high rows → a content height of 140.
        private const string Rows =
            "<VStack width='200' spacing='4'><Btn height='44'/><Btn height='44'/><Btn height='44'/></VStack>";

        private static PromptUGUI.Application.Screen Open(string animAttrs)
        {
            UI.LoadDocument("t", Header +
                "<VStack id='outer' anchor='top-left' width='200' height='400' spacing='0'>" +
                $"<Animation id='a' {animAttrs}>{Rows}</Animation>" +
                "<Btn id='below' height='20'/></VStack>" + Footer);
            return UI.Open("S");
        }

        private static LayoutElement Le(PromptUGUI.Application.Screen s)
            => s.Get<PromptUGUI.Controls.Animation>("a").LayoutHost.GetComponent<LayoutElement>();

        private static RectMask2D Mask(PromptUGUI.Application.Screen s)
            => s.Get<PromptUGUI.Controls.Animation>("a").LayoutHost.GetComponent<RectMask2D>();

        [UnityTest]
        public IEnumerator Reveal_opens_to_the_measured_content()
        {
            var s = Open("on='manual' reveal='y' duration='0.2s'");
            Assume.That(Le(s).preferredHeight, Is.EqualTo(0f).Within(0.01f), "starts closed");

            s.Get<PromptUGUI.Controls.Animation>("a").Fire();
            yield return new WaitForSeconds(0.35f);

            Assert.AreEqual(140f, Le(s).preferredHeight, 0.5f, "3 rows of 44 + 2 gaps of 4");
            Assert.IsFalse(Mask(s).enabled, "fully open — the clip is dropped so batching resumes");
        }

        [UnityTest]
        public IEnumerator Opening_pushes_the_sibling_below_it_down()
        {
            var s = Open("on='manual' reveal='y' duration='0.3s'");
            var below = s.Get<Btn>("below").RectTransform;
            yield return null;   // let the first layout pass settle before reading the baseline
            var start = below.anchoredPosition.y;

            s.Get<PromptUGUI.Controls.Animation>("a").Fire();
            yield return null;
            yield return null;
            var mid = below.anchoredPosition.y;
            yield return new WaitForSeconds(0.4f);
            var end = below.anchoredPosition.y;

            Assert.Less(mid, start, "the sibling starts moving down as the fold opens (Unity: -y)");
            Assert.Less(end, mid, "and keeps moving until the fold is open");
            Assert.AreEqual(start - 140f, end, 1f, "it ends exactly one content-height lower");
        }

        [UnityTest]
        public IEnumerator Reveal_composes_with_a_fade()
        {
            var s = Open("on='manual' reveal='y' fade='0:1' duration='0.3s'");
            var cg = s.Get<PromptUGUI.Controls.Animation>("a").GameObject.GetComponent<CanvasGroup>();
            cg.alpha = 0f;

            s.Get<PromptUGUI.Controls.Animation>("a").Fire();
            yield return new WaitForSeconds(0.1f);

            Assert.Greater(cg.alpha, 0f, "the fade runs alongside the box");
            Assert.Less(cg.alpha, 1f);
            Assert.Greater(Le(s).preferredHeight, 0f);

            yield return new WaitForSeconds(0.35f);
            Assert.AreEqual(1f, cg.alpha, 0.01f);
            Assert.AreEqual(140f, Le(s).preferredHeight, 0.5f);
        }

        [UnityTest]
        public IEnumerator On_open_plays_the_reveal_at_screen_open()
        {
            var s = Open("reveal='y' duration='0.2s'");   // on= omitted → open

            yield return new WaitForSeconds(0.35f);

            Assert.AreEqual(140f, Le(s).preferredHeight, 0.5f);
        }

        [UnityTest]
        public IEnumerator An_explicit_pixel_endpoint_is_honoured()
        {
            var s = Open("on='manual' reveal='y' reveal-from='20' reveal-to='90' duration='0.2s'");
            Assume.That(Le(s).preferredHeight, Is.EqualTo(20f).Within(0.01f));

            s.Get<PromptUGUI.Controls.Animation>("a").Fire();
            yield return new WaitForSeconds(0.35f);

            Assert.AreEqual(90f, Le(s).preferredHeight, 0.5f);
            Assert.IsTrue(Mask(s).enabled, "90 still hides part of the 140-tall content");
        }
    }
}
