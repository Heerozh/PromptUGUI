using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine.UI;
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

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

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

        // NOTE (Phase 1.4 — interactable): intentionally NOT implemented in this commit.
        // The XML `interactable` attribute is a CommonAttr (see ControlAttributeApplier.CommonAttrs),
        // so it is filtered out before per-control [UIAttr] setters run and flows ONLY through
        // ApplyCommon -> Control.Interactable (CanvasGroup), which does not drive Button.interactable
        // and therefore does not put the Selectable into Disabled. A [UIAttr("interactable")] on Btn
        // would be dead code. Resolving this is a design fork (see PR/report); the
        // InteractableFalse_EmitsDisabled test is deferred until the fork is decided.

        [Test]
        public void PlainBtn_BackCompat_TargetGraphicIsBgAndTransitionIsColorTint()
        {
            var btn = BuildBtn();
            var puiBtn = btn.GameObject.GetComponent<PuiButton>();
            var bg = btn.GameObject.GetComponent<UnityImage>();
            Assert.AreEqual(bg, puiBtn.targetGraphic);
            Assert.AreEqual(Selectable.Transition.ColorTint, puiBtn.transition);
        }
    }
}
