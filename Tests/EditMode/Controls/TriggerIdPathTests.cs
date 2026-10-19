using System;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Every <c>@id</c> takes a path (spec 2026-10-19-id-path §4.1): the first segment is found lexically, as
    /// before; each further segment in the previous control's id table — the rule <c>Get("a/b")</c> and
    /// <c>&lt;Add into="#a/b"&gt;</c> already follow.
    /// </summary>
    public class TriggerIdPathTests
    {
        private const string Templates =
            "<Template name='IconCell'><Frame width='44' height='44'>" +
            "<Btn id='btn' anchor='stretch'>x</Btn><Frame id='badge' anchor='top-right' size='8x8'/></Frame></Template>" +
            "<Template name='Setting'><Frame width='120' height='40'><Toggle id='t' anchor='stretch'>x</Toggle></Frame></Template>";

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
        public void ResolveId_walks_into_a_template_instance()
        {
            var s = Open("<IconCell id='slot' anchor='top-left'/><Trigger id='tr' on='manual'><Frame height='10'/></Trigger>");

            var found = TriggerSourceResolver.ResolveId(s.Get<Trigger>("tr"), "slot/btn", "click");

            Assert.AreSame(s.Get("slot/btn"), found, "the same control Get(\"slot/btn\") returns");
        }

        [Test]
        public void The_first_segment_is_found_lexically()
        {
            // Two instances, each with a trigger pointing at 'inner/btn': each finds its own.
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + Templates +
                                 "<Template name='Pair'><Frame width='100' height='50'>" +
                                 "<IconCell id='inner' anchor='top-left'/>" +
                                 "<Trigger id='tr' on='click@inner/btn'><Frame height='10'/></Trigger></Frame></Template>" +
                                 "<Screen name='S'><Pair id='p1' anchor='top-left'/><Pair id='p2' anchor='top-right'/></Screen></PromptUGUI>");
            var s = UI.Open("S");
            int a = 0, b = 0;
            using var _1 = s.Get<Trigger>("p1/tr").OnFire.Subscribe(_ => a++);
            using var _2 = s.Get<Trigger>("p2/tr").OnFire.Subscribe(_ => b++);

            Click(s.Get<Btn>("p2/inner/btn"));

            Assert.AreEqual(0, a);
            Assert.AreEqual(1, b);
        }

        [Test]
        public void A_missing_segment_names_it_and_lists_what_is_there()
        {
            var s = Open("<IconCell id='slot' anchor='top-left'/><Trigger id='tr' on='manual'><Frame height='10'/></Trigger>");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                TriggerSourceResolver.ResolveId(s.Get<Trigger>("tr"), "slot/nope", "click"));

            StringAssert.Contains("'nope'", ex.Message);
            StringAssert.Contains("'slot'", ex.Message);
            StringAssert.Contains("btn", ex.Message, "the ids that are there");
        }

        [Test]
        public void A_click_trigger_follows_a_Btn_inside_an_instance()
        {
            var s = Open("<IconCell id='slot' anchor='top-left'/><Trigger id='tr' on='click@slot/btn'><Frame height='10'/></Trigger>");
            var fired = 0;
            using var _ = s.Get<Trigger>("tr").OnFire.Subscribe(__ => fired++);

            Click(s.Get<Btn>("slot/btn"));

            Assert.AreEqual(1, fired);
        }

        [Test]
        public void A_Show_follows_a_Toggle_inside_an_instance()
        {
            var s = Open("<Setting id='set' anchor='top-left'/>" +
                         "<Show on='checked@set/t'><Frame id='panel' anchor='bottom-left' size='10x10'/></Show>");
            Assume.That(!s.Get<Frame>("panel").GameObject.activeInHierarchy);

            s.Get<Toggle>("set/t").IsOn = true;

            Assert.IsTrue(s.Get<Frame>("panel").GameObject.activeInHierarchy);
        }

        [Test]
        public void A_popover_opens_from_a_Btn_inside_an_instance()
        {
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
            var s = Open("<IconCell id='slot' anchor='top-left'/>" +
                         "<Popover id='pop' on='click@slot/btn' transition='0'><Frame width='50' height='20'/></Popover>");
            var pop = s.Get<Popover>("pop");

            Click(s.Get<Btn>("slot/btn"));

            Assert.IsTrue(pop.IsOpen);
            Assert.AreSame(s.Get("slot/btn"), pop.Anchor, "a path names the button itself: it is the anchor");
        }
    }
}
