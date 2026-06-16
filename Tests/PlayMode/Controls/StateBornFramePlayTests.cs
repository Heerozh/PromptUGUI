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
        public IEnumerator Disable_after_born_frame_applies_grayscale_not_instant_colortint()
        {
            // Regression guard: the born-frame gate on PuiButton.DoStateTransition must NOT force
            // instant=true on later frames. With the default grayscale feature, disabledColor is
            // neutralised to white (= normalColor), so the ColorTint fade is a no-op colour-wise;
            // we therefore verify the born-frame guard through the grayscale material path instead:
            // a later-frame disable must cause the grayscale shader to be applied (not missed), and
            // re-enabling must restore the default material.
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'><Btn id='b'/></Screen></PromptUGUI>");
            var screen = UI.Open("S");
            var btn = screen.Get<Btn>("b");
            var bg = btn.GameObject.GetComponent<UnityEngine.UI.Image>();

            yield return null;   // advance PAST the born frame
            yield return null;

            btn.Interactable = false;   // later frame => grayscale must be applied
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name,
                "later-frame disable must apply the grayscale shader");

            yield return new WaitForSeconds(0.2f);
            Assert.AreEqual("UI/Grayscale", bg.material.shader.name,
                "grayscale must persist after the fade window");

            btn.Interactable = true;
            Assert.AreEqual(bg.defaultMaterial, bg.material,
                "re-enable must restore the default material");
        }

        private static void AssertColorsEqual(Color expected, Color actual, string msg)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.02f), $"{msg} (r)");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.02f), $"{msg} (g)");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.02f), $"{msg} (b)");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.02f), $"{msg} (a)");
        }
    }
}
