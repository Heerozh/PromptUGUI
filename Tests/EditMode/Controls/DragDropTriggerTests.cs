using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>The hooks (spec 2026-11-16-drag-drop §5.2–§5.4): lift / drop on sources, drop-* / receive on targets.</summary>
    public class DragDropTriggerTests : DragDropTestBase
    {
        public override void SetUp()
        {
            base.SetUp();
            UseGeometricHits();
        }

        private sealed class Counter : IDisposable
        {
            public int Value;
            private readonly IDisposable _sub;
            public Counter(Trigger t) => _sub = t.OnFire.Subscribe(_ => Value++);
            public void Dispose() => _sub.Dispose();
        }

        [Test]
        public void Lift_and_drop_hooks_inside_a_source_fire()
        {
            var s = Open(
                "<Frame id='home' anchor='top-left' size='100x100' margin='10,_,_,10'>" +
                "  <Frame id='a' anchor='stretch' drag='unit'><Trigger id='up' on='lift'/><Trigger id='down' on='drop'/></Frame>" +
                "</Frame>" +
                "<Frame id='slotB' anchor='top-left' size='100x100' margin='10,_,_,210' drop='unit'/>");
            using var up = new Counter(s.Get<Trigger>("up"));
            using var down = new Counter(s.Get<Trigger>("down"));

            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));

            Assert.AreEqual(1, up.Value);
            Assert.AreEqual(1, down.Value);
        }

        [Test]
        public void A_lift_hook_takes_the_look_over_and_a_drop_only_hook_does_not()
        {
            var s = Open(
                "<Frame id='a' anchor='top-left' size='50x50' drag='unit'><Trigger on='lift'/></Frame>" +
                "<Frame id='b' anchor='top-left' size='50x50' margin='0,_,_,100' drag='unit'><Trigger on='drop'/></Frame>");

            var a = s.Get<Frame>("a");
            var ea = Lift(a);
            Assert.AreEqual(Vector3.one, Host(a).localScale, "the author's lift hook owns the look");
            End(SourceOf(a), ea);

            var b = s.Get<Frame>("b");
            Lift(b);
            Assert.AreEqual(LiftLook.Scale, Host(b).localScale.x, 1e-4f, "default look");
        }

        [Test]
        public void The_default_look_is_relative_and_comes_back()
        {
            var s = Open("<Frame id='a' anchor='top-left' size='50x50' drag='unit'/>");
            var a = s.Get<Frame>("a");
            Host(a).localScale = new Vector3(2f, 2f, 1f);
            var e = Lift(a);
            Assert.AreEqual(2f * LiftLook.Scale, Host(a).localScale.x, 1e-4f);

            End(SourceOf(a), e);

            Assert.AreEqual(2f, Host(a).localScale.x, 1e-4f);
        }

        [Test]
        public void A_Show_in_the_slot_follows_lift_at_the_source()
        {
            var s = Open(
                "<Frame id='slotA' anchor='top-left' size='100x100' margin='10,_,_,10'>" +
                "  <Show id='hole' on='lift@a'><Frame anchor='stretch'/></Show>" +
                "  <Frame id='a' anchor='stretch' drag='unit'/>" +
                "</Frame>");
            var hole = s.Get<Show>("hole");
            Assert.IsFalse(hole.GameObject.activeSelf);

            var e = Lift(s.Get<Frame>("a"));
            Assert.IsTrue(hole.GameObject.activeSelf);

            End(SourceOf(s.Get<Frame>("a")), e);
            Assert.IsFalse(hole.GameObject.activeSelf);
        }

        [Test]
        public void In_a_list_row_the_nearer_of_source_and_row_wins()
        {
            var s = Open("<ScrollList id='list' anchor='top-left' size='200x300' itemTemplate='Row'/>",
                "<Template name='Row'><Frame height='60'><Trigger id='rowHook' on='lift'/>" +
                "<Frame id='card' anchor='stretch' drag='unit'><Trigger id='cardHook' on='lift'/></Frame></Frame></Template>");
            var list = s.Get<ScrollList>("list");
            var rows = new List<IControl>();
            list.BindItems(Observable.Return<IReadOnlyList<int>>(new[] { 0 }), (IControl r, int _) => rows.Add(r));
            using var row = new Counter(rows[0].Get<Trigger>("rowHook"));
            using var card = new Counter(rows[0].Get<Trigger>("cardHook"));

            Lift(rows[0].Get<Frame>("card"));

            Assert.AreEqual(1, card.Value, "the card's own hook");
            Assert.AreEqual(0, row.Value, "the row's hook belongs to a reorder of the row");
        }

        [Test]
        public void Target_hooks_follow_the_drag()
        {
            var s = Open(
                "<Frame id='home' anchor='top-left' size='100x100' margin='10,_,_,10'><Frame id='a' anchor='stretch' drag='unit'/></Frame>" +
                "<Frame id='slotB' anchor='top-left' size='100x100' margin='10,_,_,210' drop='unit'>" +
                "  <Trigger id='armed' on='drop-armed'/><Trigger id='disarmed' on='drop-disarmed'/>" +
                "  <Trigger id='over' on='drop-over'/><Trigger id='out' on='drop-out'/><Trigger id='recv' on='receive'/>" +
                "</Frame>");
            using var armed = new Counter(s.Get<Trigger>("armed"));
            using var disarmed = new Counter(s.Get<Trigger>("disarmed"));
            using var over = new Counter(s.Get<Trigger>("over"));
            using var outs = new Counter(s.Get<Trigger>("out"));
            using var recv = new Counter(s.Get<Trigger>("recv"));
            Assert.AreEqual(0, disarmed.Value + outs.Value, "how it starts is established, not fired");

            var a = s.Get<Frame>("a");
            var src = SourceOf(a);
            var e = Lift(a);
            Assert.AreEqual(1, armed.Value);

            Move(src, e, ScreenOf(s.Get<Frame>("slotB")));
            Tick();
            Assert.AreEqual(1, over.Value);

            Move(src, e, ScreenOf(s.Get<Frame>("home")));
            Tick();
            Assert.AreEqual(1, outs.Value, "moved off");

            Move(src, e, ScreenOf(s.Get<Frame>("slotB")));
            Tick();
            End(src, e);

            Assert.AreEqual(2, over.Value);
            Assert.AreEqual(1, recv.Value);
            Assert.AreEqual(2, outs.Value, "the drop ends the hover");
            Assert.AreEqual(1, disarmed.Value);
        }

        [Test]
        public void Show_blocks_follow_the_target_states_and_may_overlap()
        {
            var s = Open(
                "<Frame id='home' anchor='top-left' size='100x100' margin='10,_,_,10'><Frame id='a' anchor='stretch' drag='unit'/></Frame>" +
                "<Frame id='slotB' anchor='top-left' size='100x100' margin='10,_,_,210' drop='unit'>" +
                "  <Show id='sArmed' on='drop-armed'><Frame/></Show><Show id='sOver' on='drop-over'><Frame/></Show>" +
                "  <Show id='sOut' on='drop-out'><Frame/></Show>" +
                "</Frame>" +
                "<Frame id='slotC' anchor='top-left' size='100x100' margin='10,_,_,410' drop='unit'>" +
                "  <Show id='sDeny' on='drop-deny'><Frame/></Show>" +
                "</Frame>");
            s.Get<Frame>("slotC").CanDrop = _ => false;
            bool On(string id) => s.Get<Show>(id).GameObject.activeSelf;
            Assert.IsFalse(On("sArmed"));
            Assert.IsTrue(On("sOut"));

            var a = s.Get<Frame>("a");
            var src = SourceOf(a);
            var e = Lift(a);
            Assert.IsTrue(On("sArmed"));

            Move(src, e, ScreenOf(s.Get<Frame>("slotB")));
            Tick();
            Assert.IsTrue(On("sArmed") && On("sOver"), "armed and over at once");
            Assert.IsFalse(On("sOut"));

            Move(src, e, ScreenOf(s.Get<Frame>("slotC")));
            Tick();
            Assert.IsTrue(On("sDeny"));
            Assert.IsTrue(On("sOut"));

            UI.Drag.Cancel();
            Assert.IsFalse(On("sArmed") || On("sOver") || On("sDeny"));
        }

        [Test]
        public void Receive_is_not_a_state()
        {
            Assert.That(
                () => Open("<Frame id='t' anchor='top-left' size='50x50' drop='unit'><Show on='receive'><Frame/></Show></Frame>"),
                Throws.InstanceOf<Exception>().With.Message.Contains("receive"));
        }

        [Test]
        public void A_target_hook_needs_a_target_and_a_lift_hook_a_source()
        {
            Assert.That(() => Open("<Frame id='x' size='10x10'><Trigger on='drop-over'/></Frame>"),
                Throws.InstanceOf<Exception>().With.Message.Contains("drop="));
            UI.ResetForTests();
            Assert.That(() => Open("<Frame id='x' size='10x10'><Trigger on='lift'/></Frame>"),
                Throws.InstanceOf<Exception>().With.Message.Contains("drag source"));
        }

        [Test]
        public void An_id_names_the_target_from_outside()
        {
            var s = Open(
                "<Frame id='home' anchor='top-left' size='100x100' margin='10,_,_,10'><Frame id='a' anchor='stretch' drag='unit'/></Frame>" +
                "<Frame id='slotB' anchor='top-left' size='100x100' margin='10,_,_,210' drop='unit'/>" +
                "<Show id='hint' on='drop-armed@slotB'><Frame/></Show>");

            var e = Lift(s.Get<Frame>("a"));

            Assert.IsTrue(s.Get<Show>("hint").GameObject.activeSelf);
            End(SourceOf(s.Get<Frame>("a")), e);
            Assert.IsFalse(s.Get<Show>("hint").GameObject.activeSelf);
        }
    }
}
