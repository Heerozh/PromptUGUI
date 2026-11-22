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

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A touch the system cancels — a call, the notification shade, a system gesture, too many fingers — ends a drag or a
    /// reorder as a cancel, never as a release (spec 2026-11-23-input-correctness §2.1; drag-drop spec §7.7). The device
    /// answer is faked through <c>LiftGesture.CanceledProbeForTests</c>; PlayMode drives a real touchscreen.
    /// </summary>
    public class DragSystemCancelTests : DragDropTestBase
    {
        private readonly List<DragInfo> _ends = new();
        private readonly List<DropEvent> _drops = new();
        private IDisposable _endSub;
        private IDisposable _dropSub;

        public override void SetUp()
        {
            base.SetUp();
            UseGeometricHits();
            _ends.Clear();
            _drops.Clear();
            _endSub = UI.Drag.OnEnd.Subscribe(_ends.Add);
            _dropSub = UI.Drag.OnDrop.Subscribe(_drops.Add);
        }

        public override void TearDown()
        {
            _endSub?.Dispose();
            _dropSub?.Dispose();
            LiftGesture.CanceledProbeForTests = null;
            ReorderDriver.PressedProbeForTests = null;
            base.TearDown();
        }

        private const string Slots =
            "<Frame id='slotA' anchor='top-left' size='100x100' margin='10,_,_,10' drop='unit'>" +
            "  <Frame id='a' anchor='stretch' drag='unit'/>" +
            "</Frame>" +
            "<Frame id='slotB' anchor='top-left' size='100x100' margin='10,_,_,210' drop='unit'/>";

        [Test]
        public void A_drag_ended_by_a_system_cancel_goes_back_as_cancelled()
        {
            var s = Open(Slots);
            var a = s.Get<Frame>("a");
            var src = SourceOf(a);
            var e = Lift(a);
            Move(src, e, ScreenOf(s.Get<Frame>("slotB")));
            Tick();

            LiftGesture.CanceledProbeForTests = _ => true;
            End(src, e);

            Assert.IsEmpty(_drops, "nothing was put into the target");
            Assert.AreEqual(DragResult.Cancelled, _ends.Single().Result);
            Assert.IsFalse(UI.Drag.IsDragging);
        }

        [Test]
        public void A_release_that_is_not_a_cancel_still_drops()
        {
            var s = Open(Slots);
            DragOnto(s.Get<Frame>("a"), s.Get<Frame>("slotB"));
            Assert.AreEqual(1, _drops.Count);
            Assert.AreEqual(DragResult.Dropped, _ends.Single().Result);
        }

        [Test]
        public void A_held_lift_cancelled_by_the_system_before_any_move_is_cancelled()
        {
            var s = Open("<Frame id='slot' anchor='top-left' size='80x80' drop='unit'><Frame id='x' anchor='stretch' drag='unit' dragHold='0.3s'/></Frame>");
            var x = s.Get<Frame>("x");
            var src = SourceOf(x);
            var pressed = true;
            LiftGesture.PressedProbeForTests = _ => pressed;
            Press(src, ScreenOf(x));
            src.TickForTests(0.4f);
            Assume.That(UI.Drag.IsDragging, "precondition: lifted by the hold, not moved yet");

            pressed = false;
            LiftGesture.CanceledProbeForTests = _ => true;
            src.TickForTests(0.016f);

            Assert.IsEmpty(_drops);
            Assert.AreEqual(DragResult.Cancelled, _ends.Single().Result);
        }

        // ───── reorder ─────

        private const string RowTemplate = "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        [Test]
        public void A_reorder_ended_by_a_system_cancel_keeps_the_order()
        {
            var s = Open("<Frame anchor='top-left' width='400' height='600'><ScrollList id='sl' width='150' height='200' itemTemplate='Row' reorder='true'/></Frame>",
                         RowTemplate);
            var list = s.Get<ScrollList>("sl");
            var rows = new List<IControl>();
            list.BindItems(Observable.Return<IReadOnlyList<string>>(new[] { "a", "b", "c" }), (IControl slot, string text) => rows.Add(slot));
            Canvas.ForceUpdateCanvases();
            var reordered = 0;
            list.OnReordered.Subscribe(_ => reordered++);
            var driver = ((RectTransform)list.GameObject.transform.Find("Viewport/Content")).GetComponent<ReorderDriver>();
            var rt = (rows[0] as Control)?.LayoutHost ?? rows[0].RectTransform;
            var first = rt.gameObject;
            var p0 = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            var w = ((RectTransform)rt.parent).TransformVector(new Vector3(0f, -70f, 0f));
            var e = new PointerEventData(EventSystem.current)
            {
                position = p0,
                pressPosition = p0,
                button = PointerEventData.InputButton.Left,
                pointerDrag = driver.gameObject,
                eligibleForClick = true,
            };
            ((IInitializePotentialDragHandler)driver).OnInitializePotentialDrag(e);
            ((IBeginDragHandler)driver).OnBeginDrag(e);
            var p1 = p0 + new Vector2(w.x, w.y);
            e.delta = p1 - e.position;
            e.position = p1;
            ((IDragHandler)driver).OnDrag(e);
            Assume.That(list.IsReordering, "precondition: a row is up and has made room two places down");

            LiftGesture.CanceledProbeForTests = _ => true;
            ((IEndDragHandler)driver).OnEndDrag(e);

            Assert.AreEqual(0, reordered, "no OnReordered");
            Assert.IsFalse(list.IsReordering);
            Assert.AreEqual(0, first.transform.GetSiblingIndex(), "the row is back in its own place");
        }
    }
}
