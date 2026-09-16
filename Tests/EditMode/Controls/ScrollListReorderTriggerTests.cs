using System;
using System.Collections.Generic;
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
    /// <c>lift</c> / <c>drop</c> row hooks of <c>&lt;ScrollList reorder&gt;</c> (spec 2026-09-16 §4.2 / §4.3):
    /// <c>&lt;Trigger&gt;</c> / <c>&lt;Animation&gt;</c> / <c>&lt;Show&gt;</c> resolve upward to the row they live in,
    /// fire only for THAT row, are inert in a list without <c>reorder</c>, and an authored lift hook replaces the
    /// driver's default lift look.
    /// </summary>
    public class ScrollListReorderTriggerTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string HookedRow =
            "<Template name='Row'><Frame id='row' height='30'>"
            + "<Trigger id='lt' on='lift'><Image/></Trigger>"
            + "<Trigger id='dt' on='drop'><Image/></Trigger>"
            + "</Frame></Template>";

        private const string PlainRow =
            "<Template name='Row'><Frame id='row' height='30'><Image/></Frame></Template>";

        private static PromptUGUI.Application.Screen Open(string body, string templates)
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

        private static (ScrollList list, List<IControl> rows) OpenList(string attrs, string templates, int count = 3)
        {
            var list = Open($"<ScrollList id='sl' width='150' height='200' itemTemplate='Row' {attrs}/>", templates)
                .Get<ScrollList>("sl");
            var rows = new List<IControl>();
            list.BindItems(Observable.Return<IReadOnlyList<int>>(new int[count]), (IControl slot, int _) => rows.Add(slot));
            Canvas.ForceUpdateCanvases();
            return (list, rows);
        }

        private static RectTransform ContentOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Viewport/Content");

        private static ReorderDriver DriverOf(ScrollList sl) => ContentOf(sl).GetComponent<ReorderDriver>();

        private static RectTransform Rt(IControl c) => (c as Control)?.LayoutHost ?? c.RectTransform;

        private static Vector2 ScreenOf(RectTransform rt)
            => RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));

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
        private static void End(ReorderDriver d, PointerEventData e) => ((IEndDragHandler)d).OnEndDrag(e);

        private sealed class Counter
        {
            public int Value;
            public Counter(IControl row, string triggerId) => row.Get<Trigger>(triggerId).OnFire.Subscribe(_ => Value++);
        }

        // ───── lift / drop fire on the moving row only ─────

        [Test]
        public void Lift_and_drop_fire_once_each_on_the_row_that_moves_and_not_on_its_neighbours()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0'", HookedRow);
            var lifts = new[] { new Counter(rows[0], "lt"), new Counter(rows[1], "lt"), new Counter(rows[2], "lt") };
            var drops = new[] { new Counter(rows[0], "dt"), new Counter(rows[1], "dt"), new Counter(rows[2], "dt") };

            var d = DriverOf(list);
            var e = Press(d, ScreenOf(Rt(rows[1])));
            Begin(d, e);
            Assert.AreEqual((0, 1, 0), (lifts[0].Value, lifts[1].Value, lifts[2].Value), "lift on row1 only");
            Assert.AreEqual(0, drops[1].Value, "not dropped yet");
            End(d, e);
            Assert.AreEqual((0, 1, 0), (drops[0].Value, drops[1].Value, drops[2].Value), "drop on row1 only");
            Assert.AreEqual(1, lifts[1].Value);
        }

        [Test]
        public void A_cancelled_session_still_drops()
        {
            var screen = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row' reorder='true' reorderHold='0'/>", HookedRow);
            var list = screen.Get<ScrollList>("sl");
            var rows = new List<IControl>();
            list.BindItems(Observable.Return<IReadOnlyList<int>>(new int[3]), (IControl slot, int _) => rows.Add(slot));
            Canvas.ForceUpdateCanvases();
            var drop = new Counter(rows[1], "dt");

            var d = DriverOf(list);
            var e = Press(d, ScreenOf(Rt(rows[1])));
            Begin(d, e);
            screen.ReSolve();

            Assert.AreEqual(1, drop.Value, "the row is put back — that is a drop");
        }

        [Test]
        public void Hold_lift_then_release_without_moving_fires_lift_and_drop()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0.4s'", HookedRow);
            var lift = new Counter(rows[1], "lt");
            var drop = new Counter(rows[1], "dt");
            var d = DriverOf(list);
            var e = Press(d, ScreenOf(Rt(rows[1])), pointerId: 0);
            d.TickForTests(0.5f);
            Assert.AreEqual((1, 0), (lift.Value, drop.Value));
            e.pointerDrag = null;
            d.TickForTests(0.01f);
            Assert.AreEqual((1, 1), (lift.Value, drop.Value));
        }

        // ───── <Show on="lift"> ─────

        private const string ShowRow =
            "<Template name='Row'><Frame id='row' height='30'>"
            + "<Show id='glow' on='lift'><Image/></Show>"
            + "</Frame></Template>";

        [Test]
        public void Show_on_lift_is_visible_only_while_lifted()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0'", ShowRow);
            var glow = rows[1].Get<Show>("glow").GameObject;
            Assert.IsFalse(glow.activeSelf, "hidden at rest");

            var d = DriverOf(list);
            var e = Press(d, ScreenOf(Rt(rows[1])));
            Begin(d, e);
            Assert.IsTrue(glow.activeSelf, "shown while lifted");
            Assert.IsFalse(rows[0].Get<Show>("glow").GameObject.activeSelf, "the neighbour's stays hidden");
            End(d, e);
            Assert.IsFalse(glow.activeSelf, "hidden again after the drop");
        }

        [Test]
        public void Show_on_drop_throws_because_drop_is_a_moment_not_a_state()
        {
            // A static row, so the error surfaces from UI.Open (an exception inside a BindItems
            // binder is routed to R3's unhandled-exception handler, not thrown to the caller).
            Assert.That(() => Open("<ScrollList id='sl' width='150' height='200' reorder='true'>"
                                 + "<Frame id='row' height='30'><Show on='drop'><Image/></Show></Frame>"
                                 + "</ScrollList>", ""),
                Throws.InstanceOf<Exception>().With.Message.Contains("lift"));
        }

        // ───── default lift look vs authored hook ─────

        [Test]
        public void Default_lift_look_scales_the_row_and_the_drop_undoes_it()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0'", PlainRow);
            var rt = Rt(rows[1]);
            var d = DriverOf(list);
            var e = Press(d, ScreenOf(rt));
            Begin(d, e);
            Assert.AreEqual(1.03f, rt.localScale.x, 0.001f, "no lift hook authored → the driver's default look");
            End(d, e);
            Assert.AreEqual(1f, rt.localScale.x, 0.001f);
        }

        [Test]
        public void An_authored_lift_hook_replaces_the_default_look()
        {
            var (list, rows) = OpenList("reorder='true' reorderHold='0'", HookedRow);
            var rt = Rt(rows[1]);
            var d = DriverOf(list);
            var e = Press(d, ScreenOf(rt));
            Begin(d, e);
            Assert.AreEqual(1f, rt.localScale.x, 0.001f, "the author owns the look now — nothing is layered on top");
            End(d, e);
        }

        [Test]
        public void A_drop_only_hook_keeps_the_default_lift_look()
        {
            const string row = "<Template name='Row'><Frame id='row' height='30'><Trigger id='dt' on='drop'><Image/></Trigger></Frame></Template>";
            var (list, rows) = OpenList("reorder='true' reorderHold='0'", row);
            var rt = Rt(rows[1]);
            var d = DriverOf(list);
            var e = Press(d, ScreenOf(rt));
            Begin(d, e);
            Assert.AreEqual(1.03f, rt.localScale.x, 0.001f, "only a LIFT hook takes the look over");
            End(d, e);
        }

        // ───── resolution ─────

        [Test]
        public void Lift_at_id_resolves_lexically_inside_the_row()
        {
            const string row = "<Template name='Row'><Frame id='row' height='30'><Trigger id='lt' on='lift@row'><Image/></Trigger></Frame></Template>";
            var (list, rows) = OpenList("reorder='true' reorderHold='0'", row);
            var lift = new Counter(rows[1], "lt");
            var other = new Counter(rows[0], "lt");
            var d = DriverOf(list);
            var e = Press(d, ScreenOf(Rt(rows[1])));
            Begin(d, e);
            Assert.AreEqual((1, 0), (lift.Value, other.Value));
            End(d, e);
        }

        [Test]
        public void Hooks_in_a_list_without_reorder_are_inert_not_errors()
        {
            var (list, rows) = OpenList("", HookedRow);   // no reorder=
            Assert.IsFalse(list.IsReordering);
            Assert.AreEqual(3, rows.Count, "the list opened and bound like any other");
        }

        [Test]
        public void Bare_lift_outside_any_list_row_throws()
        {
            Assert.That(() => Open("<Frame><Trigger on='lift'><Image/></Trigger></Frame>", ""),
                Throws.InstanceOf<Exception>().With.Message.Contains("ScrollList"));
        }

        [Test]
        public void Static_rows_take_hooks_too()
        {
            var screen = Open("<ScrollList id='sl' width='150' height='200' reorder='true' reorderHold='0'>"
                            + "<Frame id='a' height='30'><Trigger id='lta' on='lift'><Image/></Trigger></Frame>"
                            + "<Frame id='b' height='30'><Trigger id='ltb' on='lift'><Image/></Trigger></Frame>"
                            + "</ScrollList>", "");
            var list = screen.Get<ScrollList>("sl");
            var a = screen.Get<Frame>("a");
            var lift = new Counter(a, "lta");
            var d = DriverOf(list);
            var e = Press(d, ScreenOf(a.RectTransform));
            Begin(d, e);
            Assert.AreEqual(1, lift.Value);
            End(d, e);
        }

        [Test]
        public void Reverse_on_drop_is_accepted_by_Animation()
        {
            const string row = "<Template name='Row'><Animation on='lift' reverse-on='drop' scale='1:1.05' duration='0.1s'>"
                             + "<Frame id='row' height='30'><Image/></Frame></Animation></Template>";
            Assert.DoesNotThrow(() =>
            {
                var (list, rows) = OpenList("reorder='true' reorderHold='0'", row);
                var d = DriverOf(list);
                var e = Press(d, ScreenOf(Rt(rows[1])));
                Begin(d, e);
                End(d, e);
            });
        }
    }
}
