using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>@id</c> resolves lexically — subtree, then each enclosing scope, then the Screen (spec
    /// 2026-08-31-hug-reveal-flip-checked-design §4.3). Before this, a trigger could only name an id
    /// inside its own subtree, so pointing at a sibling was inexpressible.
    /// </summary>
    public class TriggerIdScopeTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Header = "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>";
        private const string ScreenOpen = "<Screen name='S'>";
        private const string Footer = "</Screen></PromptUGUI>";

        private static PromptUGUI.Application.Screen Open(string body)
        {
            UI.LoadDocument("t", Header + ScreenOpen + body + Footer);
            return UI.Open("S");
        }

        private static void Click(PromptUGUI.Application.Screen s, string id)
            => s.Get<Btn>(id).GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

        [Test]
        public void A_trigger_can_name_a_sibling()
        {
            var s = Open(
                "<VStack id='row' anchor='top-left' width='200' height='100'>" +
                "<Btn id='b' height='20'>x</Btn>" +
                "<Trigger id='t' on='click@b'><Frame height='10'/></Trigger>" +
                "</VStack>");
            var fired = 0;
            using var sub = s.Get<Trigger>("t").OnFire.Subscribe(_ => fired++);

            Click(s, "b");

            Assert.AreEqual(1, fired, "the sibling is found by walking out to the enclosing scope");
        }

        [Test]
        public void A_trigger_can_name_a_screen_level_id_from_deep_inside()
        {
            var s = Open(
                "<Btn id='g' anchor='top-left' size='40x20'>x</Btn>" +
                "<Frame id='outer' anchor='top-right' width='100' height='100'>" +
                "<Frame id='inner' anchor='top-left' width='50' height='50'>" +
                "<Trigger id='t' on='click@g'><Frame height='10'/></Trigger>" +
                "</Frame></Frame>");
            var fired = 0;
            using var sub = s.Get<Trigger>("t").OnFire.Subscribe(_ => fired++);

            Click(s, "g");

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void The_nearest_scope_wins()
        {
            // Two Btns share the id 'b': one at screen level, one right next to the trigger. The
            // trigger must take the near one.
            var s = Open(
                "<Btn id='b' anchor='top-left' size='40x20'>far</Btn>" +
                "<VStack id='row' anchor='bottom-left' width='200' height='100'>" +
                "<Trigger id='t' on='click@b'><Frame height='10'/></Trigger>" +
                "</VStack>");
            var fired = 0;
            using var sub = s.Get<Trigger>("t").OnFire.Subscribe(_ => fired++);

            Click(s, "b");

            Assert.AreEqual(1, fired, "only one 'b' exists here — the screen-level one");
        }

        [Test]
        public void Two_instances_of_one_template_do_not_see_each_other()
        {
            UI.LoadDocument("t",
                Header +
                "<Template name='Row'>" +
                "<VStack width='200'>" +
                "<Btn id='b' height='20'>x</Btn>" +
                "<Trigger id='t' on='click@b'><Frame height='10'/></Trigger>" +
                "</VStack></Template>" +
                ScreenOpen +
                "<VStack id='outer' anchor='top-left' width='200' height='300'>" +
                "<Row id='one'/><Row id='two'/>" +
                "</VStack>" + Footer);
            var s = UI.Open("S");

            var firedOne = 0;
            var firedTwo = 0;
            using var subOne = s.Get<Trigger>("one/t").OnFire.Subscribe(_ => firedOne++);
            using var subTwo = s.Get<Trigger>("two/t").OnFire.Subscribe(_ => firedTwo++);

            s.Get<Btn>("one/b").GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

            Assert.AreEqual(1, firedOne, "the instance's own scope answers first");
            Assert.AreEqual(0, firedTwo, "and the other instance is untouched");
        }

        [Test]
        public void An_unknown_id_says_where_it_looked()
        {
            UI.LoadDocument("t", Header + ScreenOpen +
                "<VStack id='row' anchor='top-left' width='200' height='100'>" +
                "<Trigger id='t' on='click@nope'><Frame height='10'/></Trigger>" +
                "</VStack>" + Footer);

            var ex = Assert.Throws<PromptUGUI.Parser.ParseException>(() => UI.Open("S"));
            StringAssert.Contains("subtree", ex.Message);
            StringAssert.Contains("template instance", ex.Message);
            StringAssert.Contains("screen", ex.Message);
        }

        [Test]
        public void A_state_trigger_can_name_a_sibling_toggle()
        {
            var s = Open(
                "<VStack id='row' anchor='top-left' width='200' height='100'>" +
                "<Toggle id='hdr' height='20'>t</Toggle>" +
                "<Trigger id='t' on='state-selected@hdr'><Frame height='10'/></Trigger>" +
                "</VStack>");
            var fired = 0;
            using var sub = s.Get<Trigger>("t").OnFire.Subscribe(_ => fired++);

            s.Get<Toggle>("hdr").IsOn = true;

            Assert.GreaterOrEqual(fired, 1, "the state source is found through the enclosing scope");
        }

        [Test]
        public void The_subtree_still_wins_for_a_click_source()
        {
            // Regression: the historic behaviour is a subtree walk that reaches ANY depth.
            var s = Open(
                "<Trigger id='t' anchor='top-left' size='200x100' on='click@deep'>" +
                "<Frame anchor='stretch'><Btn id='deep' anchor='top-left' size='40x20'>x</Btn></Frame>" +
                "</Trigger>");
            var fired = 0;
            using var sub = s.Get<Trigger>("t").OnFire.Subscribe(_ => fired++);

            Click(s, "deep");

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void A_wrong_type_at_an_id_still_says_so()
        {
            UI.LoadDocument("t", Header + ScreenOpen +
                "<VStack id='row' anchor='top-left' width='200' height='100'>" +
                "<Frame id='f' height='20'/>" +
                "<Trigger id='t' on='state-hover@f'><Frame height='10'/></Trigger>" +
                "</VStack>" + Footer);

            var ex = Assert.Throws<PromptUGUI.Parser.ParseException>(() => UI.Open("S"));
            StringAssert.Contains("not a state source", ex.Message);
        }
    }
}
