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
        public void PressedModulate_TintsBgAndDescendants_AndSwitchesTransitionNone()
        {
            var tab = BuildTab("pressedModulate='#808080'", "<Image id='img'/>");
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
        public void SelectedModulate_AppliesToActiveTabAtRest()
        {
            string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'><Screen name='S'>
  <TabBar id='bar'>
    <Tab id='a' selectedModulate='#808080'/>
    <Tab id='b' selectedModulate='#808080'/>
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
                "active tab bg gets selectedModulate multiplier at rest");
        }

        // Repro of the user's report: a template-wrapped <Tab> declared isOn="true" inside a
        // <TabBar>, with absolute color + selectedColor. Verifies BOTH the internal selection
        // state AND that the reactor paints the active tab its selectedColor at Open() (no runtime
        // toggle). Inactive siblings must keep the base color (catches stale-broadcaster bleed).
        [Test]
        public void DeclaredIsOn_TemplateWrappedTab_ShowsSelectedColorAtOpen()
        {
            string xml = @"<?xml version='1.0' encoding='utf-8'?>
<PromptUGUI version='1'>
  <Template name='HIconTab'>
    <Param name='text'/>
    <Param name='isOn' default='false'/>
    <Frame>
      <Tab id='tab' isOn='{{isOn}}' anchor='stretch'
           color='#202020' selectedColor='#076DD7'/>
      <Text id='lbl' anchor='stretch' raycastTarget='false'>{{text}}</Text>
    </Frame>
  </Template>
  <Screen name='S'>
    <TabBar id='bar' itemTemplate='HIconTab'>
      <HIconTab text='Cosmos' isOn='true'/>
      <HIconTab text='Alliance'/>
      <HIconTab text='Team'/>
    </TabBar>
  </Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var bar = UI.Open("S").Get<TabBar>("bar");

            Assert.AreEqual(3, bar.Count, "all three template-wrapped tabs collected");
            Assert.AreEqual(0, bar.SelectedIndex, "internal state: first tab is the active one");

            // Probe the desync: toggle.isOn vs the StateBroadcaster's composite Current.
            var pt0 = bar.GetAt(0).GameObject.GetComponent<PuiToggle>();
            Assert.IsTrue(bar.GetAt(0).IsOn, "toggle.isOn is true for the active tab");
            Assert.AreEqual(InteractState.Selected, pt0.Current,
                "broadcaster Current should be Selected for the active tab at Open");

            var sel = new Color(0x07 / 255f, 0x6D / 255f, 0xD7 / 255f, 1f);
            var bas = new Color(0x20 / 255f, 0x20 / 255f, 0x20 / 255f, 1f);
            var bg0 = bar.GetAt(0).GameObject.GetComponent<UnityImage>();
            var bg1 = bar.GetAt(1).GameObject.GetComponent<UnityImage>();
            var bg2 = bar.GetAt(2).GameObject.GetComponent<UnityImage>();

            Assert.That(bg0.color.r, Is.EqualTo(sel.r).Within(0.001f), "active tab bg = selectedColor (r)");
            Assert.That(bg0.color.g, Is.EqualTo(sel.g).Within(0.001f), "active tab bg = selectedColor (g)");
            Assert.That(bg0.color.b, Is.EqualTo(sel.b).Within(0.001f), "active tab bg = selectedColor (b)");
            Assert.That(bg1.color.r, Is.EqualTo(bas.r).Within(0.001f), "inactive tab keeps base color");
            Assert.That(bg2.color.r, Is.EqualTo(bas.r).Within(0.001f), "inactive tab keeps base color");
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
