using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// The cancel matrix (spec 2026-11-16 §7.7): put back at once, <c>drop</c> raised, no <c>OnDrop</c>,
    /// <c>OnEnd(Cancelled)</c>, and the rest of that gesture goes nowhere.
    /// </summary>
    public class DragDropCancelTests : DragDropTestBase
    {
        private Recorder _rec;

        public override void SetUp()
        {
            base.SetUp();
            UseGeometricHits();
            _rec = new Recorder();
        }

        public override void TearDown()
        {
            _rec?.Dispose();
            base.TearDown();
        }

        private void AssertCancelled(IControl source, Vector2 home, DragSource src, PointerEventData e)
        {
            Assert.IsFalse(UI.Drag.IsDragging);
            Assert.AreEqual(DragResult.Cancelled, _rec.Ends.Single().Result);
            Assert.IsEmpty(_rec.Drops);
            if (source.GameObject == null) return;   // destroyed with its row
            Assert.IsNull(Placeholder(source));
            Assert.IsNull(LiftCanvasOf(source));
            Assert.AreEqual(home, Host(source).anchoredPosition);

            // The rest of the gesture is swallowed: it neither moves anything nor drops.
            Move(src, e, e.position + new Vector2(50f, 0f));
            End(src, e);
            Assert.AreEqual(home, Host(source).anchoredPosition);
            Assert.IsEmpty(_rec.Drops);
        }

        private (Frame a, DragSource src, PointerEventData e, Vector2 home) LiftA(PromptUGUI.Application.Screen s)
        {
            var a = s.Get<Frame>("a");
            var home = Host(a).anchoredPosition;
            var src = SourceOf(a);
            var e = Lift(a);
            Move(src, e, ScreenOf(s.Get<Frame>("slotB")));
            Tick();
            return (a, src, e, home);
        }

        [Test]
        public void A_ReSolve_cancels()
        {
            var s = Open(TwoSlots);
            var (a, src, e, home) = LiftA(s);

            s.ReSolve();

            AssertCancelled(a, home, src, e);
        }

        [Test]
        public void Hiding_disabling_or_turning_drag_off_cancels()
        {
            foreach (var how in new[] { "hidden", "interactable", "kind" })
            {
                UI.ResetForTests();
                UseGeometricHits();
                _rec.Dispose();
                _rec = new Recorder();
                var s = Open(TwoSlots);
                var (a, src, e, home) = LiftA(s);

                if (how == "hidden") a.Hidden = true;
                else if (how == "interactable") a.Interactable = false;
                else a.DragKind = null;
                Tick();
                if (how == "hidden") a.Hidden = false;

                AssertCancelled(a, home, src, e);
            }
        }

        [Test]
        public void Disposing_the_source_cancels()
        {
            var s = Open(TwoSlots);
            var (a, src, e, home) = LiftA(s);

            a.Dispose();
            Tick();

            AssertCancelled(a, home, src, e);
        }

        [Test]
        public void A_push_to_the_list_the_source_lives_in_cancels()
        {
            var s = Open("<ScrollList id='list' anchor='top-left' size='200x300' itemTemplate='Row'/>" +
                         "<Frame id='slotB' anchor='top-left' size='100x100' margin='10,_,_,300' drop='unit'/>",
                "<Template name='Row'><Frame height='60'><Frame id='card' anchor='stretch' drag='unit'/></Frame></Template>");
            var list = s.Get<ScrollList>("list");
            var data = new ReactiveProperty<IReadOnlyList<int>>(new[] { 0, 1, 2 });
            var rows = new List<IControl>();
            list.BindItems(data, (IControl r, int _) => { if (!rows.Contains(r)) rows.Add(r); });
            Canvas.ForceUpdateCanvases();
            var card = rows[1].Get<Frame>("card");
            var home = Host(card).anchoredPosition;
            var src = SourceOf(card);
            var e = Lift(card);
            Move(src, e, ScreenOf(s.Get<Frame>("slotB")));
            Tick();

            data.Value = new[] { 5, 6, 7 };

            AssertCancelled(card, home, src, e);
        }

        [Test]
        public void Closing_the_source_Screen_cancels()
        {
            var s = Open(TwoSlots);
            var (a, src, e, home) = LiftA(s);

            UI.Close("S");

            Assert.IsFalse(UI.Drag.IsDragging);
            Assert.AreEqual(DragResult.Cancelled, _rec.Ends.Single().Result);
            Assert.IsEmpty(_rec.Drops);
        }

        [Test]
        public void A_layer_opening_above_the_source_cancels()
        {
            var s = Open(TwoSlots);
            var (a, src, e, home) = LiftA(s);

            PopupChain.DismissBelow(1000);   // what a modal does as it opens

            AssertCancelled(a, home, src, e);
        }

        [Test]
        public void Escape_cancels_and_is_consumed()
        {
            var s = Open(TwoSlots);
            var (a, src, e, home) = LiftA(s);

            Assert.IsTrue(EscapeChain.Dispatch(EscapeKey.Escape, EscapeChain.NextTestPress()));

            AssertCancelled(a, home, src, e);
        }

        [Test]
        public void Code_can_cancel()
        {
            var s = Open(TwoSlots);
            var (a, src, e, home) = LiftA(s);

            UI.Drag.Cancel();

            AssertCancelled(a, home, src, e);
        }

        [Test]
        public void A_target_Screen_closing_only_takes_its_targets_away()
        {
            var s = Open(TwoSlots);
            var other = Open("<Frame id='far' anchor='top-left' size='100x100' margin='200,_,_,10' drop='unit'/>", name: "T");
            var a = s.Get<Frame>("a");
            var src = SourceOf(a);
            var e = Lift(a);
            Move(src, e, ScreenOf(other.Get<Frame>("far")));
            Tick();
            Assert.IsTrue(TargetOf(other.Get<Frame>("far")).IsOver);

            UI.Close("T");
            Tick();

            Assert.IsTrue(UI.Drag.IsDragging, "the drag goes on");
            Assert.IsNull(UI.Drag.Current.Target, "nothing is hovered any more");
            End(src, e);
            Assert.AreEqual(DragResult.Returned, _rec.Ends.Single().Result);
        }
    }
}
