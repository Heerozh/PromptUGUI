using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityGraphic = UnityEngine.UI.Graphic;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    // Born-frame instant rule over REAL frames (the native uGUI ColorTint flicker is play-mode only —
    // in EditMode uGUI's TweenRunner completes a CrossFade immediately, so the fade can only be
    // observed with a running player loop).
    //
    // A plain <Btn> (no *Color/*Modulate) keeps uGUI's built-in ColorTint transition, so its disabled
    // greying is a CrossFadeColor on the targetGraphic's CanvasRenderer — exactly the path a modal
    // Configure hook hits when it sets Btn.Interactable = false right after Open.
    public class StateBornFramePlayTests
    {
        [SetUp]
        public void SetUp() => UI.ResetForTests();

        [TearDown]
        public void TearDown() => UI.ResetForTests();

        [UnityTest]
        public IEnumerator Disable_in_born_frame_snaps_to_disabled_tint_instantly()
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='b'/></Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var target = (UnityGraphic)puiBtn.targetGraphic;
            Assert.IsNotNull(target, "PuiButton should have a targetGraphic (the bg)");

            var disabled = puiBtn.colors.disabledColor * puiBtn.colors.colorMultiplier;

            // Disable in the SAME (born) frame as Open — before any yield. uGUI's play-mode
            // interactable setter does DoStateTransition(Disabled, instant:false) = a 0.1s CrossFade;
            // the born-frame gate must coerce it to instant so frame 1 already shows disabled.
            btn.Interactable = false;

            AssertColorsEqual(disabled, target.canvasRenderer.GetColor(),
                "born-frame disable must snap to the disabled tint, not start a fade from the enabled look");
            yield break;
        }

        [UnityTest]
        public IEnumerator Disable_after_born_frame_still_fades_over_frames()
        {
            // Regression guard: the born-frame gate must NOT turn off uGUI's intentional runtime fade.
            // A disable on a later frame (e.g. user clicked something, then we grey a button) animates.
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='b'/></Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var target = (UnityGraphic)puiBtn.targetGraphic;

            var normal = puiBtn.colors.normalColor * puiBtn.colors.colorMultiplier;
            var disabled = puiBtn.colors.disabledColor * puiBtn.colors.colorMultiplier;

            yield return null;   // advance PAST the born frame
            yield return null;

            btn.Interactable = false;   // later frame => should FADE, not snap

            // Immediately after the flip the CrossFade has barely started: still near the enabled
            // colour, NOT already at disabled (which is what an over-broad "always instant" fix would do).
            var afterFlip = target.canvasRenderer.GetColor();
            Assert.That(ColorDistance(afterFlip, normal), Is.LessThan(ColorDistance(afterFlip, disabled)),
                "right after a later-frame disable the tint must still be closer to normal than disabled (it animates)");

            // ...and it settles at disabled once the fade window elapses.
            yield return new WaitForSeconds(0.2f);
            AssertColorsEqual(disabled, target.canvasRenderer.GetColor(),
                "later-frame disable should settle at the disabled tint after the fade window");
        }

        private static float ColorDistance(Color a, Color b)
            => Mathf.Abs(a.r - b.r) + Mathf.Abs(a.g - b.g) + Mathf.Abs(a.b - b.b) + Mathf.Abs(a.a - b.a);

        private static void AssertColorsEqual(Color expected, Color actual, string msg)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.02f), $"{msg} (r)");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.02f), $"{msg} (g)");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.02f), $"{msg} (b)");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.02f), $"{msg} (a)");
        }
    }
}
