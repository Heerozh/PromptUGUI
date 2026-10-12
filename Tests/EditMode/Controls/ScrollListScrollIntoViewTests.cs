using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Text = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>ScrollList.ScrollIntoView(target)</c>: the least scroll that shows any control inside the list's rows
    /// whole — a row, or something inside the one slot a <c>&lt;TabBar&gt;</c> list has; a target taller than
    /// the viewport aligns its start edge. It keeps following the target while the target's geometry moves (a
    /// row unfolding) until it settles. EditMode does not run LateUpdate — ticks are driven explicitly.
    /// </summary>
    public class ScrollListScrollIntoViewTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Templates =
            "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>"
            + "<Template name='Col'><Frame width='40'><Text id='label'>x</Text></Frame></Template>";

        private PromptUGUI.Application.Screen _screen;

        // Viewport 200 tall, padding 4, rows 30 + spacing 2: row i spans [4 + 32 i, 34 + 32 i].
        private ScrollList Open(bool virtualize = false, string attrs = "", string children = "", string itemTemplate = "Row")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + Templates
                    + "<Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>"
                    + $"<ScrollList id='sl' width='150' height='200' itemTemplate='{itemTemplate}'"
                    + $" virtualize='{(virtualize ? "true" : "false")}' spacing='2' padding='4' {attrs}>{children}</ScrollList>"
                    + "</Frame></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            _screen = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return _screen.Get<ScrollList>("sl");
        }

        private static RectTransform ContentOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Viewport/Content");

        private static RectTransform ViewportOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Viewport");

        private static RectTransform HostOf(IControl c) => (c as Control)?.LayoutHost ?? c.RectTransform;

        private static float S(ScrollList sl) => ContentOf(sl).anchoredPosition.y;

        private static float EndOf(ScrollList sl) =>
            Mathf.Max(0f, ContentOf(sl).rect.height - ViewportOf(sl).rect.height);

        private static void Tick(ScrollList sl) => ((IScrollTickHost)sl).OnScrollLateUpdate();

        private static string LabelOf(IControl row) =>
            row.Get<Text>("label").GameObject.GetComponent<TMP_Text>().text;

        private static IControl RowShowing(ScrollList list, string label)
        {
            foreach (var row in list.Slots)
                if (LabelOf(row) == label) return row;
            return null;
        }

        private static void Push(ScrollList list, int count)
        {
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = "i" + i;
            list.BindItems(Observable.Return<IReadOnlyList<string>>(items),
                (IControl row, string s) => row.Get<Text>("label").TextValue = s, key: s => s);
        }

        /// <summary>The target's rect lies inside the viewport's (world space).</summary>
        private static void AssertFullyVisible(ScrollList list, IControl target, string because = "")
        {
            var t = new Vector3[4];
            var v = new Vector3[4];
            HostOf(target).GetWorldCorners(t);
            ViewportOf(list).GetWorldCorners(v);
            Assert.GreaterOrEqual(t[0].y, v[0].y - 0.5f, "bottom edge inside the viewport " + because);
            Assert.LessOrEqual(t[1].y, v[1].y + 0.5f, "top edge inside the viewport " + because);
            Assert.GreaterOrEqual(t[0].x, v[0].x - 0.5f, "left edge inside the viewport " + because);
            Assert.LessOrEqual(t[2].x, v[2].x + 0.5f, "right edge inside the viewport " + because);
        }

        private static void SetHeight(IControl row, float h)
        {
            var le = HostOf(row).GetComponent<LayoutElement>();
            le.preferredHeight = h;
            le.minHeight = h;
        }

        // ───── the least scroll ─────

        [Test]
        public void A_row_below_the_viewport_is_scrolled_up_to_its_bottom_edge()
        {
            var list = Open();
            Push(list, 100);

            list.ScrollIntoView(list.Slots[20]);

            Assert.AreEqual(34f + 32f * 20f - 200f, S(list), 0.5f, "row 20's bottom edge at the viewport's bottom edge");
            AssertFullyVisible(list, list.Slots[20]);
        }

        [Test]
        public void A_row_above_the_viewport_is_scrolled_down_to_its_top_edge()
        {
            var list = Open();
            Push(list, 100);
            list.ScrollToIndex(40);

            list.ScrollIntoView(list.Slots[10]);

            Assert.AreEqual(4f + 32f * 10f, S(list), 0.5f, "row 10's top edge at the viewport's top edge");
        }

        [Test]
        public void A_row_already_in_view_does_not_move_the_list()
        {
            var list = Open();
            Push(list, 100);
            list.ScrollToIndex(10);
            var before = S(list);

            list.ScrollIntoView(list.Slots[13]);

            Assert.AreEqual(before, S(list), 0.01f);
        }

        [Test]
        public void A_target_taller_than_the_viewport_aligns_its_start_edge()
        {
            var list = Open(children: "<Frame height='100'/><Frame id='tall' height='300'/><Frame height='100'/>");
            var tall = _screen.Get("tall");

            list.ScrollIntoView(tall);
            Assert.AreEqual(4f + 102f, S(list), 0.5f);

            list.ScrollToEnd();
            list.ScrollIntoView(tall);
            Assert.AreEqual(4f + 102f, S(list), 0.5f, "from below too: the start edge, not the least scroll");
        }

        [Test]
        public void The_last_row_stays_within_the_scrollable_range()
        {
            var list = Open();
            Push(list, 100);

            list.ScrollIntoView(list.Slots[99]);

            Assert.LessOrEqual(S(list), EndOf(list) + 0.01f);
            AssertFullyVisible(list, list.Slots[99]);
        }

        [Test]
        public void Any_descendant_of_a_single_slot_can_be_the_target()
        {
            // A <TabBar> inside a list is ONE slot: ScrollToIndex cannot reach its rows, ScrollIntoView can.
            var rows = new System.Text.StringBuilder();
            for (var i = 0; i < 20; i++) rows.Append($"<Frame id='r{i}' height='30'/>");
            var list = Open(children: $"<VStack id='inner'>{rows}</VStack>");
            Assume.That(list.SlotCount, Is.EqualTo(1));

            list.ScrollIntoView(_screen.Get("r15"));

            Assert.AreEqual(4f + 30f * 16f - 200f, S(list), 0.5f);
            AssertFullyVisible(list, _screen.Get("r15"));
        }

        [Test]
        public void Horizontal_list_scrolls_along_its_axis()
        {
            var list = Open(attrs: "direction='horizontal'", itemTemplate: "Col");
            Push(list, 30);

            list.ScrollIntoView(list.Slots[10]);

            Assert.AreEqual(-(44f + 42f * 10f - 150f), ContentOf(list).anchoredPosition.x, 0.5f,
                "column 10's right edge at the viewport's right edge");
        }

        [Test]
        public void A_virtual_list_scrolls_to_a_realized_row()
        {
            var list = Open(virtualize: true);
            Push(list, 100);
            list.ScrollToIndex(50);
            var row = RowShowing(list, "i56");
            Assume.That(row, Is.Not.Null, "realized just below the viewport");

            list.ScrollIntoView(row);

            Assert.AreEqual(34f + 32f * 56f - 200f, S(list), 0.5f);
            AssertFullyVisible(list, RowShowing(list, "i56"));
        }

        [Test]
        public void A_hidden_list_scrolls_when_it_shows()
        {
            var list = Open();
            Push(list, 100);
            var box = list.RectTransform.parent.gameObject;
            box.SetActive(false);

            list.ScrollIntoView(list.Slots[20]);
            Assert.AreEqual(0f, S(list), 0.5f, "a hidden list only remembers it");

            box.SetActive(true);
            Tick(list);

            AssertFullyVisible(list, list.Slots[20]);
        }

        [Test]
        public void A_control_outside_the_list_is_rejected()
        {
            var list = Open();
            Push(list, 3);

            Assert.Throws<ArgumentNullException>(() => list.ScrollIntoView(null));
            Assert.Throws<ArgumentException>(() => list.ScrollIntoView(_screen.Get("box")));
            Assert.Throws<ArgumentException>(() => list.ScrollIntoView(list));
        }

        // ───── following a target that moves ─────

        [Test]
        public void A_growing_target_is_followed_until_it_settles()
        {
            var list = Open();
            Push(list, 100);
            var row = list.Slots[20];
            list.ScrollIntoView(row);

            SetHeight(row, 60f);   // the row unfolds…
            Tick(list);
            AssertFullyVisible(list, row, "after the first frame of growth");
            SetHeight(row, 90f);
            Tick(list);
            AssertFullyVisible(list, row, "after the second");
            Assert.AreEqual(4f + 32f * 20f + 90f - 200f, S(list), 0.5f);

            for (var i = 0; i < ScrollList.FollowSettleTicks; i++) Tick(list);   // …and stays put
            var settled = S(list);
            SetHeight(row, 150f);
            Tick(list);

            Assert.AreEqual(settled, S(list), 0.01f, "once settled the list no longer follows");
        }

        [Test]
        public void The_user_scrolling_ends_the_follow()
        {
            var list = Open();
            Push(list, 100);
            var row = list.Slots[20];
            list.ScrollIntoView(row);

            var scrollRect = list.GameObject.GetComponent<PuiScrollRect>();
            scrollRect.OnScroll(new UnityEngine.EventSystems.PointerEventData(null) { scrollDelta = new Vector2(0f, -1f) });
            Tick(list);
            var wheeled = S(list);
            Assume.That(wheeled, Is.Not.EqualTo(34f + 32f * 20f - 200f).Within(0.5f), "guard: the wheel moved the list");
            SetHeight(row, 90f);
            Tick(list);

            Assert.AreEqual(wheeled, S(list), 0.01f, "the user's scroll position is kept");
        }

        [Test]
        public void Motion_that_is_not_the_users_does_not_end_the_follow()
        {
            // The ScrollRect's elasticity — a hugging list growing under the content, a bounce — moves Content too.
            var list = Open();
            Push(list, 100);
            var row = list.Slots[20];
            list.ScrollIntoView(row);

            ContentOf(list).anchoredPosition -= new Vector2(0f, 30f);   // no input event: not the user
            SetHeight(row, 90f);
            Tick(list);

            Assert.AreEqual(4f + 32f * 20f + 90f - 200f, S(list), 0.5f, "still followed, and put back in place");
        }

        [Test]
        public void Another_scroll_request_ends_the_follow()
        {
            var list = Open();
            Push(list, 100);
            var row = list.Slots[20];
            list.ScrollIntoView(row);

            list.ScrollToStart();
            SetHeight(row, 90f);
            Tick(list);

            Assert.AreEqual(0f, S(list), 0.5f);
        }
    }
}
