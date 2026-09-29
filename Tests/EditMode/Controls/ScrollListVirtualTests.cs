using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Image = PromptUGUI.Controls.Image;
using Text = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// 虚拟列表的窗口同步（2026-09-29 scrolllist-virtualization spec §5）：只为视口附近的项实现行，Content 用
    /// <c>Leading</c> / <c>Trailing</c> 撑出虚拟总高；滚动时离窗的行顶给进窗的项，多余的停放进 Pool，窗口变大时先从池里取。
    /// EditMode 不跑 LateUpdate —— 滚动后显式 <c>RefreshWindow()</c>。
    /// </summary>
    public class ScrollListVirtualTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string RowTemplate =
            "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        // A row whose height follows its text: the long items wrap to several lines.
        private const string WrapTemplate =
            "<Template name='Wrap'><HStack width='stretch'><Text id='label' width='stretch' wrap='true' fontSize='20'/></HStack></Template>";

        private PromptUGUI.Application.Screen _screen;

        private ScrollList Open(string attrs = "", string children = "", string templates = RowTemplate,
                                string itemTemplate = "Row")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + templates
                    + "<Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>"
                    + $"<ScrollList id='sl' width='150' height='200' itemTemplate='{itemTemplate}' virtualize='true'"
                    + $" spacing='2' padding='4' {attrs}>{children}</ScrollList>"
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

        private static RectTransform PoolOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Pool");

        private static RectTransform HostOf(IControl c) => (c as Control)?.LayoutHost ?? c.RectTransform;

        // Distance from Content's top edge to a laid-out row's top edge.
        private static float TopOf(IControl row)
        {
            var h = HostOf(row);
            return -h.anchoredPosition.y - h.rect.height * (1f - h.pivot.y);
        }

        private static float S(ScrollList sl) => ContentOf(sl).anchoredPosition.y;

        private static void ScrollTo(ScrollList sl, float s)
        {
            var content = ContentOf(sl);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, s);
            sl.RefreshWindow();
        }

        private static string LabelOf(IControl row) =>
            row.Get<Text>("label").GameObject.GetComponent<TMP_Text>().text;

        private static string[] Items(int count, string prefix = "i")
        {
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = prefix + i;
            return items;
        }

        /// <summary>Binds through one Subject so later pushes come from the same binding; counts bind calls.</summary>
        private sealed class Feed
        {
            public readonly Subject<IReadOnlyList<string>> Subject = new();
            public int Binds;

            public Feed(ScrollList list, bool keyed = false)
            {
                if (keyed)
                    list.BindItems(Subject, (IControl row, string s) => Bind(row, s), key: s => s);
                else
                    list.BindItems(Subject, (IControl row, string s) => Bind(row, s));
            }

            private void Bind(IControl row, string s)
            {
                Binds++;
                row.Get<Text>("label").TextValue = s;
            }

            public void Push(params string[] items) => Subject.OnNext(items);
        }

        // ───── the window ─────

        [Test]
        public void Push_1000_realizes_only_the_window()
        {
            var list = Open();
            new Feed(list).Push(Items(1000));

            var viewport = ViewportOf(list).rect.height;
            Assert.LessOrEqual(list.SlotCount, Mathf.CeilToInt(viewport / 32f) + 3, "rows ≈ the viewport, not the list");
            Assert.AreEqual(list.SlotCount, ContentOf(list).childCount, "Content holds the window and nothing else");
            Assert.AreEqual(1000, list.ItemCount);
        }

        [Test]
        public void Content_height_is_the_virtual_total()
        {
            var list = Open();
            new Feed(list).Push(Items(1000));

            Assert.AreEqual(4f + 1000f * 30f + 999f * 2f + 4f, ContentOf(list).rect.height, 0.5f);
        }

        [Test]
        public void Rows_sit_at_their_item_offsets()
        {
            var list = Open();
            new Feed(list).Push(Items(1000));

            for (var j = 0; j < list.Slots.Count; j++)
            {
                var row = list.Slots[j];
                var index = int.Parse(LabelOf(row).Substring(1));
                Assert.AreEqual(4f + 32f * index, TopOf(row), 0.5f, $"row for item {index}");
            }
        }

        [Test]
        public void Scrolling_moves_the_window_and_reuses_rows()
        {
            var list = Open();
            new Feed(list).Push(Items(1000));
            var before = new List<IControl>(list.Slots);

            ScrollTo(list, 4f + 32f * 500f);

            var first = int.Parse(LabelOf(list.Slots[0]).Substring(1));
            Assert.That(first, Is.InRange(497, 500), "the window starts just above item 500");
            for (var j = 1; j < list.Slots.Count; j++)
                Assert.AreEqual("i" + (first + j), LabelOf(list.Slots[j]), "consecutive, in sibling order");
            for (var j = 0; j < list.Slots.Count; j++)
                Assert.AreEqual(j, HostOf(list.Slots[j]).GetSiblingIndex());
            foreach (var row in before)
                Assert.IsTrue(list.Slots.Contains(row), "every row of the old window was reused for the new one");
            // Mid-list the margin has items above the viewport too, so the window may be a row larger than at
            // the top — but there are never more rows than the window needs.
            Assert.AreEqual(list.SlotCount, _screen.LiveDynamicSubtreeCount);
            Assert.AreEqual(4f + 32f * first, TopOf(list.Slots[0]), 0.5f);
        }

        [Test]
        public void Scroll_binds_only_rows_entering_the_window()
        {
            var list = Open();
            var feed = new Feed(list);
            feed.Push(Items(1000));
            ScrollTo(list, 32f * 100f);
            feed.Binds = 0;

            ScrollTo(list, 32f * 101f);

            Assert.That(feed.Binds, Is.InRange(1, 2), "one row left the top, one entered at the bottom");
        }

        [Test]
        public void Push_rebinds_every_realized_row()
        {
            var list = Open();
            var feed = new Feed(list);
            feed.Push(Items(1000));
            feed.Binds = 0;

            feed.Push(Items(1000));

            Assert.AreEqual(list.SlotCount, feed.Binds);
        }

        [Test]
        public void Shrinking_the_list_parks_rows_and_growing_reuses_them()
        {
            var list = Open();
            new Feed(list).Push(Items(1000));
            var rowsBefore = list.SlotCount;
            var liveBefore = _screen.LiveDynamicSubtreeCount;

            list.RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 60f);
            list.RefreshWindow();

            Assert.Less(list.SlotCount, rowsBefore);
            var pool = PoolOf(list);
            Assert.IsNotNull(pool);
            Assert.IsFalse(pool.gameObject.activeSelf, "the pool is inactive");
            Assert.AreEqual(rowsBefore - list.SlotCount, pool.childCount, "the rows that left are parked");
            for (var i = 0; i < pool.childCount; i++)
                Assert.IsTrue(pool.GetChild(i).gameObject.activeSelf, "a parked row keeps its own activeSelf");

            list.RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 200f);
            list.RefreshWindow();

            Assert.AreEqual(rowsBefore, list.SlotCount);
            Assert.AreEqual(0, pool.childCount, "the pool was drained first");
            Assert.AreEqual(liveBefore, _screen.LiveDynamicSubtreeCount, "no row was instantiated");
        }

        [Test]
        public void First_push_destroys_static_placeholders()
        {
            var list = Open("", "<Frame id='ph' height='30'/>");
            var placeholder = _screen.Get<Frame>("ph");

            new Feed(list).Push(Items(100));

            Assert.IsTrue(placeholder.GameObject == null);
            Assert.AreEqual("i0", LabelOf(list.Slots[0]));
        }

        [Test]
        public void ItemTemplate_change_rebuilds_the_window()
        {
            var list = Open("", "", RowTemplate
                + "<Template name='Row2'><Frame height='30'><Text id='label'>y</Text><Image id='badge'/></Frame></Template>");
            var feed = new Feed(list);
            feed.Push(Items(100));
            var first = new List<IControl>(list.Slots);

            list.ItemTemplate = "Row2";
            feed.Push(Items(100));

            foreach (var old in first) Assert.IsTrue(old.GameObject == null, "old rows destroyed");
            foreach (var row in list.Slots) Assert.IsNotNull(row.Get<Image>("badge"), "rows come from Row2");
        }

        [Test]
        public void Dispose_releases_realized_and_parked_rows()
        {
            var list = Open();
            new Feed(list).Push(Items(1000));
            var realized = new List<IControl>(list.Slots);
            list.RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 60f);
            list.RefreshWindow();

            list.Dispose();

            foreach (var row in realized) Assert.IsTrue(row.GameObject == null);
        }

        [Test]
        public void A_push_from_inside_bind_runs_after_the_current_sync()
        {
            var list = Open();
            var subject = new Subject<IReadOnlyList<string>>();
            var reentered = false;
            list.BindItems(subject, (IControl row, string s) =>
            {
                row.Get<Text>("label").TextValue = s;
                if (reentered) return;
                reentered = true;
                subject.OnNext(Items(50, "b"));   // a push while the list is binding
            });

            subject.OnNext(Items(1000, "a"));

            Assert.AreEqual(50, list.ItemCount, "the second push won");
            Assert.AreEqual("b0", LabelOf(list.Slots[0]));
            Assert.AreEqual(list.SlotCount, ContentOf(list).childCount);
            for (var j = 0; j < list.Slots.Count; j++)
                Assert.AreEqual("b" + j, LabelOf(list.Slots[j]));
        }

        [Test]
        public void Spacing_variant_moves_the_offsets()
        {
            var list = Open("spacing.alt='10'");
            new Feed(list).Push(Items(100));

            UI.Variants.Set("alt", true);
            list.RefreshWindow();

            for (var j = 0; j < list.Slots.Count; j++)
                Assert.AreEqual(4f + 40f * j, TopOf(list.Slots[j]), 0.5f, $"row {j} at the new stride");
        }

        [Test]
        public void The_scrollbar_follows_every_sync()
        {
            var list = Open();
            new Feed(list).Push(Items(1000));
            var scroll = list.GameObject.GetComponent<ScrollRect>();

            Assert.AreEqual(1f, scroll.verticalScrollbar.value, 0.001f, "at the top after the first push");

            Canvas.ForceUpdateCanvases();   // the ScrollRect's own layout pass must not read a stale bar back
            Assert.AreEqual(0f, S(list), 0.5f);

            ScrollTo(list, 32f * 500f);
            Assert.AreEqual(scroll.verticalNormalizedPosition, scroll.verticalScrollbar.value, 0.001f);
        }

        [Test]
        public void An_empty_push_leaves_no_rows()
        {
            var list = Open();
            var feed = new Feed(list);
            feed.Push(Items(100));

            feed.Push();

            Assert.AreEqual(0, list.SlotCount);
            Assert.AreEqual(0, ContentOf(list).childCount);
            Assert.AreEqual(8f, ContentOf(list).rect.height, 0.5f, "just the padding");
        }

        // ───── anchoring (§5.4) ─────

        private static string[] WrapItems(int count)
        {
            var items = new string[count];
            for (var i = 0; i < count; i++)
                items[i] = i % 3 == 0 ? $"i{i} a long message that wraps onto a few lines in a narrow list" : $"i{i}";
            return items;
        }

        private static string[] Shifted(int count, int by)
        {
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = "i" + (i + by);
            return items;
        }

        /// <summary>The first realized row that intersects the viewport — what the user sees at the top.</summary>
        private static (string label, float delta) FirstVisible(ScrollList list)
        {
            var s = S(list);
            foreach (var row in list.Slots)
            {
                var top = TopOf(row);
                if (top + HostOf(row).rect.height > s) return (LabelOf(row), top - s);
            }
            return (null, 0f);
        }

        private static IControl RowShowing(ScrollList list, string label)
        {
            foreach (var row in list.Slots)
                if (LabelOf(row) == label) return row;
            return null;
        }

        private static float EndOf(ScrollList list) =>
            Mathf.Max(0f, ContentOf(list).rect.height - ViewportOf(list).rect.height);

        [Test]
        public void Scrolling_up_into_estimated_rows_keeps_the_anchor_row_in_place()
        {
            var list = Open("", "", RowTemplate + WrapTemplate, "Wrap");
            new Feed(list).Push(WrapItems(1000));
            ScrollTo(list, 15000f);

            // The user scrolls up: rows that were never measured come into view above.
            var content = ContentOf(list);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, S(list) - 150f);
            var (label, delta) = FirstVisible(list);
            Assume.That(label, Is.Not.Null);

            list.RefreshWindow();

            var row = RowShowing(list, label);
            Assert.IsNotNull(row, "the row the user was looking at is still realized");
            Assert.AreEqual(delta, TopOf(row) - S(list), 0.5f, "and it did not move on screen");
        }

        [Test]
        public void Keyed_trim_front_in_the_middle_keeps_the_anchor()
        {
            var list = Open();
            var feed = new Feed(list, keyed: true);
            feed.Push(Items(1000));
            ScrollTo(list, 4f + 32f * 500f + 10f);
            var (label, delta) = FirstVisible(list);

            feed.Push(Shifted(1000, 1));   // the oldest item goes, a new one arrives

            var row = RowShowing(list, label);
            Assert.IsNotNull(row);
            Assert.AreEqual(delta, TopOf(row) - S(list), 0.5f, "the item the user was reading did not move");
        }

        [Test]
        public void A_push_from_a_new_binding_object_is_anchored_like_a_first_push()
        {
            var list = Open();
            new Feed(list, keyed: true).Push(Items(1000));
            ScrollTo(list, 32f * 500f);

            list.BindItems(Observable.Return<IReadOnlyList<string>>(Items(1000)),
                (IControl row, string s) => row.Get<Text>("label").TextValue = s, key: s => s);

            Assert.AreEqual(0f, S(list), 0.5f, "a new binding starts at the top (VIR-P4)");
            Assert.AreEqual("i0", LabelOf(list.Slots[0]));
        }

        [Test]
        public void StickToEnd_starts_at_the_end_and_stays_there_on_push()
        {
            var list = Open("stickToEnd='true'");
            var feed = new Feed(list, keyed: true);
            feed.Push(Items(1000));
            Assert.AreEqual(EndOf(list), S(list), 0.5f, "opens at the newest item");
            Assert.AreEqual("i999", LabelOf(list.Slots[list.Slots.Count - 1]));

            feed.Push(Shifted(1000, 1));

            Assert.AreEqual(EndOf(list), S(list), 0.5f, "still at the end");
            Assert.AreEqual("i1000", LabelOf(list.Slots[list.Slots.Count - 1]));
        }

        [Test]
        public void Stuck_to_end_survives_a_viewport_change_that_did_not_come_from_the_user()
        {
            var list = Open("stickToEnd='true'");
            new Feed(list, keyed: true).Push(Items(1000));
            Assume.That(S(list), Is.EqualTo(EndOf(list)).Within(0.5f));

            // The viewport gets shorter: S has not moved, so geometrically it is no longer at the end — but the
            // user did not scroll away, and the list must still follow the end (VIR-P5).
            list.RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 100f);
            ((IScrollTickHost)list).OnScrollLateUpdate();

            Assert.AreEqual(EndOf(list), S(list), 0.5f);
        }

        [Test]
        public void Scrolling_away_from_the_end_stops_following_it()
        {
            var list = Open("stickToEnd='true'");
            var feed = new Feed(list, keyed: true);
            feed.Push(Items(1000));

            ScrollTo(list, 32f * 500f);
            var (label, delta) = FirstVisible(list);
            feed.Push(Shifted(1000, 1));

            Assert.Less(S(list), EndOf(list) - 100f, "not dragged back to the end");
            Assert.AreEqual(delta, TopOf(RowShowing(list, label)) - S(list), 0.5f);
        }

        [Test]
        public void Keyed_prepend_at_the_start_with_stickToEnd_keeps_the_old_first_row()
        {
            var list = Open("stickToEnd='true'");
            var feed = new Feed(list, keyed: true);
            feed.Push(Items(1000));
            ScrollTo(list, 0f);   // up to the oldest message, to load older ones
            Assume.That(LabelOf(list.Slots[0]), Is.EqualTo("i0"));

            var older = new List<string>();
            for (var i = 50; i > 0; i--) older.Add("h" + i);
            older.AddRange(Items(1000));
            feed.Push(older.ToArray());

            var row = RowShowing(list, "i0");
            Assert.IsNotNull(row, "the row the user was looking at is still there");
            Assert.AreEqual(4f, TopOf(row) - S(list), 0.5f, "and still at the top of the viewport (padding 4)");
        }

        [Test]
        public void Keyed_prepend_at_the_start_without_stickToEnd_stays_at_the_start()
        {
            var list = Open();
            var feed = new Feed(list, keyed: true);
            feed.Push(Items(1000));

            var newer = new List<string> { "n1", "n0" };
            newer.AddRange(Items(1000));
            feed.Push(newer.ToArray());

            Assert.AreEqual(0f, S(list), 0.5f);
            Assert.AreEqual("n1", LabelOf(list.Slots[0]), "a feed that sits at the top shows what arrived");
        }

        // ───── invalidation and pending work (§5.9 – §5.11) ─────

        private static void Tick(ScrollList list) => ((IScrollTickHost)list).OnScrollLateUpdate();

        private static void AssertRowsStacked(ScrollList list)
        {
            for (var j = 1; j < list.Slots.Count; j++)
            {
                var previous = HostOf(list.Slots[j - 1]);
                if (!previous.gameObject.activeSelf) continue;
                Assert.AreEqual(TopOf(list.Slots[j - 1]) + previous.rect.height + 2f, TopOf(list.Slots[j]), 0.5f,
                    $"row {j} follows row {j - 1}");
            }
        }

        [Test]
        public void Width_change_remeasures_the_visible_rows()
        {
            var list = Open("", "", RowTemplate + WrapTemplate, "Wrap");
            new Feed(list).Push(WrapItems(1000));
            var tallBefore = HostOf(list.Slots[0]).rect.height;   // item 0 is a long, wrapping one

            list.RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, 100f);
            Tick(list);

            Assert.Greater(HostOf(list.Slots[0]).rect.height, tallBefore + 1f, "narrower → the long item wraps more");
            AssertRowsStacked(list);
            Assert.AreEqual(ContentOf(list).rect.height,
                TopOf(list.Slots[list.Slots.Count - 1]) + HostOf(list.Slots[list.Slots.Count - 1]).rect.height
                + ContentOf(list).GetComponent<WindowedVerticalLayoutGroup>().Trailing + 4f, 1f,
                "the virtual total agrees with the laid-out rows");
        }

        [Test]
        public void ReSolve_marks_heights_stale_and_the_tick_remeasures()
        {
            var list = Open("", "", RowTemplate
                + "<Template name='Big'><HStack width='stretch'><Text id='label' width='stretch' wrap='true'"
                + " fontSize='20' fontSize.alt='40'/></HStack></Template>", "Big");
            new Feed(list).Push(Items(1000));   // one-line labels: at fontSize 40 a row still fits many times
            var before = HostOf(list.Slots[1]).rect.height;

            UI.Variants.Set("alt", true);   // ReSolve replays the rows: a bigger font
            Tick(list);

            Assert.Greater(HostOf(list.Slots[1]).rect.height, before + 1f);
            AssertRowsStacked(list);
            Assert.AreEqual(0f, S(list), 0.5f, "still at the start");
        }

        [Test]
        public void A_push_to_an_inactive_list_binds_nothing_until_it_shows()
        {
            var list = Open();
            var wrap = list.RectTransform.parent.gameObject;   // the 'box' Frame
            wrap.SetActive(false);
            var feed = new Feed(list);

            feed.Push(Items(1000));
            Assert.AreEqual(0, feed.Binds, "a hidden list only keeps the data");
            Assert.AreEqual(1000, list.ItemCount);

            wrap.SetActive(true);   // PuiScrollRect.OnEnable → the list owes a sync
            Tick(list);

            Assert.Greater(feed.Binds, 0);
            Assert.AreEqual("i0", LabelOf(list.Slots[0]));
            Assert.AreEqual(list.SlotCount, ContentOf(list).childCount);
        }

        [Test]
        public void Several_pushes_while_inactive_then_showing_mid_list_keeps_the_anchor()
        {
            var list = Open();
            var feed = new Feed(list, keyed: true);
            feed.Push(Items(1000));
            ScrollTo(list, 4f + 32f * 500f + 7f);
            var (label, delta) = FirstVisible(list);
            var wrap = list.RectTransform.parent.gameObject;
            wrap.SetActive(false);

            feed.Push(Shifted(1000, 1));
            feed.Push(Shifted(1000, 3));
            wrap.SetActive(true);
            Tick(list);

            var row = RowShowing(list, label);
            Assert.IsNotNull(row);
            Assert.AreEqual(delta, TopOf(row) - S(list), 0.5f, "the anchor was carried through both pushes");
        }

        [Test]
        public void First_push_in_the_open_frame_measures_at_the_real_width()
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + WrapTemplate
                    + "<Screen name='S'><Frame id='box' anchor='top-left' width='300' height='400'>"
                    + "<VStack anchor='stretch'><ScrollList id='sl' width='stretch' height='stretch'"
                    + " itemTemplate='Wrap' virtualize='true' spacing='2' padding='4'/></VStack>"
                    + "</Frame></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var list = UI.Open("S").Get<ScrollList>("sl");   // no ForceUpdateCanvases: the VStack has not laid out yet

            new Feed(list).Push(WrapItems(1000));
            var tops = new List<float>();
            foreach (var row in list.Slots) tops.Add(TopOf(row));

            Canvas.ForceUpdateCanvases();
            list.RefreshWindow();

            Assert.AreEqual(tops.Count, list.Slots.Count);
            for (var j = 0; j < tops.Count; j++)
                Assert.AreEqual(tops[j], TopOf(list.Slots[j]), 0.5f, $"row {j} was measured at the final width");
        }

        [Test]
        public void Bind_exception_on_push_is_logged_and_the_window_stays_consistent()
        {
            var list = Open();
            var subject = new Subject<IReadOnlyList<string>>();
            list.BindItems(subject, (IControl row, string s) =>
            {
                row.Get<Text>("label").TextValue = s;
                if (s == "i3") throw new System.InvalidOperationException("boom");
            });

            UnityEngine.TestTools.LogAssert.Expect(LogType.Exception, new System.Text.RegularExpressions.Regex("boom"));
            subject.OnNext(Items(1000));

            Assert.AreEqual(list.SlotCount, ContentOf(list).childCount);
            Assert.AreEqual("i4", LabelOf(list.Slots[4]), "the rows after the failing one were still bound");
            AssertRowsStacked(list);
        }

        [Test]
        public void Bind_exception_on_scroll_is_logged_as_an_error()
        {
            var list = Open();
            var subject = new Subject<IReadOnlyList<string>>();
            list.BindItems(subject, (IControl row, string s) =>
            {
                row.Get<Text>("label").TextValue = s;
                if (s == "i500") throw new System.InvalidOperationException("boom at 500");
            });
            subject.OnNext(Items(1000));

            UnityEngine.TestTools.LogAssert.Expect(LogType.Error, new System.Text.RegularExpressions.Regex("boom at 500"));
            ScrollTo(list, 4f + 32f * 500f);

            Assert.AreEqual(list.SlotCount, ContentOf(list).childCount);
        }

        [Test]
        public void Parked_rows_survive_a_ReSolve()
        {
            var list = Open("", "", RowTemplate
                + "<Template name='Stretchy'><HStack width='stretch' hidden='false'><Text id='label'/></HStack></Template>",
                "Stretchy");
            new Feed(list).Push(Items(1000));
            list.RectTransform.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, 60f);
            list.RefreshWindow();
            var pool = PoolOf(list);
            Assume.That(pool.childCount, Is.GreaterThan(0));
            var parked = pool.childCount;

            Assert.DoesNotThrow(() => _screen.ReSolve(), "a width='stretch' row root replays under the pool's disabled group");

            Assert.AreEqual(parked, pool.childCount, "hidden='false' did not pull a parked row back into Content");
            Assert.AreEqual(list.SlotCount, ContentOf(list).childCount);
        }

        [Test]
        public void A_row_hidden_by_bind_collapses_and_does_not_resync_every_tick()
        {
            var list = Open();
            var subject = new Subject<IReadOnlyList<string>>();
            list.BindItems(subject, (IControl row, string s) =>
            {
                row.Get<Text>("label").TextValue = s;
                row.Hidden = s == "i2";
            });

            UnityEngine.TestTools.LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex("hidden"));
            subject.OnNext(Items(100));
            var syncs = list.SyncCount;
            for (var i = 0; i < 10; i++) Tick(list);

            Assert.AreEqual(syncs, list.SyncCount, "a collapsed row agrees with the model: nothing to redo");
            Assert.AreEqual(4f + 30f + 2f + 30f + 2f, TopOf(list.Slots[3]), 0.5f, "row 3 follows row 1 directly");
            Assert.AreEqual(4f + 99f * 30f + 98f * 2f + 4f, ContentOf(list).rect.height, 0.5f, "one row, one gap less");
        }

        [Test]
        public void A_jump_past_the_window_anchors_on_the_item_at_S()
        {
            var list = Open();
            new Feed(list).Push(Items(1000));

            ScrollTo(list, 4f + 32f * 900f + 5f);   // far below anything realized

            var first = int.Parse(LabelOf(list.Slots[0]).Substring(1));
            Assert.That(first, Is.InRange(897, 900));
            var row = RowShowing(list, "i900");
            Assert.IsNotNull(row);
            Assert.AreEqual(-5f, TopOf(row) - S(list), 0.5f, "item 900 sits where the jump put it");
        }
    }
}
