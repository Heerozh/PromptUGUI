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
    // PlayMode integration for <Tab>: real LitMotion tint fade over actual frames + <Show> swap,
    // and the persistent Selected resting-baseline driving a state-selected <Show>. Drives via
    // SimulateState / IsOn (same path a pointer takes) — no EventSystem (mirrors BtnStateVisualsPlayTests).
    public class TabStateVisualsPlayTests
    {
        private const int Normal = 0;
        private const int Highlighted = 1;
        private const int Pressed = 2;
        private static readonly Color Half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f);

        [SetUp]
        public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        [TearDown]
        public void TearDown() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        [UnityTest]
        public IEnumerator Press_then_normal_fades_bg_tint_and_swaps_Show_over_real_frames()
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='t' pressedModulate='#808080'>
      <Show id='sn' on='state-normal'><Image id='n'/></Show>
      <Show id='sp' on='state-pressed'><Image id='p'/></Show>
    </Tab>
  </TabBar>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            yield return null;

            var tab = screen.Get<Tab>("bar/t");
            var pt = tab.GameObject.GetComponent<PuiToggle>();
            Assert.IsNotNull(pt, "Tab should host a PuiToggle");
            var bg = tab.GameObject.GetComponent<UnityImage>();
            var bgBase = bg.color;   // no selectedColor => Selected multiplier white => base

            var sn = screen.Get<Show>("bar/t/sn");
            var sp = screen.Get<Show>("bar/t/sp");
            Assert.IsTrue(sn.GameObject.activeSelf, "normal Show active at open (Selected falls back to Normal block)");
            Assert.IsFalse(sp.GameObject.activeSelf, "pressed Show inactive at open");

            pt.SimulateState(Pressed);
            Assert.IsFalse(sn.GameObject.activeSelf, "normal Show hidden when pressed");
            Assert.IsTrue(sp.GameObject.activeSelf, "pressed Show shown when pressed");

            yield return new WaitForSeconds(0.2f);
            AssertColorsEqual(bgBase * Half, bg.color, "pressed bg tint settles at base * #808080");

            pt.SimulateState(Normal);
            Assert.IsTrue(sn.GameObject.activeSelf, "normal Show active again after release");
            Assert.IsFalse(sp.GameObject.activeSelf, "pressed Show hidden again after release");

            yield return new WaitForSeconds(0.2f);
            AssertColorsEqual(bgBase, bg.color, "bg tint reverts to base after returning to Normal/Selected (white)");
        }

        // Two tabs (auto-select + allowSwitchOff=false) so selection can move off 'a'. Proves the
        // Selected resting-baseline + Normal-fallback end-to-end with real instantiation.
        [UnityTest]
        public IEnumerator IsOn_drives_state_selected_Show_and_yields_to_transient_hover()
        {
            UI.LoadDocument("t", @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a'>
      <Show id='ssel' on='state-selected'><Image id='sel'/></Show>
      <Show id='snorm' on='state-normal'><Image id='nrm'/></Show>
    </Tab>
    <Tab id='b'/>
  </TabBar>
</Screen></PromptUGUI>");
            var screen = UI.Open("S");
            yield return null;

            var a = screen.Get<Tab>("bar/a");
            var pt = a.GameObject.GetComponent<PuiToggle>();
            var ssel = screen.Get<Show>("bar/a/ssel");
            var snorm = screen.Get<Show>("bar/a/snorm");

            screen.Get<Tab>("bar/b").IsOn = true;        // a -> Normal
            Assert.IsFalse(ssel.GameObject.activeSelf, "selected Show hidden when 'a' not active");
            Assert.IsTrue(snorm.GameObject.activeSelf, "normal Show shown when 'a' not active");

            a.IsOn = true;                               // a -> Selected at rest
            Assert.IsTrue(ssel.GameObject.activeSelf, "selected Show shown when 'a' active at rest");
            Assert.IsFalse(snorm.GameObject.activeSelf, "normal Show hidden when 'a' selected");

            pt.SimulateState(Highlighted);               // hover overrides Selected; no hover block -> Normal fallback
            Assert.IsFalse(ssel.GameObject.activeSelf, "selected Show yields to transient hover");
            Assert.IsTrue(snorm.GameObject.activeSelf, "normal block is the fallback for unclaimed Hover");

            pt.SimulateState(Normal);                    // back to rest -> Selected
            Assert.IsTrue(ssel.GameObject.activeSelf, "selected Show returns at rest while still selected");
            yield return null;
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
