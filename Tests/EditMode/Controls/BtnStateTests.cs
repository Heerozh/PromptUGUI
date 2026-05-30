using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.UI;
using PuiImage = PromptUGUI.Controls.Image;
using PuiText = PromptUGUI.Controls.Text;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class BtnStateTests
    {
        // Mirror of the (protected) UnityEngine.UI.Selectable.SelectionState ordinals.
        // The test assembly cannot name the protected nested type, so PuiButton's
        // test hooks take the ordinal int and cast internally.
        private const int Normal = 0;
        private const int Highlighted = 1;
        private const int Pressed = 2;
        private const int Selected = 3;
        private const int Disabled = 4;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            StateTintReactor.TestForceInstant = false;
        }

        private static Btn BuildBtn(string extraAttrs = "")
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' {extraAttrs}>Hi</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            return screen.Get<Btn>("b");
        }

        // Builds a Btn from a full inner-XML body (children + attrs on the Btn itself).
        private static Btn BuildBtnXml(string btnAttrs, string body)
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Btn id='b' {btnAttrs}>{body}</Btn>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            return screen.Get<Btn>("b");
        }

        [Test]
        public void Map_TranslatesSelectionStatesToBtnState()
        {
            Assert.AreEqual(BtnState.Normal, PuiButton.Map(Normal));
            Assert.AreEqual(BtnState.Hover, PuiButton.Map(Highlighted));
            Assert.AreEqual(BtnState.Pressed, PuiButton.Map(Pressed));
            Assert.AreEqual(BtnState.Disabled, PuiButton.Map(Disabled));
            // Momentary button must not keep a sticky highlight after a touch tap.
            Assert.AreEqual(BtnState.Normal, PuiButton.Map(Selected));
        }

        [Test]
        public void OnState_EmitsCurrentValueImmediatelyAsNormal()
        {
            var btn = BuildBtn();
            BtnState seen = (BtnState)(-1);
            using var _ = btn.OnState.Subscribe(s => seen = s);
            Assert.AreEqual(BtnState.Normal, seen);
        }

        [Test]
        public void OnState_EmitsSequenceFollowingSimulatedStates()
        {
            var btn = BuildBtn();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.IsNotNull(puiBtn, "Btn should host a PuiButton");

            var seen = new List<BtnState>();
            using var _ = btn.OnState.Subscribe(s => seen.Add(s));

            puiBtn.SimulateState(Highlighted); // -> Hover
            puiBtn.SimulateState(Pressed);     // -> Pressed
            puiBtn.SimulateState(Selected);    // -> Normal (Selected folds to Normal)
            puiBtn.SimulateState(Highlighted); // -> Hover again (proves stream still live)

            // First emission is the replayed Normal initial value, then each *changed* state.
            // ReactiveProperty is distinct-until-changed, so Selected->Normal emits a single
            // Normal (the prior value was Pressed) and a redundant Normal->Normal would be
            // suppressed — see the dedicated dedup test below.
            CollectionAssert.AreEqual(
                new[]
                {
                    BtnState.Normal,  // replayed initial value
                    BtnState.Hover,
                    BtnState.Pressed,
                    BtnState.Normal,  // Selected -> Normal
                    BtnState.Hover,
                },
                seen);
        }

        [Test]
        public void OnState_IsDistinctUntilChanged_SelectedAfterNormalEmitsNothing()
        {
            var btn = BuildBtn();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            var seen = new List<BtnState>();
            using var _ = btn.OnState.Subscribe(s => seen.Add(s));

            // Already Normal (initial). Selected folds to Normal, so no change -> no emission.
            puiBtn.SimulateState(Selected);
            puiBtn.SimulateState(Normal);

            CollectionAssert.AreEqual(new[] { BtnState.Normal }, seen);
        }

        [Test]
        public void InteractableFalse_DrivesButtonAndEmitsDisabled()
        {
            // `interactable` is a common attr: it flows through ApplyCommon -> Control.Interactable
            // (CanvasGroup) and, via Btn.OnAfterApply, is bridged to Button.interactable. Setting
            // Button.interactable = false synchronously runs DoStateTransition(Disabled).
            var btn = BuildBtn("interactable='false'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.IsNotNull(puiBtn, "Btn should host a PuiButton");

            Assert.IsFalse(puiBtn.interactable, "Button.interactable should mirror interactable='false'");

            BtnState seen = (BtnState)(-1);
            using var _ = btn.OnState.Subscribe(s => seen = s);
            Assert.AreEqual(BtnState.Disabled, seen);
        }

        [Test]
        public void InteractableOmitted_StaysNormalAndButtonInteractable()
        {
            var btn = BuildBtn();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();

            Assert.IsTrue(puiBtn.interactable, "default <Btn> Button.interactable should be true");

            BtnState seen = (BtnState)(-1);
            using var _ = btn.OnState.Subscribe(s => seen = s);
            Assert.AreEqual(BtnState.Normal, seen);
        }

        [Test]
        public void PlainBtn_BackCompat_TargetGraphicIsBgAndTransitionIsColorTint()
        {
            var btn = BuildBtn();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var bg = btn.GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(bg, puiBtn.targetGraphic);
            Assert.AreEqual(Selectable.Transition.ColorTint, puiBtn.transition);
        }

        // ---- Phase 2: state-driven tint fan-out (StateTintReactor) ----

        // Force the reactor's fade to 0 so the target colour is applied synchronously
        // (no frame loop in EditMode). PRODUCTION default stays 0.1f.
        private static void UseInstantTint() => StateTintReactor.TestForceInstant = true;

        [Test]
        public void PressedColor_InstallsReactorOnBgAndDescendantGraphics()
        {
            var btn = BuildBtnXml("pressedColor='#808080'", "<Image id='img'/><Text id='t'>x</Text>");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            Assert.IsNotNull(bg.GetComponent<StateTintReactor>(), "bg should host a reactor");

            var img = btn.Get<PuiImage>("img");
            Assert.IsNotNull(img.GameObject.GetComponent<StateTintReactor>(), "Image graphic should host a reactor");

            var txt = btn.Get<PuiText>("t");
            Assert.IsNotNull(txt.GameObject.GetComponent<StateTintReactor>(), "Text graphic should host a reactor");
        }

        [Test]
        public void PressedColor_TintsThenRestoresOnStateChange()
        {
            UseInstantTint();
            var btn = BuildBtnXml("pressedColor='#808080'", "<Image id='img'/>");
            var bg = btn.GameObject.GetComponent<UnityImage>();
            var img = btn.Get<PuiImage>("img").GameObject.GetComponent<UnityImage>();

            var bgBase = bg.color;
            var imgBase = img.color;
            var half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f); // #808080

            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            puiBtn.SimulateState(Pressed);

            AssertColorsEqual(bgBase * half, bg.color);
            AssertColorsEqual(imgBase * half, img.color);

            puiBtn.SimulateState(Normal);
            AssertColorsEqual(bgBase, bg.color);
            AssertColorsEqual(imgBase, img.color);
        }

        [Test]
        public void PressedColor_VariantReSolve_KeepsSingleReactorAndReConfiguresMultiplier()
        {
            UseInstantTint();
            // pressedColor has an inline Variant override: light=#808080, dark=#404040.
            var btn = BuildBtnXml("pressedColor='#808080' pressedColor.dark='#404040'", "<Image id='img'/>");
            var bg = btn.GameObject.GetComponent<UnityImage>();

            // Base (authored) colour + the single reactor installed by the first apply.
            var bgBase = bg.color;
            Assert.AreEqual(1, bg.GetComponents<StateTintReactor>().Length,
                "bg should host exactly one reactor after the initial apply");

            // Toggle the 'dark' variant: VariantStore.Changed fires → open Screen ReSolves →
            // Btn.OnAfterApply re-runs ApplyStateTint with the dark-resolved multiplier.
            UI.Variants.Set("dark", true);

            // (a) No duplicate reactor — re-apply reuses the existing one via GetComponent ?? Add.
            Assert.AreEqual(1, bg.GetComponents<StateTintReactor>().Length,
                "Variant ReSolve must NOT add a second reactor");

            // (b) Pressed now multiplies by the dark override (#404040), and the base colour
            // is still the original authored colour (reactor never re-captured a tinted value).
            var dark = new Color(0.2509804f, 0.2509804f, 0.2509804f, 1f); // #404040
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            puiBtn.SimulateState(Pressed);
            AssertColorsEqual(bgBase * dark, bg.color);

            // Returning to Normal restores the untinted base, proving base wasn't promoted.
            puiBtn.SimulateState(Normal);
            AssertColorsEqual(bgBase, bg.color);
        }

        [Test]
        public void StateReactFalse_ChildKeepsColorAndHasNoReactor()
        {
            UseInstantTint();
            var btn = BuildBtnXml("pressedColor='#808080'",
                "<Image id='keep' color='#FF0000' stateReact='false'/>");
            var keep = btn.Get<PuiImage>("keep").GameObject.GetComponent<UnityImage>();

            Assert.IsNull(keep.GetComponent<StateTintReactor>(),
                "stateReact='false' child must not get a reactor");

            var before = keep.color;
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            puiBtn.SimulateState(Pressed);
            AssertColorsEqual(before, keep.color); // unchanged across state
            puiBtn.SimulateState(Normal);
            AssertColorsEqual(before, keep.color);
        }

        [Test]
        public void NoStateColor_KeepsColorTintAndHasNoReactors()
        {
            var btn = BuildBtnXml("", "<Image id='img'/><Text id='t'>x</Text>");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.AreEqual(Selectable.Transition.ColorTint, puiBtn.transition);

            var reactors = btn.GameObject.GetComponentsInChildren<StateTintReactor>(includeInactive: true);
            Assert.AreEqual(0, reactors.Length, "plain Btn must install zero reactors");
        }

        [Test]
        public void StateColor_SwitchesTransitionToNone()
        {
            var btn = BuildBtn("pressedColor='#808080'");
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            Assert.AreEqual(Selectable.Transition.None, puiBtn.transition);
        }

        private static void AssertColorsEqual(Color expected, Color actual)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.001f), "r");
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.001f), "g");
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.001f), "b");
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.001f), "a");
        }
    }
}
