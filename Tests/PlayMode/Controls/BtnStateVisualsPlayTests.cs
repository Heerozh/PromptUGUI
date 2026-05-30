using System.Collections;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.TestTools;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    // PlayMode integration: exercises the StateTintReactor's LitMotion fade over REAL frames
    // (EditMode uses StateTintReactor.TestForceInstant and can't cover the actual animation),
    // and proves <Show> artwork toggles + reverts as the Btn's InteractState changes.
    //
    // Drive path: PuiButton.SimulateState(ordinal) — the documented internal test hook that
    // routes through the REAL Selectable.DoStateTransition (same code path a pointer event takes),
    // then yields real frames so LitMotion's Update-driven tween settles. We deliberately do NOT
    // use ExecuteEvents/EventSystem here: SimulateState through DoStateTransition + real frame
    // advancement already exercises the real fade loop (the point of this phase) without a
    // Canvas/EventSystem raycast harness that the other PlayMode tests don't establish either.
    public class BtnStateVisualsPlayTests
    {
        // Mirror of the (protected) UnityEngine.UI.Selectable.SelectionState ordinals.
        private const int Normal = 0;
        private const int Pressed = 2;

        // #808080 as a linear-ish float color (matches BtnStateTests / Color32 -> Color).
        private static readonly Color Half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f);

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            // Defend against TestForceInstant leaking from another test class: we WANT the real fade.
            StateTintReactor.TestForceInstant = false;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;
        }

        [UnityTest]
        public IEnumerator Press_then_normal_fades_bg_tint_and_swaps_Show_artwork_over_real_frames()
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' pressedColor='#808080'>
    <Show id='sn' on='state-normal'><Image id='n'/></Show>
    <Show id='sp' on='state-pressed'><Image id='p'/></Show>
  </Btn>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            yield return null;

            var btn = screen.Get<Btn>("b");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.IsNotNull(puiBtn, "Btn should host a PuiButton");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            Assert.IsNotNull(bg, "Btn should host a background Image");

            var sn = screen.Get<Show>("b/sn");
            var sp = screen.Get<Show>("b/sp");

            // Capture the authored base colour BEFORE any state drive.
            var bgBase = bg.color;

            // Sanity: at open the screen is in Normal — normal Show shown, pressed Show hidden.
            Assert.IsTrue(sn.GameObject.activeSelf, "normal Show active at open");
            Assert.IsFalse(sp.GameObject.activeSelf, "pressed Show inactive at open");

            // ---- Drive Pressed and let the 0.1s LitMotion fade settle over real frames ----
            puiBtn.SimulateState(Pressed);

            // <Show> toggles are SetActive — they flip synchronously, no frame wait needed.
            Assert.IsFalse(sn.GameObject.activeSelf, "normal Show hidden when pressed");
            Assert.IsTrue(sp.GameObject.activeSelf, "pressed Show shown when pressed");

            // 0.2s >> 0.1s fade: assert the SETTLED endpoint, not a mid-tween value.
            yield return new WaitForSeconds(0.2f);

            AssertColorsEqual(bgBase * Half, bg.color, "pressed bg tint should settle at base * #808080");

            // ---- Drive Normal and assert the tint reverts to base + artwork swaps back ----
            puiBtn.SimulateState(Normal);

            Assert.IsTrue(sn.GameObject.activeSelf, "normal Show active again after release");
            Assert.IsFalse(sp.GameObject.activeSelf, "pressed Show hidden again after release");

            yield return new WaitForSeconds(0.2f);

            AssertColorsEqual(bgBase, bg.color, "bg tint should revert to base after returning to Normal");
        }

        [UnityTest]
        public IEnumerator Pressed_tint_is_mid_tween_between_base_and_target_during_the_fade()
        {
            // Proves the colour actually ANIMATES rather than snapping: sampled mid-fade the bg is
            // strictly between base and the settled target. We compare the per-channel delta
            // magnitude (base differs from target on every RGB channel for #808080) and require the
            // mid-sample to be a partial move — well clear of both endpoints' tolerances.
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' pressedColor='#808080'><Image id='img'/></Btn>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            yield return null;

            var btn = screen.Get<Btn>("b");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var bg = btn.GameObject.GetComponent<UnityImage>();

            var bgBase = bg.color;
            var bgTarget = bgBase * Half;

            puiBtn.SimulateState(Pressed);

            // Sample ~halfway through the 0.1s fade. WaitForSeconds yields until at least the
            // requested real time has elapsed, so 0.04s lands inside (0, 0.1) for the tween.
            yield return new WaitForSeconds(0.04f);
            var mid = bg.color;

            // Mid value must have moved off base toward target on the green channel (the most
            // significant change for a grey multiplier) but not yet reached the target.
            float baseG = bgBase.g;
            float targetG = bgTarget.g;
            Assert.That(mid.g, Is.LessThan(baseG - 0.02f),
                $"mid-tween green ({mid.g}) should have moved below base ({baseG}) — proves it animates");
            Assert.That(mid.g, Is.GreaterThan(targetG + 0.02f),
                $"mid-tween green ({mid.g}) should not yet have reached target ({targetG}) — proves it's not instant");

            // And it still settles at the target after the full window.
            yield return new WaitForSeconds(0.2f);
            AssertColorsEqual(bgTarget, bg.color, "bg should settle at target after the fade window");
        }

        private static void AssertColorsEqual(Color expected, Color actual, string msg)
        {
            // Tolerance 0.02 per channel: LitMotion settles within float-epsilon of the target by
            // 0.2s, but easing + frame granularity make a tighter bound fragile on slow CI.
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.02f), $"{msg} (r)");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.02f), $"{msg} (g)");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.02f), $"{msg} (b)");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.02f), $"{msg} (a)");
        }
    }
}
