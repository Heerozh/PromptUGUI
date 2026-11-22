using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using static PromptUGUI.Tests.EditMode.Controls.SfxTestKit;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// Drag and drop cues (spec 2026-11-23-ui-sfx-cues §7.11): lift; land when it goes in (the target's sfx first), at the
    /// server's Accept when deferred; return when it goes home from the user's release, a Reject or Escape. A code or
    /// system cancel, a timeout and the moment a deferred drop is let go raise nothing.
    /// </summary>
    public class SfxDragTests : DragDropTestBase
    {
        private List<SfxCue> _cues;
        private IDisposable _dropSub;

        public override void SetUp()
        {
            base.SetUp();
            UseGeometricHits();
            _cues = Record();
        }

        public override void TearDown()
        {
            _dropSub?.Dispose();
            UI.Variants.Set("mobile", false);
            base.TearDown();
        }

        private const string Slots =
            "<Frame id='slotA' anchor='top-left' size='100x100' margin='10,_,_,10' drop='unit'>" +
            "  <Frame id='a' anchor='stretch' drag='unit' {0}/>" +
            "</Frame>" +
            "<Frame id='slotB' anchor='top-left' size='100x100' margin='10,_,_,210' drop='unit' {1}/>";

        private static PromptUGUI.Application.Screen OpenSlots(string aSfx = "", string bSfx = "") =>
            Open(string.Format(Slots, aSfx, bSfx));

        private void OnDrop(Action<DropEvent> answer) => _dropSub = UI.Drag.OnDrop.Subscribe(answer);

        [Test]
        public void Lifting_cues_lift_before_OnLift()
        {
            var s = OpenSlots();
            var order = new List<string>();
            UI.Sfx.OnCue.Subscribe(c => order.Add(c.Key));
            UI.Drag.OnLift.Subscribe(_ => order.Add("lifted"));

            Lift(s.Get<Frame>("a"));

            CollectionAssert.AreEqual(new[] { "drag.lift", "lifted" }, order);
            Assert.AreSame(s.Get<Frame>("a"), _cues.Single().Source);
        }

        [Test]
        public void Going_into_a_target_cues_land_and_the_target_is_looked_up_first()
        {
            var s = OpenSlots("sfx='land:place'", "sfx='land:equip'");

            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));

            Assert.AreEqual("[drag.lift→drag.lift, drag.land→equip]", Describe(_cues));
            Assert.AreSame(s.Get<Frame>("a"), _cues[1].Source);
            Assert.AreSame(s.Get<Frame>("slotB"), _cues[1].Target);
        }

        [Test]
        public void Without_a_target_key_the_source_decides_the_landing()
        {
            var s = OpenSlots("sfx='land:place'");
            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));
            Assert.AreEqual("drag.land→place", $"{_cues[1].Kind}.{_cues[1].Action}→{_cues[1].Key}");
        }

        [Test]
        public void Letting_go_over_nothing_or_a_reject_cues_return()
        {
            var s = OpenSlots();
            var a = s.Get<Frame>("a");
            var src = SourceOf(a);
            var e = Lift(a);
            Move(src, e, ScreenOf(a, new Vector2(0f, -400f)));
            Tick();
            End(src, e);

            OnDrop(d => d.Reject());
            DragOnto(a, s.Get<Frame>("slotB"));

            Assert.AreEqual("[drag.lift→drag.lift, drag.return→drag.return, drag.lift→drag.lift, drag.return→drag.return]",
                Describe(_cues));
        }

        [Test]
        public void Escape_cues_return_and_code_or_system_cancels_cue_nothing()
        {
            var s = OpenSlots();
            var a = s.Get<Frame>("a");

            Lift(a);
            Assert.IsTrue(EscapeChain.Dispatch(EscapeKey.Escape, EscapeChain.NextTestPress()));
            Assert.AreEqual("[drag.lift→drag.lift, drag.return→drag.return]", Describe(_cues));
            _cues.Clear();

            Lift(a);
            UI.Drag.Cancel();
            Lift(a);
            UI.Variants.Set("mobile", true);   // a ReSolve ends the drag

            Assert.AreEqual("[drag.lift→drag.lift, drag.lift→drag.lift]", Describe(_cues));
        }

        [Test]
        public void A_deferred_drop_cues_at_the_answer_and_not_when_let_go()
        {
            var s = OpenSlots();
            DropEvent pending = null;
            OnDrop(d => { d.Defer(); pending = d; });

            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));
            Assert.AreEqual("[drag.lift→drag.lift]", Describe(_cues), "waiting for the answer");
            pending.Accept(null);
            Assert.AreEqual("[drag.lift→drag.lift, drag.land→drag.land]", Describe(_cues));

            _cues.Clear();
            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));
            pending.Reject();
            Assert.AreEqual("[drag.lift→drag.lift, drag.return→drag.return]", Describe(_cues));

            _cues.Clear();
            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));
            Tick(UI.Drag.PendingTimeout + 1f);   // the safety net gives it up
            pending.Accept(null);               // a late answer
            Assert.AreEqual("[drag.lift→drag.lift]", Describe(_cues));
        }

        [Test]
        public void A_source_disabled_during_the_hold_raises_no_lift()
        {
            var s = Open("<Frame id='x' anchor='top-left' size='50x50' drag='unit' dragHold='0.3s'/>");
            var x = s.Get<Frame>("x");
            var src = SourceOf(x);
            LiftGesture.PressedProbeForTests = _ => true;   // the finger stays down
            Press(src, ScreenOf(x));

            x.Interactable = false;
            src.TickForTests(0.4f);

            Assert.IsEmpty(_cues, Describe(_cues));
        }

        // ───── reorder ─────

        private const string RowTemplate = "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        private static (ScrollList list, List<IControl> rows) OpenList(string attrs)
        {
            var s = Open($"<Frame anchor='top-left' width='400' height='600'><ScrollList id='sl' width='150' height='200' itemTemplate='Row' reorder='true' {attrs}/></Frame>",
                         RowTemplate);
            var list = s.Get<ScrollList>("sl");
            var rows = new List<IControl>();
            list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b", "c" }), (IControl slot, string text) => rows.Add(slot));
            Canvas.ForceUpdateCanvases();
            return (list, rows);
        }

        private static ReorderDriver DriverOf(ScrollList sl) =>
            ((RectTransform)sl.GameObject.transform.Find("Viewport/Content")).GetComponent<ReorderDriver>();

        // Press, begin and move a row by dy canvas units along the list; End unless told not to.
        private static PointerEventData DragRow(ScrollList list, IControl row, float dy, bool release = true)
        {
            var d = DriverOf(list);
            var rt = (row as Control)?.LayoutHost ?? row.RectTransform;
            var content = (RectTransform)rt.parent;
            var p0 = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            var w = content.TransformVector(new Vector3(0f, dy, 0f));
            var e = new PointerEventData(EventSystem.current)
            {
                position = p0, pressPosition = p0, button = PointerEventData.InputButton.Left, pointerDrag = d.gameObject,
                eligibleForClick = true,
            };
            ((IInitializePotentialDragHandler)d).OnInitializePotentialDrag(e);
            ((IBeginDragHandler)d).OnBeginDrag(e);
            var p1 = p0 + new Vector2(w.x, w.y);
            e.delta = p1 - e.position;
            e.position = p1;
            ((IDragHandler)d).OnDrag(e);
            if (release) ((IEndDragHandler)d).OnEndDrag(e);
            return e;
        }

        [Test]
        public void A_reorder_cues_lift_then_land_before_OnReordered_and_the_list_is_looked_up()
        {
            var (list, rows) = OpenList("sfx='land:slot_in'");
            var order = new List<string>();
            UI.Sfx.OnCue.Subscribe(c => order.Add(c.Key));
            list.OnReordered.Subscribe(_ => order.Add("reordered"));

            DragRow(list, rows[0], -70f);   // two rows down

            CollectionAssert.AreEqual(new[] { "reorder.lift", "slot_in", "reordered" }, order);
            Assert.AreSame(rows[0], _cues[0].Source);
        }

        [Test]
        public void Putting_a_row_back_in_its_place_cues_return()
        {
            var (list, rows) = OpenList("");
            DragRow(list, rows[0], 0f);
            Assert.AreEqual("[reorder.lift→reorder.lift, reorder.return→reorder.return]", Describe(_cues));
        }

        [Test]
        public void A_push_during_a_reorder_cancels_it_silently()
        {
            var (list, rows) = OpenList("");
            var e = DragRow(list, rows[0], -70f, release: false);

            list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "x", "y" }), (IControl slot, string text) => { });
            ((IEndDragHandler)DriverOf(list)).OnEndDrag(e);   // the rest of the gesture goes nowhere

            Assert.AreEqual("[reorder.lift→reorder.lift]", Describe(_cues));
        }
    }
}
