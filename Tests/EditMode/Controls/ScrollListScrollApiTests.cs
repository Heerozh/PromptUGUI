using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using TMPro;
using UnityEngine;
using Text = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// 滚动 API、<c>IsAtEnd</c> / <c>OnAtEndChanged</c>、<c>stickToEnd</c>（2026-09-29 scrolllist-virtualization spec §4.2 /
    /// §5.4），虚拟与非虚拟两种列表各跑一遍。EditMode 不跑 LateUpdate —— tick 显式调。
    /// </summary>
    public class ScrollListScrollApiTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string Templates =
            "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>"
            + "<Template name='Col'><Frame width='40'><Text id='label'>x</Text></Frame></Template>";

        private PromptUGUI.Application.Screen _screen;

        private ScrollList Open(bool virtualize, string attrs = "", string children = "", string itemTemplate = "Row")
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

        private static float TopOf(IControl row)
        {
            var h = HostOf(row);
            return -h.anchoredPosition.y - h.rect.height * (1f - h.pivot.y);
        }

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

        private static string[] Items(int count)
        {
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = "i" + i;
            return items;
        }

        /// <summary>Rows cover the viewport from edge to edge (padding 4 at both ends of the list).</summary>
        private static void AssertViewportCovered(ScrollList list)
        {
            var s = S(list);
            var view = ViewportOf(list).rect.height;
            var content = ContentOf(list).rect.height;
            var first = list.Slots[0];
            var last = list.Slots[list.Slots.Count - 1];
            Assert.LessOrEqual(TopOf(first), Mathf.Max(s, 4f) + 0.5f, "a row reaches the viewport's top edge");
            Assert.GreaterOrEqual(TopOf(last) + HostOf(last).rect.height, Mathf.Min(s + view, content - 4f) - 0.5f,
                "a row reaches the viewport's bottom edge");
        }

        private sealed class Feed
        {
            public readonly Subject<IReadOnlyList<string>> Subject = new();

            public Feed(ScrollList list, bool keyed = false)
            {
                if (keyed)
                    list.BindItems(Subject, (IControl row, string s) => row.Get<Text>("label").TextValue = s, key: s => s);
                else
                    list.BindItems(Subject, (IControl row, string s) => row.Get<Text>("label").TextValue = s);
            }

            public void Push(string[] items) => Subject.OnNext(items);
        }

        // ───── ScrollTo* ─────

        [TestCase(false), TestCase(true)]
        public void ScrollToEnd_and_ScrollToStart(bool virtualize)
        {
            var list = Open(virtualize);
            new Feed(list).Push(Items(100));

            list.ScrollToEnd();

            Assert.Greater(S(list), 0f);
            Assert.AreEqual(EndOf(list), S(list), 0.5f);
            Assert.IsTrue(list.IsAtEnd);
            AssertViewportCovered(list);

            list.ScrollToStart();

            Assert.AreEqual(0f, S(list), 0.5f);
            Assert.IsFalse(list.IsAtEnd);
            AssertViewportCovered(list);
        }

        [TestCase(false), TestCase(true)]
        public void ScrollToIndex_puts_the_item_at_the_top_clamped(bool virtualize)
        {
            var list = Open(virtualize);
            new Feed(list).Push(Items(100));

            list.ScrollToIndex(50);

            Assert.AreEqual(4f + 32f * 50f, S(list), 0.5f, "item 50's top edge at the viewport's top edge");
            Assert.AreEqual(S(list), TopOf(RowShowing(list, "i50")), 0.5f);
            Assert.IsFalse(list.IsAtEnd);

            list.ScrollToIndex(99);

            Assert.AreEqual(EndOf(list), S(list), 0.5f, "clamped: the last item cannot reach the top");
            Assert.IsTrue(list.IsAtEnd);
            AssertViewportCovered(list);

            Assert.Throws<ArgumentOutOfRangeException>(() => list.ScrollToIndex(100));
            Assert.Throws<ArgumentOutOfRangeException>(() => list.ScrollToIndex(-1));
        }

        [TestCase(false), TestCase(true)]
        public void ScrollToIndex_on_a_list_of_static_rows_counts_the_rows(bool virtualize)
        {
            // VIR-P3: before any push ItemCount is 0 — the static rows are what can be scrolled to.
            var list = Open(virtualize, "", "<Frame height='100'/><Frame height='100'/><Frame height='100'/>");

            list.ScrollToIndex(1);

            Assert.AreEqual(4f + 102f, S(list), 0.5f);
            Assert.Throws<ArgumentOutOfRangeException>(() => list.ScrollToIndex(3));
        }

        [Test]
        public void Horizontal_list_end_is_the_right_edge()
        {
            var list = Open(false, "direction='horizontal'", "", "Col");
            new Feed(list).Push(Items(20));
            var content = ContentOf(list);

            list.ScrollToEnd();

            var end = content.rect.width - ViewportOf(list).rect.width;
            Assume.That(end, Is.GreaterThan(0f));
            Assert.AreEqual(-end, content.anchoredPosition.x, 0.5f, "Content slid left until its right edge shows");
            Assert.IsTrue(list.IsAtEnd);

            list.ScrollToIndex(5);

            Assert.AreEqual(-(4f + 42f * 5f), content.anchoredPosition.x, 0.5f, "item 5's left edge at the viewport's left edge");
            Assert.IsFalse(list.IsAtEnd);

            list.ScrollToStart();

            Assert.AreEqual(0f, content.anchoredPosition.x, 0.5f);
        }

        [TestCase(false), TestCase(true)]
        public void ScrollToEnd_on_an_inactive_list_applies_when_it_shows(bool virtualize)
        {
            var list = Open(virtualize);
            new Feed(list).Push(Items(100));
            var box = list.RectTransform.parent.gameObject;
            box.SetActive(false);

            list.ScrollToEnd();
            Assert.AreEqual(0f, S(list), 0.5f, "a hidden list only remembers it");

            box.SetActive(true);
            Tick(list);

            Assert.AreEqual(EndOf(list), S(list), 0.5f);
            Assert.IsTrue(list.IsAtEnd);
        }

        [TestCase(false), TestCase(true)]
        public void ScrollTo_from_inside_bind_runs_after_the_push(bool virtualize)
        {
            var list = Open(virtualize);
            var subject = new Subject<IReadOnlyList<string>>();
            var asked = false;
            list.BindItems(subject, (IControl row, string s) =>
            {
                row.Get<Text>("label").TextValue = s;
                if (asked) return;
                asked = true;
                list.ScrollToEnd();
            });

            subject.OnNext(Items(100));

            Assert.AreEqual(EndOf(list), S(list), 0.5f);
            Assert.AreEqual(list.SlotCount, ContentOf(list).childCount);
            AssertViewportCovered(list);
        }

        // ───── IsAtEnd / OnAtEndChanged ─────

        [TestCase(false), TestCase(true)]
        public void IsAtEnd_is_true_when_the_content_fits(bool virtualize)
        {
            var list = Open(virtualize);
            new Feed(list).Push(Items(3));
            Canvas.ForceUpdateCanvases();

            Assert.IsTrue(list.IsAtEnd);

            var content = ContentOf(list);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, -20f);   // pulled past the start
            Assert.IsTrue(list.IsAtEnd, "content that fits is at the end wherever it is");
        }

        [TestCase(false), TestCase(true)]
        public void OnAtEndChanged_replays_the_current_value_and_dedupes(bool virtualize)
        {
            var list = Open(virtualize);
            new Feed(list).Push(Items(100));
            Canvas.ForceUpdateCanvases();
            Tick(list);
            var seen = new List<bool>();

            using (list.OnAtEndChanged.Subscribe(seen.Add))
            {
                CollectionAssert.AreEqual(new[] { false }, seen, "the current value, on subscribe");

                list.ScrollToEnd();
                list.ScrollToEnd();
                Tick(list);
                list.ScrollToStart();
            }

            CollectionAssert.AreEqual(new[] { false, true, false }, seen);
        }

        // ───── stickToEnd ─────

        [TestCase(false), TestCase(true)]
        public void StickToEnd_opens_at_the_end_and_follows_pushes(bool virtualize)
        {
            var list = Open(virtualize, "stickToEnd='true'");
            var feed = new Feed(list, keyed: true);

            feed.Push(Items(100));
            Tick(list);
            Assert.Greater(S(list), 0f);
            Assert.AreEqual(EndOf(list), S(list), 0.5f, "opens at the newest item");

            feed.Push(Items(101));
            Tick(list);
            Assert.AreEqual(EndOf(list), S(list), 0.5f, "and follows it");
            Assert.IsTrue(list.IsAtEnd);
        }

        [TestCase(false), TestCase(true)]
        public void StickToEnd_stops_following_once_the_user_scrolls_away(bool virtualize)
        {
            var list = Open(virtualize, "stickToEnd='true'");
            var feed = new Feed(list, keyed: true);
            feed.Push(Items(100));
            Tick(list);

            var content = ContentOf(list);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 500f);   // the user drags up
            Tick(list);
            feed.Push(Items(101));
            Tick(list);

            Assert.AreEqual(500f, S(list), 0.5f, "not dragged back to the end");
            Assert.IsFalse(list.IsAtEnd);
        }

        [TestCase(false), TestCase(true)]
        public void StickToEnd_comes_back_with_ScrollToEnd(bool virtualize)
        {
            var list = Open(virtualize, "stickToEnd='true'");
            var feed = new Feed(list, keyed: true);
            feed.Push(Items(100));
            Tick(list);
            var content = ContentOf(list);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 500f);
            Tick(list);

            list.ScrollToEnd();
            feed.Push(Items(101));
            Tick(list);

            Assert.AreEqual(EndOf(list), S(list), 0.5f);
        }

        [TestCase(false), TestCase(true)]
        public void Without_stickToEnd_a_push_keeps_the_offset(bool virtualize)
        {
            var list = Open(virtualize);
            var feed = new Feed(list, keyed: true);
            feed.Push(Items(100));
            list.ScrollToIndex(40);
            var s = S(list);

            feed.Push(Items(101));
            Tick(list);

            Assert.AreEqual(s, S(list), 0.5f);
        }

        [TestCase(false), TestCase(true)]
        public void A_ReSolve_keeps_the_scroll_position(bool virtualize)
        {
            // direction / columns are replayed on every ReSolve like any other attribute.
            var list = Open(virtualize, "direction='vertical' columns='0'");
            new Feed(list).Push(Items(100));
            Canvas.ForceUpdateCanvases();
            var content = ContentOf(list);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 500f);   // the user scrolled
            Tick(list);

            _screen.ReSolve();
            Tick(list);

            Assert.AreEqual(500f, S(list), 0.5f);
        }
    }
}
