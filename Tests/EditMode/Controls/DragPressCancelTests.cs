using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The drag-drop spec's §7.7 cancels hold while a source is still <b>pressed</b> — counting down its hold, or (hold 0)
    /// waiting for its first move — not only once it is lifted (spec 2026-11-23-input-correctness §2.2); a parked drop keeps
    /// its target armed and ends when its source's Screen starts closing (§2.3).
    /// </summary>
    public class DragPressCancelTests : DragDropTestBase
    {
        private readonly List<DragInfo> _lifts = new();
        private readonly List<DragInfo> _ends = new();
        private readonly List<IDisposable> _subs = new();

        public override void SetUp()
        {
            base.SetUp();
            UseGeometricHits();
            _lifts.Clear();
            _ends.Clear();
            _subs.Add(UI.Drag.OnLift.Subscribe(_lifts.Add));
            _subs.Add(UI.Drag.OnEnd.Subscribe(_ends.Add));
            LiftGesture.PressedProbeForTests = _ => true;   // the finger stays down
        }

        public override void TearDown()
        {
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            UI.Variants.Set("mobile", false);
            base.TearDown();
        }

        private const string Held = "<Frame id='x' anchor='top-left' size='60x60' margin='10,_,_,10' drag='unit' dragHold='0.3s'/>";

        private void AssertNothingLifted(string why)
        {
            Assert.IsFalse(UI.Drag.IsDragging, why);
            Assert.IsEmpty(_lifts, "no OnLift");
            Assert.IsEmpty(_ends, "no OnEnd either: nothing was lifted");
        }

        [Test]
        public void Disabled_during_the_hold_lifts_nothing()
        {
            var s = Open(Held);
            var x = s.Get<Frame>("x");
            var src = SourceOf(x);
            Press(src, ScreenOf(x));

            x.Interactable = false;
            src.TickForTests(0.4f);

            AssertNothingLifted("a source disabled while held is not picked up");
        }

        [Test]
        public void Disabled_while_still_with_no_hold_lifts_nothing_on_the_first_move()
        {
            var s = Open("<Frame id='x' anchor='top-left' size='60x60' margin='10,_,_,10' drag='unit'/>");
            var x = s.Get<Frame>("x");
            var src = SourceOf(x);
            var e = Press(src, ScreenOf(x));

            x.Interactable = false;
            src.TickForTests(0.016f);
            Begin(src, e);
            Move(src, e, ScreenOf(x, new Vector2(40f, 0f)));

            AssertNothingLifted("hold 0: the first move does not pick up a source disabled meanwhile");
        }

        [Test]
        public void A_push_to_its_list_during_the_hold_lifts_nothing()
        {
            var s = Open("<ScrollList id='sl' anchor='top-left' width='150' height='200' itemTemplate='Card'/>",
                         "<Template name='Card'><Frame height='40' drag='unit' dragHold='0.3s'><Text id='label'/></Frame></Template>");
            var list = s.Get<ScrollList>("sl");
            var rows = new List<IControl>();
            var items = new Subject<IReadOnlyList<string>>();
            list.BindItems(items, (IControl row, string t) => rows.Add(row));
            items.OnNext(new[] { "a", "b", "c" });
            Canvas.ForceUpdateCanvases();
            var row0 = rows[0];
            var src = SourceOf(row0);
            Press(src, ScreenOf(row0));

            items.OnNext(new[] { "z", "a", "b", "c" });   // the row under the finger now shows another item
            src.TickForTests(0.4f);

            AssertNothingLifted("the press ended with the push: never lift the row's new item");
        }

        [Test]
        public void A_layer_opening_above_during_the_hold_lifts_nothing()
        {
            var s = Open(Held);
            var x = s.Get<Frame>("x");
            var src = SourceOf(x);
            Press(src, ScreenOf(x));

            DragSession.OnLayerAbove(UI.Modal.SortingOrderBase);   // what a dialog opening sends
            src.TickForTests(0.4f);

            AssertNothingLifted("a dialog came up over it");
        }

        [Test]
        public void Its_Screen_starting_to_close_during_the_hold_lifts_nothing()
        {
            var s = Open(Held);
            var x = s.Get<Frame>("x");
            var src = SourceOf(x);
            Press(src, ScreenOf(x));

            DragSession.BeforeStructureChange(s.RootGameObject.transform);   // what a closing Screen sends as it seals input
            src.TickForTests(0.4f);

            AssertNothingLifted("its Screen is going");
        }

        [Test]
        public void A_ReSolve_during_the_hold_lifts_nothing()
        {
            var s = Open(Held);
            var x = s.Get<Frame>("x");
            var src = SourceOf(x);
            Press(src, ScreenOf(x));

            UI.Variants.Set("mobile", true);
            src.TickForTests(0.4f);

            AssertNothingLifted("a ReSolve cancels every gesture");
        }

        [Test]
        public void An_undisturbed_hold_still_lifts()
        {
            var s = Open(Held);
            var x = s.Get<Frame>("x");
            var src = SourceOf(x);
            Press(src, ScreenOf(x));
            src.TickForTests(0.4f);
            Assert.IsTrue(UI.Drag.IsDragging);
            Assert.AreEqual(1, _lifts.Count);
        }

        // ───── waiting for the answer ─────

        private const string Slots =
            "<Frame id='slotA' anchor='top-left' size='100x100' margin='10,_,_,10' drop='unit'>" +
            "  <Frame id='a' anchor='stretch' drag='unit'/>" +
            "</Frame>" +
            "<Frame id='slotB' anchor='top-left' size='100x100' margin='10,_,_,210' drop='unit'/>";

        [Test]
        public void A_parked_target_stays_armed_while_it_waits()
        {
            LiftGesture.PressedProbeForTests = null;
            var s = Open(Slots);
            DropEvent pending = null;
            _subs.Add(UI.Drag.OnDrop.Subscribe(d => { d.Defer(); pending = d; }));
            var b = TargetOf(s.Get<Frame>("slotB"));

            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));
            Assume.That(UI.Drag.IsPending, "precondition: waiting for the answer");

            Assert.IsTrue(b.IsArmed, "the target it waits over is still armed (only the others let go)");
            Assert.IsFalse(TargetOf(s.Get<Frame>("slotA")).IsArmed, "the others let go");
            pending.Accept(null);
            Assert.IsFalse(b.IsArmed, "disarmed when the wait ends");
        }

        [Test]
        public void Closing_the_source_Screen_ends_the_wait()
        {
            LiftGesture.PressedProbeForTests = null;
            var s = Open(Slots);
            _subs.Add(UI.Drag.OnDrop.Subscribe(d => d.Defer()));
            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));
            Assume.That(UI.Drag.IsPending, "precondition: waiting for the answer");

            UI.Close("S");

            Assert.IsFalse(UI.Drag.IsPending, "the wait ended when its source's Screen started closing");
            Assert.AreEqual(DragResult.Cancelled, _ends.Single().Result);
        }
    }
}
