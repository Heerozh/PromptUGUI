using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Application.Modals;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using R3;
using UnityEngine;
using PuguiScreen = PromptUGUI.Application.Screen;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Opening and closing a <c>&lt;Popover&gt;</c> (spec 2026-10-19 §4.3 / §4.11): its <c>on="click@id"</c>
    /// trigger toggles it, wherever the trigger is declared; C# opens it on any control of the Screen or at
    /// a point; what cannot be opened is refused quietly, what makes no sense throws.
    /// </summary>
    public class PopoverOpenCloseTests
    {
        private const string Body = @"
  <Btn id='a' anchor='center' size='40x40'>A</Btn>
  <Btn id='b' anchor='top-left' size='40x40'>B</Btn>
  <Popover id='p' on='click@a'><Btn id='inner' width='60' height='30'>I</Btn></Popover>
  <Popover id='m'><Frame width='50' height='20'/><Trigger id='bare' on='expand'><Frame width='10' height='10'/></Trigger></Popover>
  <Trigger id='watch' on='expand@m'/>";

        private PuguiScreen _s;

        [SetUp]
        public void SetUp()
        {
            UI.ResetForTests();
            PopupBounds.BoundsOverride = () => new Rect(-1000f, -1000f, 2000f, 2000f);
        }

        [TearDown]
        public void TearDown()
        {
            UI.ResetForTests();
            MessageBox.XmlSrc = "PromptUGUI/Modals/MessageBox.ui";
        }

        private PuguiScreen Open(string body = Body, string prelude = "")
        {
            UI.LoadDocument("t", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'>" + prelude
                                 + "<Screen name='S'>" + body + "</Screen></PromptUGUI>");
            return _s = UI.Open("S");
        }

        private Btn Btn(string id) => _s.Get<Btn>(id);
        private Popover Pop(string id) => _s.Get<Popover>(id);

        private static List<PopoverCloseReason> Closes(Popover p)
        {
            var list = new List<PopoverCloseReason>();
            p.OnClosed.Subscribe(list.Add);
            return list;
        }

        // ── on="click@id" ─────────────────────────────────────────────────────────────────

        [Test]
        public void on_click_toggles_from_the_trigger()
        {
            Open();
            var p = Pop("p");
            var closes = Closes(p);

            Btn("a").SimulateClick();
            Assert.IsTrue(p.IsOpen);
            Assert.AreSame(Btn("a"), p.Anchor, "the trigger is the anchor");

            Btn("a").SimulateClick();
            Assert.IsFalse(p.IsOpen);
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.Trigger }, closes);
        }

        [Test]
        public void The_trigger_may_come_before_or_after_the_popover()
        {
            Open("<Popover id='p' on='click@late'><Frame width='10' height='10'/></Popover>"
                 + "<Btn id='late' anchor='center' size='40x40'>L</Btn>");
            Btn("late").SimulateClick();
            Assert.IsTrue(Pop("p").IsOpen, "declared after the popover, at Screen level");
        }

        [Test]
        public void A_trigger_in_the_same_row_template_resolves()
        {
            Open("<ScrollList id='l' anchor='center' size='300x300' itemTemplate='Row'/>",
                @"<Template name='Row'><Frame height='40'>
                    <Popover id='rp' on='click@more'><Frame width='10' height='10'/></Popover>
                    <Btn id='more' anchor='center' size='30x30'>…</Btn>
                  </Frame></Template>");
            var list = _s.Get<ScrollList>("l");
            using (list.BindItems(Observable.Return<IReadOnlyList<int>>(new[] { 1, 2 }), (IControl r, int i) => { }))
            {
                var row = list.Slots[1];
                row.Get<Btn>("more").SimulateClick();
                Assert.IsTrue(row.Get<Popover>("rp").IsOpen);
                Assert.IsFalse(list.Slots[0].Get<Popover>("rp").IsOpen, "each row's popover answers its own button");
            }
        }

        [Test]
        public void A_trigger_inside_the_popover_or_not_a_Btn_is_an_error()
        {
            var inside = Assert.Throws<ParseException>(() =>
                Open("<Popover id='p' on='click@x'><Btn id='x'>X</Btn></Popover>"));
            StringAssert.Contains("inside", inside.Message);

            UI.ResetForTests();
            var notBtn = Assert.Throws<ParseException>(() =>
                Open("<Frame id='f' size='10x10'/><Popover id='p' on='click@f'/>"));
            StringAssert.Contains("<Btn>", notBtn.Message);

            UI.ResetForTests();
            var grammar = Assert.Throws<ParseException>(() =>
                Open("<Btn id='a'>A</Btn><Popover id='p' on='hover-enter@a'/>"));
            StringAssert.Contains("PUI-POPOVER-ON", grammar.Message);
        }

        [Test]
        public void A_variant_can_rewire_on()
        {
            Open("<Btn id='a' anchor='center' size='40x40'>A</Btn><Btn id='b' anchor='top-left' size='40x40'>B</Btn>"
                 + "<Popover id='p' on='click@a' on.v='click@b'><Frame width='10' height='10'/></Popover>");
            UI.Variants.Set("v", true);

            Btn("a").SimulateClick();
            Assert.IsFalse(Pop("p").IsOpen, "the variant moved the trigger away from a");
            Btn("b").SimulateClick();
            Assert.IsTrue(Pop("p").IsOpen);
            Assert.AreSame(Btn("b"), Pop("p").Anchor);
        }

        // ── C# ────────────────────────────────────────────────────────────────────────────

        [Test]
        public void Open_without_a_trigger_throws()
        {
            Open();
            Assert.Throws<InvalidOperationException>(() => Pop("m").Open());
            Assert.Throws<InvalidOperationException>(() => Pop("m").Toggle());
        }

        [Test]
        public void Open_on_another_anchor_reopens_there()
        {
            Open();
            var m = Pop("m");
            var closes = Closes(m);
            var opens = 0;
            m.OnOpened.Subscribe(_ => opens++);

            m.Open(Btn("a"));
            m.Open(Btn("a"));
            Assert.AreEqual(1, opens, "the same anchor again changes nothing");

            m.Open(Btn("b"));
            Assert.IsTrue(m.IsOpen);
            Assert.AreSame(Btn("b"), m.Anchor);
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.Code }, closes);
            Assert.AreEqual(2, opens);
        }

        [Test]
        public void Cross_screen_or_self_anchors_are_rejected()
        {
            Open();
            Assert.Throws<ArgumentNullException>(() => Pop("m").Open(null));
            Assert.Throws<ArgumentException>(() => Pop("p").Open(Btn("inner")), "an anchor inside the popover");

            UI.LoadDocument("t2", "<?xml version='1.0' encoding='utf-8'?><PromptUGUI version='1'><Screen name='S2'>"
                                  + "<Btn id='x'>X</Btn></Screen></PromptUGUI>");
            var other = UI.Open("S2").Get<Btn>("x");
            Assert.Throws<ArgumentException>(() => Pop("m").Open(other), "an anchor of another Screen");
        }

        [Test]
        public void Nothing_happens_when_it_cannot_open()
        {
            Open();
            var m = Pop("m");

            m.Interactable = false;
            m.Open(Btn("a"));
            Assert.IsFalse(m.IsOpen, "not interactable");
            m.Interactable = true;

            Btn("b").Hidden = true;
            m.Open(Btn("b"));
            Assert.IsFalse(m.IsOpen, "the anchor is switched off");

            _ = MessageBox.Open("over it", MsgBtn.OK);
            m.Open(Btn("a"));
            Assert.IsFalse(m.IsOpen, "a dialog covers its layer");
        }

        [Test]
        public void Toggle_with_an_anchor()
        {
            Open();
            var m = Pop("m");
            m.Toggle(Btn("a"));
            Assert.IsTrue(m.IsOpen);
            m.Toggle(Btn("b"));
            Assert.IsTrue(m.IsOpen, "another anchor: reopened there");
            Assert.AreSame(Btn("b"), m.Anchor);
            m.Toggle(Btn("b"));
            Assert.IsFalse(m.IsOpen);
        }

        [Test]
        public void OpenAt_uses_a_point_and_reports_no_anchor()
        {
            Open();
            var m = Pop("m");
            m.OpenAt(new Vector2(100f, 100f));
            Assert.IsTrue(m.IsOpen);
            Assert.IsNull(m.Anchor);
        }

        // ── Hidden / Interactable / Dispose ───────────────────────────────────────────────

        [Test]
        public void Hidden_true_closes_and_false_does_nothing()
        {
            Open();
            var m = Pop("m");
            Assert.IsTrue(m.Hidden, "closed reads as hidden");
            m.Open(Btn("a"));
            Assert.IsFalse(m.Hidden);

            m.Hidden = true;
            Assert.IsFalse(m.IsOpen);
            m.Hidden = false;
            Assert.IsFalse(m.IsOpen, "showing needs an anchor: Open it");
            Assert.IsFalse(m.GameObject.activeSelf, "and the panel was not switched on bare");
        }

        [Test]
        public void Interactable_false_closes_it_as_Code()
        {
            Open();
            var m = Pop("m");
            var closes = Closes(m);
            m.Open(Btn("a"));
            m.Interactable = false;
            Assert.IsFalse(m.IsOpen);
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.Code }, closes);
        }

        [Test]
        public void Dispose_reports_HostGone()
        {
            Open();
            var m = Pop("m");
            var closes = Closes(m);
            m.Open(Btn("a"));
            m.Dispose();
            CollectionAssert.AreEqual(new[] { PopoverCloseReason.HostGone }, closes);
        }

        [Test]
        public void expand_and_collapse_triggers_fire()
        {
            Open();
            var m = Pop("m");
            var watched = 0;
            var inner = 0;
            _s.Get<Trigger>("watch").OnFire.Subscribe(_ => watched++);
            _s.Get<Trigger>("bare").OnFire.Subscribe(_ => inner++);

            m.Open(Btn("a"));

            Assert.AreEqual(1, watched, "expand@m from outside");
            Assert.AreEqual(1, inner, "a bare expand inside finds the popover");
        }

        // ── Re-entrancy (spec §4.11) ──────────────────────────────────────────────────────

        [Test]
        public void A_close_handler_that_opens_another_leaves_one_line()
        {
            Open(Body + "<Popover id='q'><Frame width='10' height='10'/></Popover>");
            var m = Pop("m");
            var q = Pop("q");
            m.Open(Btn("a"));
            m.OnClosed.Subscribe(_ => q.Open(Btn("b")));

            Btn("a").SimulateClick();   // opens p, which replaces m, whose handler opens q

            Assert.IsTrue(Pop("p").IsOpen);
            Assert.IsFalse(q.IsOpen, "q is not p's descendant: it cannot stay under it");
            Assert.AreEqual(1, PopupChain.Count);
        }

        [Test]
        public void A_replaced_handler_that_closes_the_newcomer_leaves_no_zombie()
        {
            Open();
            var m = Pop("m");
            var p = Pop("p");
            m.Open(Btn("b"));
            m.OnClosed.Subscribe(_ => p.Close());

            Btn("a").SimulateClick();

            Assert.IsFalse(p.IsOpen);
            Assert.IsTrue(PopupChain.IsEmpty, "a popup closed during its own opening is not left on the chain");
            Assert.IsFalse(p.GameObject.activeSelf);
        }
    }
}
