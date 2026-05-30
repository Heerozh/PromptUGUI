using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    public class TabStateTests
    {
        private const int Normal = 0, Pressed = 2;

        [SetUp] public void SetUp() { UI.ResetForTests(); StateTintReactor.TestForceInstant = true; }
        [TearDown] public void TearDown() { UI.ResetForTests(); StateTintReactor.TestForceInstant = false; }

        // A TabBar with a single Tab carrying the given attrs/body.
        private static Tab BuildTab(string tabAttrs, string body = "")
        {
            string xml = $@"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'><Tab id='t' {tabAttrs}>{body}</Tab></TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            return UI.Open("S").Get<Tab>("bar/t");
        }

        [Test]
        public void PressedColor_TintsBgAndDescendants_AndSwitchesTransitionNone()
        {
            var tab = BuildTab("pressedColor='#808080'", "<Image id='img'/>");
            var pt = tab.GameObject.GetComponent<PuiToggle>();
            Assert.IsNotNull(pt, "Tab should host a PuiToggle");
            Assert.AreEqual(Selectable.Transition.None, pt.transition);

            var bg = tab.GameObject.GetComponent<UnityImage>();
            Assert.IsNotNull(bg.GetComponent<StateTintReactor>());

            var half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f);
            var bgBase = bg.color;   // no selectedColor set => Selected multiplier is white => base
            pt.SimulateState(Pressed);
            Assert.That(bg.color.r, Is.EqualTo((bgBase * half).r).Within(0.001f));
            pt.SimulateState(Normal);
            Assert.That(bg.color.r, Is.EqualTo(bgBase.r).Within(0.001f));
        }

        // Two tabs: auto-select + allowSwitchOff=false means we drive tab 'a' to a known Normal
        // baseline via its sibling before activating it.
        [Test]
        public void SelectedColor_AppliesToActiveTabAtRest()
        {
            string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a' selectedColor='#808080'/>
    <Tab id='b' selectedColor='#808080'/>
  </TabBar>
</Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            var a = screen.Get<Tab>("bar/a");
            var b = screen.Get<Tab>("bar/b");
            var bgA = a.GameObject.GetComponent<UnityImage>();
            var half = new Color(0.5019608f, 0.5019608f, 0.5019608f, 1f);

            b.IsOn = true;                       // a -> Normal (untinted base)
            var aBase = bgA.color;
            a.IsOn = true;                       // a -> Selected (active at rest)
            Assert.That(bgA.color.r, Is.EqualTo((aBase * half).r).Within(0.001f),
                "active tab bg gets selectedColor multiplier at rest");
        }

        [Test]
        public void InteractableFalse_BridgesToToggleAndEmitsDisabled()
        {
            var tab = BuildTab("interactable='false'");
            var pt = tab.GameObject.GetComponent<PuiToggle>();
            Assert.IsFalse(pt.interactable);
            Assert.AreEqual(InteractState.Disabled, pt.Current);
        }

        [Test]
        public void ShowInsideTab_ResolvesTabAsStateSource()
        {
            // Would throw "no <Btn>/<Tab>/<Toggle> ancestor" if Tab were not an IStateSource.
            var tab = BuildTab("", "<Show id='sn' on='state-normal'><Image/></Show>" +
                                   "<Show id='sp' on='state-pressed'><Image/></Show>");
            var sn = tab.Get<Show>("sn");
            var sp = tab.Get<Show>("sp");
            Assert.IsTrue(sn.GameObject.activeSelf);
            Assert.IsFalse(sp.GameObject.activeSelf);

            tab.GameObject.GetComponent<PuiToggle>().SimulateState(Pressed);
            Assert.IsFalse(sn.GameObject.activeSelf);
            Assert.IsTrue(sp.GameObject.activeSelf);
        }
    }
}
