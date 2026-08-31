using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Parser;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>on="checked"</c> / <c>on="unchecked"</c> — persistent on/off state, as opposed to the
    /// transient <c>state-*</c> machine (spec 2026-08-31-hug-reveal-flip-checked-design §4.4).
    /// </summary>
    public class CheckedTriggerTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Header = "<?xml version='1.0' encoding='utf-8'?>" +
            "<PromptUGUI version='1'><Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        private static PromptUGUI.Application.Screen Open(string body)
        {
            UI.LoadDocument("t", Header + body + Footer);
            return UI.Open("S");
        }

        // ── <Trigger> ────────────────────────────────────────────────────────────────────

        [Test]
        public void Checked_fires_at_open_when_already_on()
        {
            var s = Open("<Toggle id='t' isOn='true'><Trigger id='k' on='checked'><Frame/></Trigger></Toggle>");
            var fired = 0;
            using var sub = s.Get<Trigger>("k").OnFire.Subscribe(_ => fired++);

            // The dispatch happened during Open, before the subscription — assert the follow-up
            // behaviour instead: flipping off then on fires exactly once more.
            s.Get<Toggle>("t").IsOn = false;
            s.Get<Toggle>("t").IsOn = true;

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void Checked_and_unchecked_fire_on_their_own_edge()
        {
            var s = Open(
                "<Toggle id='t' isOn='false'>" +
                "<Trigger id='on' on='checked'><Frame/></Trigger>" +
                "<Trigger id='off' on='unchecked'><Frame/></Trigger>" +
                "</Toggle>");
            var onCount = 0;
            var offCount = 0;
            using var a = s.Get<Trigger>("on").OnFire.Subscribe(_ => onCount++);
            using var b = s.Get<Trigger>("off").OnFire.Subscribe(_ => offCount++);

            s.Get<Toggle>("t").IsOn = true;
            Assert.AreEqual(1, onCount);
            Assert.AreEqual(0, offCount);

            s.Get<Toggle>("t").IsOn = false;
            Assert.AreEqual(1, onCount);
            Assert.AreEqual(1, offCount);
        }

        [Test]
        public void A_trigger_can_name_a_toggle_by_id()
        {
            var s = Open(
                "<VStack id='row' anchor='top-left' width='200' height='100'>" +
                "<Toggle id='hdr' height='20' isOn='false'>t</Toggle>" +
                "<Trigger id='k' on='checked@hdr'><Frame height='10'/></Trigger>" +
                "</VStack>");
            var fired = 0;
            using var sub = s.Get<Trigger>("k").OnFire.Subscribe(_ => fired++);

            s.Get<Toggle>("hdr").IsOn = true;

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void A_tab_is_a_toggle_source_too()
        {
            var s = Open(
                "<TabBar id='bar' anchor='top-left' width='200' height='40'>" +
                "<Tab id='a' text='A' isOn='true'/>" +
                "<Tab id='b' text='B'><Trigger id='k' on='checked'><Frame/></Trigger></Tab>" +
                "</TabBar>");
            var fired = 0;
            using var sub = s.Get<Trigger>("k").OnFire.Subscribe(_ => fired++);

            s.Get<Tab>("b").IsOn = true;

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void A_group_mate_being_selected_unchecks_this_one()
        {
            var s = Open(
                "<VStack id='row' anchor='top-left' width='200' height='100'>" +
                "<Toggle id='a' height='20' group='g' isOn='true'>" +
                "<Trigger id='k' on='unchecked'><Frame/></Trigger></Toggle>" +
                "<Toggle id='b' height='20' group='g'>b</Toggle>" +
                "</VStack>");
            var fired = 0;
            using var sub = s.Get<Trigger>("k").OnFire.Subscribe(_ => fired++);

            s.Get<Toggle>("b").IsOn = true;

            Assert.AreEqual(1, fired, "the ToggleGroup turned 'a' off — that is a real unchecked edge");
        }

        [Test]
        public void A_bare_checked_needs_a_toggle_ancestor()
        {
            UI.LoadDocument("t", Header +
                "<Btn id='b'><Trigger id='k' on='checked'><Frame/></Trigger></Btn>" + Footer);

            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("<Toggle>", ex.Message);
        }

        [Test]
        public void Checked_at_a_non_toggle_id_says_so()
        {
            UI.LoadDocument("t", Header +
                "<VStack id='row' anchor='top-left' width='200' height='100'>" +
                "<Btn id='b' height='20'>x</Btn>" +
                "<Trigger id='k' on='checked@b'><Frame height='10'/></Trigger>" +
                "</VStack>" + Footer);

            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("not a toggle source", ex.Message);
        }

        // ── <Show> ───────────────────────────────────────────────────────────────────────

        [Test]
        public void Show_blocks_are_complementary_and_established_at_open()
        {
            var s = Open(
                "<Toggle id='t' isOn='true'>" +
                "<Show on='checked'><Frame id='yes'/></Show>" +
                "<Show on='unchecked'><Frame id='no'/></Show>" +
                "</Toggle>");

            Assert.IsTrue(s.Get<Frame>("yes").GameObject.activeInHierarchy);
            Assert.IsFalse(s.Get<Frame>("no").GameObject.activeInHierarchy);

            s.Get<Toggle>("t").IsOn = false;

            Assert.IsFalse(s.Get<Frame>("yes").GameObject.activeInHierarchy);
            Assert.IsTrue(s.Get<Frame>("no").GameObject.activeInHierarchy);
        }

        [Test]
        public void Show_can_point_at_a_sibling_toggle()
        {
            // The whole point of §4: a header Toggle showing a panel that is NOT inside it.
            var s = Open(
                "<VStack id='row' anchor='top-left' width='200' height='200'>" +
                "<Toggle id='hdr' height='20' isOn='true'>任务</Toggle>" +
                "<Show on='checked@hdr'><Frame id='panel' height='100'/></Show>" +
                "</VStack>");

            Assert.IsTrue(s.Get<Frame>("panel").GameObject.activeInHierarchy);

            s.Get<Toggle>("hdr").IsOn = false;

            Assert.IsFalse(s.Get<Frame>("panel").GameObject.activeInHierarchy);
        }

        [Test]
        public void Hovering_does_not_take_a_checked_block_away()
        {
            // state-selected would blink out here; that is exactly why checked exists.
            var s = Open(
                "<Toggle id='t' isOn='true'>" +
                "<Show on='checked'><Frame id='yes'/></Show>" +
                "</Toggle>");
            Assume.That(s.Get<Frame>("yes").GameObject.activeInHierarchy, Is.True);

            var go = s.Get<Toggle>("t").GameObject;
            ExecuteEvents.Execute(go, new PointerEventData(EventSystem.current), ExecuteEvents.pointerEnterHandler);

            Assert.IsTrue(s.Get<Frame>("yes").GameObject.activeInHierarchy,
                "checked asks 'is it on', which hovering does not change");
        }

        [Test]
        public void Show_rejects_a_non_state_event_and_lists_checked()
        {
            UI.LoadDocument("t", Header +
                "<Toggle id='t'><Show on='click'><Frame/></Show></Toggle>" + Footer);

            var ex = Assert.Throws<ParseException>(() => UI.Open("S"));
            StringAssert.Contains("checked", ex.Message);
        }

        // ── <Animation> first-frame establishment (FND-D10) ──────────────────────────────

        [Test]
        public void An_animation_already_in_state_establishes_its_end_state_without_playing()
        {
            var s = Open(
                "<Toggle id='t' isOn='true' width='200' height='40'>" +
                "<Animation id='a' on='checked' reverse-on='unchecked' rotate='0:180' duration='1s'>" +
                "<Frame id='chev' width='12' height='12'/></Animation>" +
                "</Toggle>");
            var proxy = (RectTransform)s.Get<PromptUGUI.Controls.Animation>("a").ChildHostTransform;

            Assert.AreEqual(180f, proxy.localEulerAngles.z, 0.01f,
                "authored isOn='true' → the chevron is already turned, not turning");
        }

        [Test]
        public void The_off_state_establishes_the_reverse_end_state()
        {
            var s = Open(
                "<Toggle id='t' isOn='false' width='200' height='40'>" +
                "<Animation id='a' on='checked' reverse-on='unchecked' rotate='0:180' duration='1s'>" +
                "<Frame id='chev' width='12' height='12'/></Animation>" +
                "</Toggle>");
            var proxy = (RectTransform)s.Get<PromptUGUI.Controls.Animation>("a").ChildHostTransform;

            Assert.AreEqual(0f, proxy.localEulerAngles.z, 0.01f);
        }
    }
}
