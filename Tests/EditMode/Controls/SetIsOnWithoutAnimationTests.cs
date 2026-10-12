using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using Animation = PromptUGUI.Controls.Animation;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>Tab.SetIsOn(on, animate: false)</c> / <c>Toggle.SetIsOn</c> / <c>TabBar.ClearSelection(animate: false)</c>:
    /// a selection set from code that lands on the end state of every <c>checked</c> / <c>unchecked</c>
    /// <c>&lt;Animation&gt;</c> it drives — the way a Screen's opening establishes them (FND-D10) — instead of
    /// playing them. The tab the group turns off on the way is included.
    /// </summary>
    public class SetIsOnWithoutAnimationTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string body)
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S'>"
                                 + body + "</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private static string TabWithChevron(string id, bool on) =>
            $"<Tab id='{id}' isOn='{(on ? "true" : "false")}' width='100' height='40'>"
            + $"<Animation id='{id}Chev' on='checked' reverse-on='unchecked' rotate='0:180' duration='1s'>"
            + "<Frame width='12' height='12'/></Animation></Tab>";

        private static PromptUGUI.Application.Screen OpenBar(string barAttrs = "") =>
            Open($"<TabBar id='bar' {barAttrs}>{TabWithChevron("a", true)}{TabWithChevron("b", false)}</TabBar>");

        private static float Angle(PromptUGUI.Application.Screen s, string animationId) =>
            ((RectTransform)s.Get<Animation>(animationId).ChildHostTransform).localEulerAngles.z;

        [Test]
        public void Selecting_without_animation_lands_both_tabs_on_their_end_states()
        {
            var s = OpenBar();
            Assume.That(Angle(s, "aChev"), Is.EqualTo(180f).Within(0.01f));
            Assume.That(Angle(s, "bChev"), Is.EqualTo(0f).Within(0.01f));

            s.Get<Tab>("b").SetIsOn(true, animate: false);

            Assert.IsTrue(s.Get<Tab>("b").IsOn);
            Assert.IsFalse(s.Get<Tab>("a").IsOn);
            Assert.AreEqual(180f, Angle(s, "bChev"), 0.01f, "the selected tab's chevron is already turned");
            Assert.AreEqual(0f, Angle(s, "aChev"), 0.01f, "…and the one the group turned off is already back");
        }

        [Test]
        public void An_ordinary_selection_still_animates()
        {
            var s = OpenBar();
            s.Get<Tab>("b").SetIsOn(true, animate: false);   // the scope must not outlive the call

            s.Get<Tab>("a").IsOn = true;

            Assert.Less(Angle(s, "aChev"), 179f, "a plain IsOn plays the animation — it starts from where it is");
        }

        [Test]
        public void Animate_true_is_the_ordinary_setter()
        {
            var s = OpenBar();

            s.Get<Tab>("b").SetIsOn(true, animate: true);

            Assert.IsTrue(s.Get<Tab>("b").IsOn);
            Assert.Less(Angle(s, "bChev"), 179f);
        }

        [Test]
        public void ClearSelection_without_animation_lands_the_last_tab_on_its_off_state()
        {
            var s = OpenBar();
            Tab announced = s.Get<Tab>("a");
            using var sub = s.Get<TabBar>("bar").OnSelectionChanged.Subscribe(t => announced = t);

            s.Get<TabBar>("bar").ClearSelection(animate: false);

            Assert.AreEqual(-1, s.Get<TabBar>("bar").SelectedIndex);
            Assert.IsNull(announced, "still announced");
            Assert.AreEqual(0f, Angle(s, "aChev"), 0.01f);
        }

        [Test]
        public void Toggle_SetIsOn_without_animation()
        {
            var s = Open("<Toggle id='t' isOn='false' width='200' height='40'>"
                         + "<Animation id='chev' on='checked' reverse-on='unchecked' rotate='0:180' duration='1s'>"
                         + "<Frame width='12' height='12'/></Animation></Toggle>");

            s.Get<Toggle>("t").SetIsOn(true, animate: false);

            Assert.IsTrue(s.Get<Toggle>("t").IsOn);
            Assert.AreEqual(180f, Angle(s, "chev"), 0.01f);
        }

        [Test]
        public void A_plain_trigger_still_fires()
        {
            // <Trigger> has no end state to land on: "without animation" changes nothing for it.
            var s = Open("<Toggle id='t' isOn='false'><Trigger id='k' on='checked'><Frame/></Trigger></Toggle>");
            var fired = 0;
            using var sub = s.Get<Trigger>("k").OnFire.Subscribe(_ => fired++);

            s.Get<Toggle>("t").SetIsOn(true, animate: false);

            Assert.AreEqual(1, fired);
        }
    }
}
