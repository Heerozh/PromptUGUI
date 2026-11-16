using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>Waiting for the server: <c>Defer</c> / <c>Accept(apply)</c> / <c>Reject</c> (spec 2026-11-16 §7.10).</summary>
    public class DragDropDeferTests : DragDropTestBase
    {
        private Recorder _rec;
        private DropEvent _pending;
        private IDisposable _deferSub;

        public override void SetUp()
        {
            base.SetUp();
            UseGeometricHits();
            _rec = new Recorder();
            _deferSub = UI.Drag.OnDrop.Subscribe(e =>
            {
                e.Defer();
                _pending = e;
            });
        }

        public override void TearDown()
        {
            _deferSub?.Dispose();
            _rec?.Dispose();
            _pending = null;
            base.TearDown();
        }

        private const string Three =
            "<HStack id='row' anchor='top-left' height='100' spacing='10'>" +
            "  <Frame id='slotA' width='100' drop='unit'><Frame id='a' anchor='stretch' drag='unit'/></Frame>" +
            "  <Frame id='slotB' width='100' drop='unit'><Frame id='b' anchor='stretch' drag='unit'/></Frame>" +
            "  <Frame id='slotC' width='100' drop='unit'/>" +
            "</HStack>";

        [Test]
        public void Defer_parks_the_source_over_its_target_and_blocks_new_lifts()
        {
            var s = Open(Three);
            var a = s.Get<Frame>("a");

            DragOnto(a, s.Get<Frame>("slotB"));

            Assert.IsNotNull(_pending);
            Assert.IsTrue(_pending.IsPending);
            Assert.IsTrue(UI.Drag.IsPending);
            Assert.IsFalse(UI.Drag.IsDragging);
            Assert.IsNotNull(LiftCanvasOf(a), "still lifted");
            var parked = ScreenOf(a);
            Assert.IsTrue(RectTransformUtility.RectangleContainsScreenPoint(Host(s.Get<Frame>("slotB")), parked, null),
                "parked over the target");
            var b = TargetOf(s.Get<Frame>("slotB"));
            Assert.IsTrue(b.IsPending);
            Assert.IsTrue(b.IsOver);
            Assert.IsFalse(TargetOf(s.Get<Frame>("slotC")).IsArmed, "the others let go");
            Assert.IsEmpty(_rec.Ends, "not over yet");

            Lift(s.Get<Frame>("b"));
            Assert.IsNull(LiftCanvasOf(s.Get<Frame>("b")), "nothing else lifts while a drop waits");
        }

        // Sources straight in a layout group, so taking one out of the flow leaves a placeholder.
        private const string InFlow =
            "<HStack id='row' anchor='top-left' height='100' spacing='10'>" +
            "  <Frame id='a' width='100' drag='unit'/><Frame id='b' width='100' drag='unit'/>" +
            "</HStack>" +
            "<Frame id='slotB' anchor='top-left' size='100x100' margin='150,_,_,10' drop='unit'/>";

        [Test]
        public void Accept_restores_the_structure_then_runs_apply_then_lands()
        {
            var s = Open(InFlow);
            var a = s.Get<Frame>("a");
            var receives = 0;
            using var rcv = TargetOf(s.Get<Frame>("slotB")).OnReceive.Subscribe(_ => receives++);
            DragOnto(a, s.Get<Frame>("slotB"));
            Assert.IsNotNull(Placeholder(a), "parked: still out of the flow");

            var applied = false;
            _pending.Accept(() =>
            {
                Assert.IsNull(Placeholder(a), "structure back before apply");
                Assert.IsFalse(Host(a).GetComponent<UnityEngine.UI.LayoutElement>().ignoreLayout, "back in the flow");
                applied = true;
            });

            Assert.IsTrue(applied);
            Assert.IsFalse(UI.Drag.IsPending);
            Assert.IsFalse(_pending.IsPending);
            Assert.AreEqual(1, receives);
            Assert.AreEqual(DragResult.Dropped, _rec.Ends.Single().Result);
            Assert.IsFalse(TargetOf(s.Get<Frame>("slotB")).IsPending);
        }

        [Test]
        public void Reject_sends_it_home()
        {
            var s = Open(Three);
            var a = s.Get<Frame>("a");
            var row = (RectTransform)s.Get<HStack>("row").RectTransform;
            UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(row);
            var home = Host(a).anchoredPosition;
            DragOnto(a, s.Get<Frame>("slotB"));

            _pending.Reject();

            Assert.IsFalse(UI.Drag.IsPending);
            Assert.AreEqual(DragResult.Returned, _rec.Ends.Single().Result);
            Assert.AreEqual(home, Host(a).anchoredPosition);
            Assert.IsNull(LiftCanvasOf(a));
        }

        [Test]
        public void A_push_under_the_source_lets_go_of_the_park_but_the_drop_still_waits()
        {
            var s = Open("<ScrollList id='list' anchor='top-left' size='330x110' columns='3' cellSize='100x100' spacing='10' itemTemplate='Slot'/>",
                "<Template name='Slot'><Frame drop='unit'><Frame id='card' anchor='stretch' drag='unit'/></Frame></Template>");
            var list = s.Get<ScrollList>("list");
            var data = new ReactiveProperty<IReadOnlyList<int>>(new[] { 0, 1, 2 });
            var rows = new List<IControl>();
            list.BindItems(data, (IControl r, int _) => { if (!rows.Contains(r)) rows.Add(r); });
            Canvas.ForceUpdateCanvases();
            var card = rows[0].Get<Frame>("card");
            DragOnto(card, rows[1]);
            Assert.IsNotNull(LiftCanvasOf(card), "parked");

            data.Value = new[] { 0, 1, 2 };   // an unrelated refresh arrives while waiting

            Assert.IsNull(Placeholder(card), "a push never runs with a placeholder in the list");
            Assert.IsNull(LiftCanvasOf(card));
            Assert.IsTrue(UI.Drag.IsPending, "still waiting for the answer");

            var applied = false;
            _pending.Accept(() => applied = true);
            Assert.IsTrue(applied);
            Assert.AreEqual(DragResult.Dropped, _rec.Ends.Single().Result);
        }

        [Test]
        public void Losing_the_source_ends_the_wait_and_a_late_Accept_still_applies()
        {
            var s = Open(Three);
            var a = s.Get<Frame>("a");
            DragOnto(a, s.Get<Frame>("slotB"));

            a.Dispose();
            Tick();

            Assert.IsFalse(UI.Drag.IsPending);
            Assert.AreEqual(DragResult.Cancelled, _rec.Ends.Single().Result);
            var applied = false;
            _pending.Accept(() => applied = true);
            Assert.IsTrue(applied, "confirmed data is never dropped on the floor");
            Assert.AreEqual(1, _rec.Ends.Count);
        }

        [Test]
        public void Code_cancel_ends_the_wait()
        {
            var s = Open(Three);
            var a = s.Get<Frame>("a");
            DragOnto(a, s.Get<Frame>("slotB"));

            UI.Drag.Cancel();

            Assert.IsFalse(UI.Drag.IsPending);
            Assert.AreEqual(DragResult.Cancelled, _rec.Ends.Single().Result);
            Assert.IsNull(Placeholder(a));
            Assert.IsNull(LiftCanvasOf(a));
        }

        [Test]
        public void An_unanswered_drop_times_out()
        {
            UI.Drag.PendingTimeout = 1f;
            var s = Open(Three);
            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));

            Tick(0.6f);
            Assert.IsTrue(UI.Drag.IsPending);
            LogAssert.Expect(LogType.Warning, new Regex("PendingTimeout"));
            Tick(0.6f);

            Assert.IsFalse(UI.Drag.IsPending);
            Assert.AreEqual(DragResult.Cancelled, _rec.Ends.Single().Result);
        }

        [Test]
        public void A_second_answer_only_applies()
        {
            var s = Open(Three);
            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));
            _pending.Accept();

            var second = false;
            _pending.Accept(() => second = true);
            _pending.Reject();

            Assert.IsTrue(second);
            Assert.AreEqual(DragResult.Dropped, _rec.Ends.Single().Result);
        }

        [Test]
        public void Defer_is_only_legal_inside_the_drop_callback()
        {
            var s = Open(Three);
            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));
            _pending.Accept();

            Assert.Throws<InvalidOperationException>(() => _pending.Defer());
        }

        [Test]
        public void Reject_beats_Defer_and_Accept_inside_the_callback_lands_at_once()
        {
            _deferSub.Dispose();
            var s = Open(Three);
            using (UI.Drag.OnDrop.Subscribe(e => { e.Defer(); e.Reject(); }))
                DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));
            Assert.IsFalse(UI.Drag.IsPending);
            Assert.AreEqual(DragResult.Returned, _rec.Ends.Last().Result);

            var applied = false;
            using (UI.Drag.OnDrop.Subscribe(e => { e.Defer(); e.Accept(() => applied = true); }))
                DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotC"));
            Assert.IsTrue(applied);
            Assert.IsFalse(UI.Drag.IsPending);
            Assert.AreEqual(DragResult.Dropped, _rec.Ends.Last().Result);
        }
    }
}
