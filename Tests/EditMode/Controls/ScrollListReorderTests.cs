using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Text = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>&lt;ScrollList reorder&gt;</c> 拖动排序（2026-09-16 scrolllist-drag-reorder spec）—— M0：手势、结构、事件。
    /// 事件用 <c>PointerEventData</c> 直接打到 Content 上的 <see cref="ReorderDriver"/>（同 <c>CarouselDragTests</c>）；
    /// EditMode 下 <c>!Application.isPlaying</c> → FLIP / 落位全部瞬移，结构当帧可断言。
    /// </summary>
    public class ScrollListReorderTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string RowTemplate =
            "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        private static PromptUGUI.Application.Screen Open(string body, string templates = RowTemplate)
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>"
                    + templates
                    + $"<Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>{body}</Frame></Screen>"
                    + "</PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return screen;
        }

        private static RectTransform ContentOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Viewport/Content");

        private static ReorderDriver DriverOf(ScrollList sl) => ContentOf(sl).GetComponent<ReorderDriver>();

        private static List<IControl> Push(ScrollList list, int count, string prefix = "i")
        {
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = prefix + i;
            return Push(list, items);
        }

        private static List<IControl> Push(ScrollList list, IReadOnlyList<string> items)
        {
            var rows = new List<IControl>();
            list.BindItems(
                Observable.Return(items),
                (IControl slot, string s) =>
                {
                    rows.Add(slot);
                    slot.Get<Text>("label").TextValue = s;
                });
            Canvas.ForceUpdateCanvases();
            return rows;
        }

        // ───── 指针几何：全部经 world 坐标，CanvasScaler 的 scaleFactor 不影响断言 ─────

        private static Vector2 ScreenOf(RectTransform rt, Vector2 localOffset = default)
        {
            var world = rt.TransformPoint((Vector3)rt.rect.center + (Vector3)localOffset);
            return RectTransformUtility.WorldToScreenPoint(null, world);
        }

        /// <summary>屏幕点沿 Content 本地轴偏移 (dx, dy) 个 canvas 单位。</summary>
        private static Vector2 Offset(RectTransform content, Vector2 screen, float dx, float dy)
        {
            var w = content.TransformVector(new Vector3(dx, dy, 0f));
            return screen + new Vector2(w.x, w.y);
        }

        private static PointerEventData Press(ReorderDriver d, Vector2 screen, int pointerId = -1)
        {
            var e = new PointerEventData(EventSystem.current)
            {
                position = screen,
                pressPosition = screen,
                button = PointerEventData.InputButton.Left,
                pointerId = pointerId,
                pointerDrag = d.gameObject,
                eligibleForClick = true,
            };
            ((IInitializePotentialDragHandler)d).OnInitializePotentialDrag(e);
            return e;
        }

        private static void Begin(ReorderDriver d, PointerEventData e) => ((IBeginDragHandler)d).OnBeginDrag(e);

        private static void DragTo(ReorderDriver d, PointerEventData e, Vector2 screen)
        {
            e.delta = screen - e.position;
            e.position = screen;
            ((IDragHandler)d).OnDrag(e);
        }

        private static void End(ReorderDriver d, PointerEventData e) => ((IEndDragHandler)d).OnEndDrag(e);

        /// <summary>按下 → begin → 拖到目标 → 松手，一气呵成（hold=0 的鼠标路径）。</summary>
        private static void DragRow(ScrollList list, RectTransform row, float dx, float dy)
        {
            var d = DriverOf(list);
            var p0 = ScreenOf(row);
            var e = Press(d, p0);
            Begin(d, e);
            DragTo(d, e, Offset(ContentOf(list), p0, dx, dy));
            End(d, e);
        }

        private static RectTransform Rt(IControl c) => (c as Control)?.LayoutHost ?? c.RectTransform;

        // Row geometry is MEASURED, not assumed: the list's own V/H group leaves childControl* off,
        // so a row keeps its default rect rather than its LayoutElement (the caveat HugSizingTests
        // spells out). What the tests need is "one slot further", whatever a slot turns out to be.
        private static float StrideY(IReadOnlyList<IControl> rows)
            => Rt(rows[0]).anchoredPosition.y - Rt(rows[1]).anchoredPosition.y;

        private static float StrideX(IReadOnlyList<IControl> rows)
            => Rt(rows[1]).anchoredPosition.x - Rt(rows[0]).anchoredPosition.x;

        private static Transform Placeholder(ScrollList list) => ContentOf(list).Find(ReorderDriver.PlaceholderName);

        private static string[] LabelsInSiblingOrder(ScrollList list)
        {
            var content = ContentOf(list);
            var result = new List<string>();
            for (var i = 0; i < content.childCount; i++)
            {
                var tmp = content.GetChild(i).GetComponentInChildren<TMPro.TMP_Text>(true);
                if (tmp != null) result.Add(tmp.text);
            }
            return result.ToArray();
        }

        /// <summary>挂在 ScrollList 根上，数被驱动器转发上来的 drag 事件（ScrollRect 收到的就是它收到的）。</summary>
        private sealed class DragCounter : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
        {
            public int Begin, Drag, End;
            void IBeginDragHandler.OnBeginDrag(PointerEventData e) => Begin++;
            void IDragHandler.OnDrag(PointerEventData e) => Drag++;
            void IEndDragHandler.OnEndDrag(PointerEventData e) => End++;
        }

        private static DragCounter CountForwarded(ScrollList list) => list.GameObject.AddComponent<DragCounter>();

        private static (ScrollList list, List<IControl> rows) OpenList(string attrs, int count = 3)
        {
            var list = Open($"<ScrollList id='sl' width='150' height='200' itemTemplate='Row' {attrs}/>")
                .Get<ScrollList>("sl");
            var rows = Push(list, count);
            return (list, rows);
        }

        // ───── 1. 开关 ─────

        [Test]
        public void Reorder_off_installs_no_driver_and_no_catcher()
        {
            var (list, _) = OpenList("");
            Assert.IsNull(DriverOf(list), "no driver without reorder=");
            Assert.IsNull(ContentOf(list).GetComponent<HitCatcher>(), "no catcher without reorder=");
        }

        [Test]
        public void Reorder_on_installs_driver_and_geometry_less_catcher()
        {
            var (list, _) = OpenList("reorder='true'");
            Assert.IsNotNull(DriverOf(list));
            var catcher = ContentOf(list).GetComponent<HitCatcher>();
            Assert.IsNotNull(catcher, "Content catches presses between rows so the driver is found");
            Assert.IsTrue(catcher.raycastTarget);
            Assert.IsNotNull(catcher.GetComponent<CanvasRenderer>(), "a Graphic needs its CanvasRenderer");
        }

        // ───── 2. 空地 = 纯滚动 ─────

        [Test]
        public void Press_off_any_row_forwards_the_whole_gesture()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0' spacing='10'");
            var counter = CountForwarded(list);
            var d = DriverOf(list);
            var content = ContentOf(list);
            // Press in the gap between row0 and row1 (spacing 10): half a row plus half the gap below row0's centre.
            var p0 = ScreenOf(Rt(rows[0]), new Vector2(0f, -(Rt(rows[0]).rect.height * 0.5f + 5f)));
            var e = Press(d, p0);
            Begin(d, e);
            DragTo(d, e, Offset(content, p0, 0f, -50f));
            End(d, e);

            Assert.AreEqual((1, 1, 1), (counter.Begin, counter.Drag, counter.End), "begin/drag/end all reach the root");
            Assert.IsNull(Placeholder(list));
            Assert.IsFalse(list.IsReordering);
        }

        // ───── 3. 提起 ─────

        [Test]
        public void Lift_takes_row_out_of_flow_and_leaves_a_same_size_placeholder()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0'");
            var counter = CountForwarded(list);
            var content = ContentOf(list);
            var row1 = Rt(rows[1]);
            var preferredBefore = LayoutUtility.GetPreferredHeight(content);
            var sizeBefore = row1.sizeDelta;

            var d = DriverOf(list);
            var e = Press(d, ScreenOf(row1));
            Begin(d, e);

            Assert.IsTrue(list.IsReordering);
            Assert.IsTrue(row1.GetComponent<LayoutElement>().ignoreLayout, "lifted row leaves the layout flow");
            Assert.AreEqual(content.childCount - 1, row1.GetSiblingIndex(), "lifted row draws above its siblings");
            var ph = Placeholder(list);
            Assert.IsNotNull(ph, "a placeholder holds the slot");
            Assert.AreEqual(1, ph.GetSiblingIndex(), "placeholder sits where the row was");
            Assert.AreEqual(sizeBefore, ((RectTransform)ph).sizeDelta, "placeholder copies the row's size");
            Assert.AreEqual(preferredBefore, LayoutUtility.GetPreferredHeight(content), 0.01f,
                "content size unchanged — a hug list does not jump");
            Assert.AreEqual(0, counter.Begin, "reorder-mode begin is not forwarded to the ScrollRect");
        }

        [Test]
        public void Lifted_row_follows_the_pointer_on_the_main_axis_only()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0'");
            var content = ContentOf(list);
            var row1 = Rt(rows[1]);
            var d = DriverOf(list);
            var p0 = ScreenOf(row1);
            var before = row1.anchoredPosition;
            var e = Press(d, p0);
            Begin(d, e);
            DragTo(d, e, Offset(content, p0, 25f, -12f));

            Assert.AreEqual(before.y - 12f, row1.anchoredPosition.y, 0.01f, "tracks the finger 1:1 along the axis");
            Assert.AreEqual(before.x, row1.anchoredPosition.x, 0.01f, "cross axis is locked in a single column");
        }

        // ───── 4. 实时让位 ─────

        [Test]
        public void Crossing_the_next_row_center_moves_the_placeholder_and_the_row_shifts_up()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0' spacing='10'");
            var content = ContentOf(list);
            var row1 = Rt(rows[1]);
            var row2 = Rt(rows[2]);
            var row1Pos = row1.anchoredPosition;
            var row2Pos = row2.anchoredPosition;

            var d = DriverOf(list);
            var p0 = ScreenOf(row1);
            var e = Press(d, p0);
            Begin(d, e);
            // Dragging one stride (row height + spacing) and a bit puts row1's centre past row2's, so row2
            // takes row1's slot.
            DragTo(d, e, Offset(content, p0, 0f, -(StrideY(rows) + 5f)));

            Assert.AreEqual(2, Placeholder(list).GetSiblingIndex(), "placeholder moved after row2");
            Assert.AreEqual(row1Pos.y, row2.anchoredPosition.y, 0.01f, "row2 slid up into row1's slot (instant in EditMode)");

            // Back up past row2's NEW centre (now -55): the swap undoes. A hair above the origin, so
            // the comparison does not hinge on a screen→local round-trip landing exactly on -55.
            DragTo(d, e, Offset(content, p0, 0f, 5f));
            Assert.AreEqual(1, Placeholder(list).GetSiblingIndex());
            Assert.AreEqual(row2Pos.y, row2.anchoredPosition.y, 0.01f);
        }

        // ───── 5. 松手定型 + 事件 ─────

        [Test]
        public void Drop_commits_sibling_order_slots_and_fires_OnReordered_once()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0' spacing='10'");
            var content = ContentOf(list);
            var fired = new List<(int From, int To)>();
            list.OnReordered.Subscribe(fired.Add);

            DragRow(list, Rt(rows[1]), 0f, -(StrideY(rows) + 5f));

            Assert.IsNull(Placeholder(list), "placeholder gone");
            Assert.IsFalse(Rt(rows[1]).GetComponent<LayoutElement>().ignoreLayout, "row is back in flow");
            Assert.AreEqual(3, content.childCount);
            CollectionAssert.AreEqual(new[] { "i0", "i2", "i1" }, LabelsInSiblingOrder(list));
            CollectionAssert.AreEqual(new[] { rows[0], rows[2], rows[1] }, list.Slots, "_slots permuted the same way");
            CollectionAssert.AreEqual(new[] { (1, 2) }, fired);
            Assert.IsFalse(list.IsReordering);
        }

        [Test]
        public void Drop_in_place_fires_nothing()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0' spacing='10'");
            var fired = 0;
            list.OnReordered.Subscribe(_ => fired++);

            DragRow(list, Rt(rows[1]), 0f, -5f);

            Assert.AreEqual(0, fired);
            CollectionAssert.AreEqual(new[] { "i0", "i1", "i2" }, LabelsInSiblingOrder(list));
        }

        [Test]
        public void To_is_the_insert_index_after_removal()
        {
            // Moving the LAST row to the top: from=2, to=0 (List.RemoveAt(2); Insert(0, x)).
            var (list, rows) = OpenList("reorder='true' reorderHold='0' spacing='10'");
            var fired = new List<(int From, int To)>();
            list.OnReordered.Subscribe(fired.Add);

            DragRow(list, Rt(rows[2]), 0f, 2f * StrideY(rows) + 5f);

            CollectionAssert.AreEqual(new[] { (2, 0) }, fired);
            CollectionAssert.AreEqual(new[] { "i2", "i0", "i1" }, LabelsInSiblingOrder(list));
        }

        // ───── 6. 与 BindItems 的契约 ─────

        [Test]
        public void Synchronous_push_of_the_permuted_list_rebinds_with_zero_visual_change()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0' spacing='10'");
            var gos = new[] { rows[0].GameObject, rows[1].GameObject, rows[2].GameObject };
            list.OnReordered.Subscribe(e =>
            {
                var next = new List<string> { "i0", "i1", "i2" };
                var moved = next[e.From]; next.RemoveAt(e.From); next.Insert(e.To, moved);
                Push(list, next);
            });

            DragRow(list, Rt(rows[1]), 0f, -(StrideY(rows) + 5f));

            CollectionAssert.AreEqual(new[] { "i0", "i2", "i1" }, LabelsInSiblingOrder(list));
            // Same GameObjects, same order — the host's push found every row already where it belongs.
            var content = ContentOf(list);
            CollectionAssert.AreEqual(new[] { gos[0], gos[2], gos[1] },
                new[] { content.GetChild(0).gameObject, content.GetChild(1).gameObject, content.GetChild(2).gameObject });
        }

        [Test]
        public void Push_of_the_unsynced_list_pulls_rows_back_to_data_order()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0' spacing='10'");
            DragRow(list, Rt(rows[1]), 0f, -(StrideY(rows) + 5f));
            CollectionAssert.AreEqual(new[] { "i0", "i2", "i1" }, LabelsInSiblingOrder(list));

            Push(list, new[] { "i0", "i1", "i2" });   // host did NOT apply the move

            CollectionAssert.AreEqual(new[] { "i0", "i1", "i2" }, LabelsInSiblingOrder(list), "the model is the truth");
        }

        // ───── 7. hold ─────

        [Test]
        public void Hold_not_elapsed_when_the_drag_begins_means_scroll()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0.4s'");
            var counter = CountForwarded(list);
            var d = DriverOf(list);
            var e = Press(d, ScreenOf(Rt(rows[1])), pointerId: 0);
            d.TickForTests(0.2f);
            Begin(d, e);

            Assert.AreEqual(1, counter.Begin, "the finger moved first: it is a scroll");
            Assert.IsFalse(list.IsReordering);
            Assert.IsNull(Placeholder(list));
            DragTo(d, e, e.position + new Vector2(0f, -30f));
            End(d, e);
            Assert.AreEqual((1, 1), (counter.Drag, counter.End));
        }

        [Test]
        public void Hold_elapsed_lifts_clears_click_eligibility_and_then_swallows_the_drag()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0.4s'");
            var counter = CountForwarded(list);
            var d = DriverOf(list);
            var e = Press(d, ScreenOf(Rt(rows[1])), pointerId: 0);
            Assert.IsFalse(list.IsReordering, "not yet");
            d.TickForTests(0.5f);

            Assert.IsTrue(list.IsReordering, "hold elapsed: lifted before any drag");
            Assert.IsFalse(e.eligibleForClick, "a long-press lift is not a click");
            Assert.IsNotNull(Placeholder(list));

            Begin(d, e);
            Assert.AreEqual(0, counter.Begin, "reorder mode: nothing forwarded");
        }

        [Test]
        public void Auto_hold_is_zero_for_the_mouse_and_a_hold_for_touch()
        {
            var (list, rows) = OpenList("reorder='true'");   // reorderHold=auto
            var counter = CountForwarded(list);
            var d = DriverOf(list);

            var mouse = Press(d, ScreenOf(Rt(rows[1])), pointerId: -1);
            Begin(d, mouse);
            Assert.IsTrue(list.IsReordering, "mouse lifts on the first drag frame");
            End(d, mouse);
            Assert.IsFalse(list.IsReordering);

            var touch = Press(d, ScreenOf(Rt(rows[1])), pointerId: 0);
            Begin(d, touch);
            Assert.IsFalse(list.IsReordering, "touch needs the hold");
            Assert.AreEqual(1, counter.Begin, "so this drag scrolls");
            End(d, touch);
        }

        [Test]
        public void Auto_hold_is_zero_on_touch_when_a_handle_is_declared()
        {
            var (list, rows) = OpenList("reorder='true' reorderHandle='label'");
            var d = DriverOf(list);
            var touch = Press(d, ScreenOf(Rt(rows[1].Get<Text>("label"))), pointerId: 0);
            Begin(d, touch);
            Assert.IsTrue(list.IsReordering, "a handle drag cannot be a scroll, so no hold is needed");
            End(d, touch);
        }

        // ───── 8. Armed 未动松手 ─────

        [Test]
        public void Release_without_moving_after_a_lift_drops_in_place()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0.4s'");
            var fired = 0;
            list.OnReordered.Subscribe(_ => fired++);
            var d = DriverOf(list);
            var row1 = Rt(rows[1]);
            var e = Press(d, ScreenOf(row1), pointerId: 0);
            d.TickForTests(0.5f);
            Assert.IsTrue(list.IsReordering);

            e.pointerDrag = null;   // what both input modules do on release
            d.TickForTests(0.01f);

            Assert.IsFalse(list.IsReordering);
            Assert.IsNull(Placeholder(list));
            Assert.IsFalse(row1.GetComponent<LayoutElement>().ignoreLayout);
            Assert.AreEqual(1, row1.GetSiblingIndex());
            Assert.AreEqual(0, fired);
        }

        // ───── 9. 把手 ─────

        private const string HandleRowTemplate =
            "<Template name='Row'><HStack height='30'><Frame id='grip' width='20'/><Text id='label'>x</Text></HStack></Template>";

        [Test]
        public void Handle_restricts_where_a_lift_can_start()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row' reorder='true' reorderHold='0' reorderHandle='grip'/>",
                            HandleRowTemplate).Get<ScrollList>("sl");
            var rows = Push(list, 3);
            var counter = CountForwarded(list);
            var d = DriverOf(list);

            var offHandle = Press(d, ScreenOf(Rt(rows[1].Get<Text>("label"))));
            Begin(d, offHandle);
            Assert.IsFalse(list.IsReordering, "the label is not the handle");
            Assert.AreEqual(1, counter.Begin, "so the gesture scrolls");
            End(d, offHandle);

            var onHandle = Press(d, ScreenOf(Rt(rows[1].Get<Frame>("grip"))));
            Begin(d, onHandle);
            Assert.IsTrue(list.IsReordering);
            End(d, onHandle);
        }

        [Test]
        public void Missing_handle_id_warns_once_and_falls_back_to_the_whole_row()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("PUI-REORDER-HANDLE-ID"));
            var (list, rows) = OpenList("reorder='true' reorderHold='0' reorderHandle='nope'");
            var d = DriverOf(list);
            var e = Press(d, ScreenOf(Rt(rows[1])));
            Begin(d, e);
            Assert.IsTrue(list.IsReordering, "falls back to the whole row");
            End(d, e);

            var e2 = Press(d, ScreenOf(Rt(rows[0])));   // no second warning
            Begin(d, e2);
            End(d, e2);
            LogAssert.NoUnexpectedReceived();
        }

        // ───── 10. 网格 / 横向 ─────

        private const string CellTemplate =
            "<Template name='Cell'><Frame><Text id='label'>x</Text></Frame></Template>";

        [Test]
        public void Grid_target_is_the_cell_under_the_dragged_centre()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Cell' reorder='true' reorderHold='0' columns='2' cellSize='50x30'/>",
                            CellTemplate).Get<ScrollList>("sl");
            var rows = Push(list, 4);
            var fired = new List<(int From, int To)>();
            list.OnReordered.Subscribe(fired.Add);

            // cell 0 → the cell one row down (index 2): move down one cell height.
            DragRow(list, Rt(rows[0]), 0f, -30f);

            CollectionAssert.AreEqual(new[] { (0, 2) }, fired);
            CollectionAssert.AreEqual(new[] { "i1", "i2", "i0", "i3" }, LabelsInSiblingOrder(list));
        }

        private const string ColumnTemplate =
            "<Template name='Col'><Frame width='30'><Text id='label'>x</Text></Frame></Template>";

        [Test]
        public void Horizontal_list_reorders_along_x()
        {
            var list = Open("<ScrollList id='sl' width='200' height='100' itemTemplate='Col' direction='horizontal' reorder='true' reorderHold='0' spacing='10'/>",
                            ColumnTemplate).Get<ScrollList>("sl");
            var rows = Push(list, 3);
            var fired = new List<(int From, int To)>();
            list.OnReordered.Subscribe(fired.Add);

            DragRow(list, Rt(rows[0]), StrideX(rows) + 5f, 0f);

            CollectionAssert.AreEqual(new[] { (0, 1) }, fired);
            CollectionAssert.AreEqual(new[] { "i1", "i0", "i2" }, LabelsInSiblingOrder(list));
        }

        // ───── 11. 隐藏行 / 不可交互行 ─────

        [Test]
        public void Hidden_rows_are_skipped_and_stay_put()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0' spacing='10'");
            rows[1].Hidden = true;
            Canvas.ForceUpdateCanvases();
            var fired = new List<(int From, int To)>();
            list.OnReordered.Subscribe(fired.Add);

            // Visible: row0 in slot 0, row2 in slot 1. Drag row0 one stride down — past row2's centre.
            DragRow(list, Rt(rows[0]), 0f, -(StrideY(rows) + 5f));

            CollectionAssert.AreEqual(new[] { (0, 2) }, fired, "to counts every slot, hidden ones included");
            CollectionAssert.AreEqual(new[] { rows[1], rows[2], rows[0] }, list.Slots);
        }

        [Test]
        public void Non_interactable_row_cannot_be_lifted()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0'");
            rows[1].Interactable = false;
            var counter = CountForwarded(list);
            var d = DriverOf(list);
            var e = Press(d, ScreenOf(Rt(rows[1])));
            Begin(d, e);
            Assert.IsFalse(list.IsReordering);
            Assert.AreEqual(1, counter.Begin);
            End(d, e);
        }

        // ───── 12. 取消 ─────

        [Test]
        public void ReSolve_mid_drag_cancels_and_restores_the_origin()
        {
            var screen = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row' reorder='true' reorderHold='0' spacing='10'/>");
            var list = screen.Get<ScrollList>("sl");
            var rows = Push(list, 3);
            var counter = CountForwarded(list);
            var fired = 0;
            list.OnReordered.Subscribe(_ => fired++);
            var d = DriverOf(list);
            var content = ContentOf(list);
            var row1 = Rt(rows[1]);
            var p0 = ScreenOf(row1);
            var e = Press(d, p0);
            Begin(d, e);
            DragTo(d, e, Offset(content, p0, 0f, -(StrideY(rows) + 5f)));
            Assert.AreEqual(2, Placeholder(list).GetSiblingIndex(), "precondition: mid-drag, row2 has made room");

            screen.ReSolve();

            Assert.IsFalse(list.IsReordering);
            Assert.IsNull(Placeholder(list));
            Assert.IsFalse(row1.GetComponent<LayoutElement>().ignoreLayout);
            CollectionAssert.AreEqual(new[] { "i0", "i1", "i2" }, LabelsInSiblingOrder(list), "back to the origin");
            Assert.AreEqual(0, fired);

            // The rest of this gesture is swallowed — not handed to the ScrollRect half-way.
            DragTo(d, e, Offset(content, p0, 0f, -60f));
            End(d, e);
            Assert.AreEqual((0, 0, 0), (counter.Begin, counter.Drag, counter.End));
            CollectionAssert.AreEqual(new[] { "i0", "i1", "i2" }, LabelsInSiblingOrder(list));
        }

        [Test]
        public void Push_mid_drag_cancels_before_rebinding()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0' spacing='10'");
            var fired = 0;
            list.OnReordered.Subscribe(_ => fired++);
            var d = DriverOf(list);
            var content = ContentOf(list);
            var p0 = ScreenOf(Rt(rows[1]));
            var e = Press(d, p0);
            Begin(d, e);
            DragTo(d, e, Offset(content, p0, 0f, -(StrideY(rows) + 5f)));

            Push(list, new[] { "a", "b", "c" });

            Assert.IsFalse(list.IsReordering);
            Assert.IsNull(Placeholder(list));
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, LabelsInSiblingOrder(list));
            Assert.AreEqual(0, fired);
            End(d, e);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, LabelsInSiblingOrder(list));
        }

        // ───── 13. 静态子卡 ─────

        [Test]
        public void Static_children_reorder_and_ReSolve_keeps_the_new_order()
        {
            var screen = Open("<ScrollList id='sl' width='150' height='200' reorder='true' reorderHold='0' spacing='10'>"
                            + "<Frame id='a' height='30'><Text>a</Text></Frame>"
                            + "<Frame id='b' height='30'><Text>b</Text></Frame>"
                            + "<Frame id='c' height='30'><Text>c</Text></Frame>"
                            + "</ScrollList>");
            var list = screen.Get<ScrollList>("sl");
            var fired = new List<(int From, int To)>();
            list.OnReordered.Subscribe(fired.Add);

            var a = screen.Get<Frame>("a").RectTransform;
            var stride = a.anchoredPosition.y - screen.Get<Frame>("b").RectTransform.anchoredPosition.y;
            DragRow(list, a, 0f, -(stride + 5f));

            CollectionAssert.AreEqual(new[] { (0, 1) }, fired);
            CollectionAssert.AreEqual(new[] { "b", "a", "c" }, LabelsInSiblingOrder(list));

            screen.ReSolve();
            CollectionAssert.AreEqual(new[] { "b", "a", "c" }, LabelsInSiblingOrder(list), "ReSolve replays attributes, not sibling order");
        }

        // ───── 14. 边缘自动滚动 ─────

        [Test]
        public void Dragging_near_the_viewport_edge_scrolls_the_content()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0'", count: 12);   // 360 > 200 viewport
            var d = DriverOf(list);
            var content = ContentOf(list);
            var viewport = (RectTransform)content.parent;
            var p0 = ScreenOf(Rt(rows[0]));
            var e = Press(d, p0);
            Begin(d, e);
            // Pointer 5 units above the viewport's bottom edge.
            var bottom = RectTransformUtility.WorldToScreenPoint(null,
                viewport.TransformPoint(new Vector3(viewport.rect.center.x, viewport.rect.yMin + 5f, 0f)));
            DragTo(d, e, bottom);
            var before = content.anchoredPosition.y;

            d.TickForTests(0.1f);

            Assert.Greater(content.anchoredPosition.y, before, "content scrolled towards the dragged edge");
            End(d, e);
        }

        // ───── 15. 参数解析 ─────

        [Test]
        public void Bad_hold_value_warns_and_keeps_the_previous_value()
        {
            LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("reorderHold"));
            var (list, _) = OpenList("reorder='true' reorderHold='soon'");
            Assert.IsNotNull(DriverOf(list), "the Screen still opens");
        }
    }
}
