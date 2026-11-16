using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>Press, hold, lift, follow — and everything that is not a lift (spec 2026-11-16 §7.1, §7.2).</summary>
    public class DragDropGestureTests : DragDropTestBase
    {
        private sealed class PointerUpCounter : MonoBehaviour, IPointerUpHandler
        {
            public int Count;
            public void OnPointerUp(PointerEventData eventData) => Count++;
        }

        private const string RowTemplate =
            "<Template name='Row'><Frame height='60'><Frame id='card' anchor='stretch' margin='4' drag='unit'/></Frame></Template>";

        private static List<IControl> PushRows(ScrollList list, int count)
        {
            var rows = new List<IControl>();
            var items = new int[count];
            for (var i = 0; i < count; i++) items[i] = i;
            list.BindItems(Observable.Return<IReadOnlyList<int>>(items), (IControl row, int _) => rows.Add(row));
            Canvas.ForceUpdateCanvases();
            return rows;
        }

        [Test]
        public void A_mouse_lifts_on_the_first_drag_frame_into_a_lift_layer()
        {
            var s = Open(TwoSlots);
            var a = s.Get<Frame>("a");
            var e = Press(SourceOf(a), ScreenOf(a));

            Assert.IsFalse(UI.Drag.IsDragging, "a press alone is still a click");
            Begin(SourceOf(a), e);

            Assert.IsTrue(UI.Drag.IsDragging);
            var canvas = LiftCanvasOf(a);
            Assert.IsNotNull(canvas, "lifted in place under a nested canvas");
            Assert.IsTrue(canvas.overrideSorting);
            Assert.AreEqual(UI.Drag.SortingOrder, canvas.sortingOrder);
            Assert.IsNull(canvas.GetComponent<GraphicRaycaster>(), "no raycaster: the lifted thing is transparent to the pointer");
            var parentCanvas = Host(a).parent.GetComponentInParent<Canvas>();
            Assert.AreEqual(parentCanvas.additionalShaderChannels,
                canvas.additionalShaderChannels & parentCanvas.additionalShaderChannels,
                "shader channels copied, or a procedural panel collapses");
            Assert.IsFalse(e.eligibleForClick);
        }

        [Test]
        public void A_press_then_release_without_dragging_is_not_a_lift()
        {
            var s = Open(TwoSlots);
            var a = s.Get<Frame>("a");
            var e = Press(SourceOf(a), ScreenOf(a));
            End(SourceOf(a), e);

            Assert.IsFalse(UI.Drag.IsDragging);
            Assert.IsNull(LiftCanvasOf(a));
        }

        [Test]
        public void The_lift_ends_the_press_the_way_uGUI_ends_it_for_a_drag()
        {
            var s = Open(TwoSlots);
            var a = s.Get<Frame>("a");
            var pressed = new GameObject("pressed").AddComponent<PointerUpCounter>();
            try
            {
                var e = Press(SourceOf(a), ScreenOf(a));
                e.pointerPress = pressed.gameObject;
                Begin(SourceOf(a), e);

                Assert.AreEqual(1, pressed.Count, "a lifted <Btn> must not keep its Pressed look");
                Assert.IsNull(e.pointerPress, "and release must not click it");
            }
            finally
            {
                Object.DestroyImmediate(pressed.gameObject);
            }
        }

        [Test]
        public void In_a_layout_group_a_placeholder_holds_the_slot()
        {
            var s = Open("<HStack id='row' anchor='top-left' height='50' spacing='4'>" +
                         "<Frame id='x' width='40' drag='unit'/><Frame id='y' width='40' drag='unit'/><Frame id='z' width='40'/>" +
                         "</HStack>");
            var row = (RectTransform)s.Get<HStack>("row").RectTransform;
            var x = s.Get<Frame>("x");
            var widthBefore = LayoutUtility.GetPreferredWidth(row);
            var indexBefore = Host(x).GetSiblingIndex();

            Lift(x);

            var ph = Placeholder(x);
            Assert.IsNotNull(ph);
            Assert.AreEqual(indexBefore + 1, ph.GetSiblingIndex(), "right after the source");
            Assert.AreEqual(indexBefore, Host(x).GetSiblingIndex(), "the source keeps its sibling index");
            Assert.IsTrue(Host(x).GetComponent<LayoutElement>().ignoreLayout);
            LayoutRebuilder.ForceRebuildLayoutImmediate(row);
            Assert.AreEqual(widthBefore, LayoutUtility.GetPreferredWidth(row), 0.01f, "the group does not shrink");
        }

        [Test]
        public void The_lifted_source_follows_the_pointer_one_to_one()
        {
            var s = Open(TwoSlots);
            var a = s.Get<Frame>("a");
            var src = SourceOf(a);
            var parent = (RectTransform)Host(a).parent;
            var e = Lift(a);
            var before = Host(a).anchoredPosition;

            Move(src, e, Offset(parent, e.pressPosition, 30f, -20f));

            Assert.AreEqual(before.x + 30f, Host(a).anchoredPosition.x, 0.01f);
            Assert.AreEqual(before.y - 20f, Host(a).anchoredPosition.y, 0.01f);
        }

        [Test]
        public void A_finger_outside_any_scroller_lifts_at_once()
        {
            var s = Open(TwoSlots);
            var a = s.Get<Frame>("a");
            var e = Press(SourceOf(a), ScreenOf(a), pointerId: 0);
            Begin(SourceOf(a), e);

            Assert.IsTrue(UI.Drag.IsDragging, "nothing to tell a drag apart from: no hold");
        }

        [Test]
        public void In_a_scroller_a_finger_moving_along_the_axis_scrolls()
        {
            var s = Open("<ScrollList id='list' anchor='top-left' size='200x300' itemTemplate='Row'/>", RowTemplate);
            var list = s.Get<ScrollList>("list");
            var rows = PushRows(list, 10);
            var card = rows[1].Get<Frame>("card");
            var content = (RectTransform)list.GameObject.transform.Find("Viewport/Content");
            var contentBefore = content.anchoredPosition;

            var src = SourceOf(card);
            var e = Press(src, ScreenOf(card), pointerId: 0);
            e.position = Offset(content, e.pressPosition, 0f, 30f);
            Begin(src, e);
            Move(src, e, Offset(content, e.pressPosition, 0f, 80f));

            Assert.IsFalse(UI.Drag.IsDragging);
            Assert.AreNotEqual(contentBefore, content.anchoredPosition, "the ScrollRect got the gesture");
        }

        [Test]
        public void In_a_single_axis_scroller_a_finger_moving_across_it_lifts_at_once()
        {
            var s = Open("<ScrollList id='list' anchor='top-left' size='200x300' itemTemplate='Row'/>", RowTemplate);
            var rows = PushRows(s.Get<ScrollList>("list"), 10);
            var card = rows[1].Get<Frame>("card");
            var src = SourceOf(card);
            var parent = (RectTransform)Host(card).parent;

            var e = Press(src, ScreenOf(card), pointerId: 0);
            e.position = Offset(parent, e.pressPosition, 40f, 5f);
            Begin(src, e);

            Assert.IsTrue(UI.Drag.IsDragging);
        }

        [Test]
        public void In_a_scroller_a_finger_held_still_lifts_after_the_hold()
        {
            var s = Open("<ScrollList id='list' anchor='top-left' size='200x300' itemTemplate='Row'/>", RowTemplate);
            var rows = PushRows(s.Get<ScrollList>("list"), 10);
            var card = rows[1].Get<Frame>("card");
            var src = SourceOf(card);

            Press(src, ScreenOf(card), pointerId: 0);
            src.TickForTests(0.2f);
            Assert.IsFalse(UI.Drag.IsDragging, "still counting");
            src.TickForTests(0.3f);

            Assert.IsTrue(UI.Drag.IsDragging);
        }

        [Test]
        public void An_explicit_hold_applies_to_the_mouse_too()
        {
            var s = Open("<Frame id='x' anchor='top-left' size='50x50' drag='unit' dragHold='0.3s'/>");
            var x = s.Get<Frame>("x");
            var src = SourceOf(x);
            var e = Press(src, ScreenOf(x));
            e.position += new Vector2(20f, 0f);
            Begin(src, e);

            Assert.IsFalse(UI.Drag.IsDragging, "moved before the hold: not a lift");
        }

        [Test]
        public void A_handle_is_the_only_place_a_lift_starts()
        {
            var s = Open("<Frame id='x' anchor='top-left' size='100x100' drag='unit' dragHandle='grip'>" +
                         "<Frame id='grip' anchor='top-left' size='20x20'/></Frame>");
            var x = s.Get<Frame>("x");
            var src = SourceOf(x);

            var off = Press(src, ScreenOf(x, new Vector2(30f, -30f)));
            Begin(src, off);
            Assert.IsFalse(UI.Drag.IsDragging, "pressed outside the grip");
            End(src, off);

            var on = Press(src, ScreenOf(s.Get<Frame>("grip")));
            Begin(src, on);
            Assert.IsTrue(UI.Drag.IsDragging);
        }

        [Test]
        public void A_handle_that_is_not_there_warns_once_and_the_whole_source_lifts()
        {
            LogAssert.Expect(LogType.Warning, new Regex("PUI-DRAG-HANDLE-ID"));
            var s = Open("<Frame id='x' anchor='top-left' size='100x100' drag='unit' dragHandle='nope'/>");
            var x = s.Get<Frame>("x");
            Lift(x);

            Assert.IsTrue(UI.Drag.IsDragging);
        }

        [Test]
        public void No_lift_off_the_left_button_on_a_disabled_source_or_with_drag_off()
        {
            var s = Open("<Frame id='x' anchor='top-left' size='50x50' drag='unit'/>" +
                         "<Frame id='y' anchor='top-left' size='50x50' margin='0,_,_,60' drag='unit' interactable='false'/>" +
                         "<Frame id='z' anchor='top-left' size='50x50' margin='0,_,_,120' drag='none' drag.compact='unit'/>");

            var x = s.Get<Frame>("x");
            var right = Press(SourceOf(x), ScreenOf(x), button: PointerEventData.InputButton.Right);
            Begin(SourceOf(x), right);
            Assert.IsFalse(UI.Drag.IsDragging, "right button");
            End(SourceOf(x), right);

            Lift(s.Get<Frame>("y"));
            Assert.IsFalse(UI.Drag.IsDragging, "interactable=false");

            Lift(s.Get<Frame>("z"));
            Assert.IsFalse(UI.Drag.IsDragging, "drag none");
        }

        [Test]
        public void One_session_at_a_time()
        {
            var s = Open(TwoSlots);
            Lift(s.Get<Frame>("a"));
            var b = s.Get<Frame>("b");
            var eb = Press(SourceOf(b), ScreenOf(b), pointerId: 3);
            Begin(SourceOf(b), eb);

            Assert.IsNull(LiftCanvasOf(b), "a second source does not lift while one is up");
        }

        [Test]
        public void Another_pointer_does_not_move_the_session()
        {
            var s = Open(TwoSlots);
            var a = s.Get<Frame>("a");
            var src = SourceOf(a);
            Lift(a);
            var before = Host(a).anchoredPosition;

            var other = new PointerEventData(EventSystem.current) { pointerId = 7, position = Vector2.zero };
            ((IDragHandler)src).OnDrag(other);

            Assert.AreEqual(before, Host(a).anchoredPosition);
        }
    }
}
