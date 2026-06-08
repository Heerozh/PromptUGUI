using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;
using PuiToggle = PromptUGUI.Controls.Internal.PuiToggle;
using Toggle = PromptUGUI.Controls.Toggle;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class ToggleStateTests
    {
        private const int Normal = 0, Hover = 1, Pressed = 2;

        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        private static Toggle BuildToggle(string attrs, string body = "")
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='tg' {attrs}>{body}</Toggle>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S").Get<Toggle>("tg");
        }

        // Setting Interactable from code must drive the underlying Toggle (grey + Disabled),
        // not just the base CanvasGroup — symmetry with the XML interactable="false" path.
        [Test]
        public void RuntimeInteractableFalse_BridgesToToggleAndEmitsDisabled()
        {
            var tg = BuildToggle("");
            var pt = tg.GameObject.GetComponent<PuiToggle>();
            Assert.IsTrue(pt.interactable, "precondition: starts interactable");

            tg.Interactable = false;

            Assert.IsFalse(pt.interactable, "runtime Interactable=false should drive Toggle.interactable");
            Assert.AreEqual(InteractState.Disabled, pt.Current);
        }

        [Test]
        public void PressedModulate_SwitchesTransitionNone_AndInstallsReactors()
        {
            var tg = BuildToggle("pressedModulate='#808080'");
            var pt = tg.GameObject.GetComponent<PuiToggle>();
            Assert.IsNotNull(pt, "Toggle should host a PuiToggle");
            Assert.AreEqual(Selectable.Transition.None, pt.transition);
            // bg lives on the child "Background" GO; the installer walks all descendant graphics.
            var reactors = tg.GameObject.GetComponentsInChildren<StateTintReactor>(true);
            Assert.Greater(reactors.Length, 0);
        }

        [Test]
        public void Selected_ReadsWhenIsOnAtRest()
        {
            var tg = BuildToggle("selectedModulate='#808080'");
            var pt = tg.GameObject.GetComponent<PuiToggle>();
            Assert.AreEqual(InteractState.Normal, pt.Current);
            tg.IsOn = true;
            Assert.AreEqual(InteractState.Selected, pt.Current);
            pt.SimulateState(Pressed);
            Assert.AreEqual(InteractState.Pressed, pt.Current);
            pt.SimulateState(Normal);
            Assert.AreEqual(InteractState.Selected, pt.Current);
        }

        [Test]
        public void NoStateColor_KeepsDefaultTransition_NoReactors()
        {
            var tg = BuildToggle("");
            var reactors = tg.GameObject.GetComponentsInChildren<StateTintReactor>(true);
            Assert.AreEqual(0, reactors.Length);
        }

        // Mirror of Tab's regression on the Toggle's bg (the Background child): a checked toggle
        // hovered with no hoverColor must stay at selectedColor, not fall back to the transparent base.
        [Test]
        public void HoverOnCheckedToggle_StaysSelectedColor()
        {
            var tg = BuildToggle("color='#00000000' selectedColor='#076DD7'");
            var pt = tg.GameObject.GetComponent<PuiToggle>();
            var bg = tg.GameObject.transform.Find("Background").GetComponent<UnityImage>();
            var sel = new Color(0x07 / 255f, 0x6D / 255f, 0xD7 / 255f, 1f);

            tg.IsOn = true;                        // checked at rest -> Selected
            Assert.That(bg.color.r, Is.EqualTo(sel.r).Within(0.001f), "checked idle = selectedColor");

            pt.SimulateState(Hover);
            Assert.That(bg.color.r, Is.EqualTo(sel.r).Within(0.001f), "checked+hover stays selectedColor (r)");
            Assert.That(bg.color.b, Is.EqualTo(sel.b).Within(0.001f), "checked+hover stays selectedColor (b)");
            Assert.That(bg.color.a, Is.EqualTo(1f).Within(0.001f), "checked+hover does not fall back to transparent base");

            // Checkmark overlay (Toggle.graphic) is intact — Toggle keeps its composited check.
            var checkmark = tg.GameObject.transform.Find("Background/Checkmark");
            Assert.IsNotNull(checkmark, "Toggle keeps its Checkmark overlay");
            Assert.IsNull(checkmark.GetComponent<StateTintReactor>(),
                "Checkmark is untouched by the bg selection-aware base (no descendant reactor without a modulate)");
        }

        // Mirrors Tab's SelectedColor_SurvivesReSolve: the selected base must survive a ReSolve
        // (window resize / Variant / Theme) — that path re-applies `color` then re-Configures the
        // reactor, which repaints the current state from the persisted selection.
        [Test]
        public void SelectedColor_SurvivesReSolve()
        {
            string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <Toggle id='tg' color='#202020' selectedColor='#076DD7'/>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var tg = screen.Get<Toggle>("tg");
            var bg = tg.GameObject.transform.Find("Background").GetComponent<UnityImage>();
            var sel = new Color(0x07 / 255f, 0x6D / 255f, 0xD7 / 255f, 1f);
            tg.IsOn = true;
            Assert.That(bg.color.r, Is.EqualTo(sel.r).Within(0.001f), "selectedColor applied before ReSolve");

            screen.ReSolve();

            Assert.That(bg.color.r, Is.EqualTo(sel.r).Within(0.001f), "selectedColor survives ReSolve (r)");
            Assert.That(bg.color.b, Is.EqualTo(sel.b).Within(0.001f), "selectedColor survives ReSolve (b)");
        }

        // selectedColor alone (no hover/pressed/modulate) must still install the bg reactor and flip
        // transition=None via the selectedBase.HasValue branch of the installer's early-exit guard.
        [Test]
        public void SelectedColorOnly_InstallsReactor_AndFlipsTransitionNone()
        {
            var tg = BuildToggle("color='#202020' selectedColor='#076DD7'");
            var pt = tg.GameObject.GetComponent<PuiToggle>();
            var bg = tg.GameObject.transform.Find("Background").GetComponent<UnityImage>();
            Assert.AreEqual(Selectable.Transition.None, pt.transition);
            Assert.IsNotNull(bg.GetComponent<StateTintReactor>(), "selectedColor installs the bg reactor");
        }
    }
}
