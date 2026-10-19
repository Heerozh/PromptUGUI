using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>click@x</c> where x is not a <c>&lt;Btn&gt;</c> (spec 2026-10-19-id-path §4.2): the one <c>&lt;Btn&gt;</c>
    /// in x's subtree — what a bare <c>on="click"</c> means. None or several is an error; other kinds do not
    /// fall back.
    /// </summary>
    public class ClickFallbackTests
    {
        private const string Templates =
            "<Template name='IconCell'><Frame width='44' height='44'>" +
            "<Btn id='btn' anchor='stretch'>x</Btn><Frame id='badge' anchor='top-right' size='8x8'/></Frame></Template>" +
            "<Template name='Two'><Frame width='44' height='44'>" +
            "<Btn id='b1' anchor='top-left' size='20x20'>1</Btn><Btn id='b2' anchor='bottom-right' size='20x20'>2</Btn></Frame></Template>" +
            "<Template name='Bare'><Frame width='44' height='44'><Text id='label'>x</Text></Frame></Template>";

        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static PromptUGUI.Application.Screen Open(string body)
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + Templates +
                                 "<Screen name='S'>" + body + "</Screen></PromptUGUI>");
            return UI.Open("S");
        }

        private static void Click(Btn b) => b.GameObject.GetComponent<UnityEngine.UI.Button>().onClick.Invoke();

        [Test]
        public void A_click_trigger_on_an_instance_follows_its_one_Btn()
        {
            var s = Open("<IconCell id='slot' anchor='top-left'/><Trigger id='tr' on='click@slot'><Frame height='10'/></Trigger>");
            var fired = 0;
            using var _ = s.Get<Trigger>("tr").OnFire.Subscribe(__ => fired++);

            Click(s.Get<Btn>("slot/btn"));

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void An_instance_with_no_Btn_is_an_error()
        {
            var ex = Assert.Catch<Exception>(() =>
                Open("<Bare id='bare' anchor='top-left'/><Trigger id='tr' on='click@bare'><Frame height='10'/></Trigger>"));
            StringAssert.Contains("no <Btn>", ex.Message);
        }

        [Test]
        public void An_instance_with_two_Btns_lists_them_and_asks_for_a_path()
        {
            var ex = Assert.Catch<Exception>(() =>
                Open("<Two id='two' anchor='top-left'/><Trigger id='tr' on='click@two'><Frame height='10'/></Trigger>"));
            StringAssert.Contains("b1", ex.Message);
            StringAssert.Contains("b2", ex.Message);
            StringAssert.Contains("click@two/", ex.Message);
        }

        [Test]
        public void A_Btn_named_directly_is_unchanged()
        {
            var s = Open("<Btn id='b' anchor='top-left' size='40x20'>x</Btn><Trigger id='tr' on='click@b'><Frame height='10'/></Trigger>");
            var fired = 0;
            using var _ = s.Get<Trigger>("tr").OnFire.Subscribe(__ => fired++);

            Click(s.Get<Btn>("b"));

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void Hover_does_not_fall_back()
        {
            var ex = Assert.Catch<Exception>(() =>
                Open("<IconCell id='slot' anchor='top-left'/><Trigger id='tr' on='hover-enter@slot'><Frame height='10'/></Trigger>"));
            StringAssert.Contains("not supported as pointer event source", ex.Message);
        }
    }
}
