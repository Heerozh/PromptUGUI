using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Text = PromptUGUI.Controls.Text;

namespace PromptUGUI.Tests.PlayMode.Controls
{
    /// <summary>
    /// 虚拟列表的 Play 模式行为（2026-09-29 scrolllist-virtualization spec）。<c>LateUpdate</c> 只在 Play 模式跑，
    /// 所以 <c>PuiScrollRect</c> 的钩子时机、拖动中平移不变成甩动速度、「用户移动」判据都只能在这里验。
    /// </summary>
    public class ScrollListVirtualPlayTests
    {
        private readonly List<GameObject> _objects = new();

        [SetUp] public void SetUp() => UI.ResetForTests();

        [TearDown]
        public void TearDown()
        {
            foreach (var go in _objects)
                if (go != null) Object.Destroy(go);
            _objects.Clear();
            UI.ResetForTests();
        }

        private sealed class RecordingHost : IScrollTickHost
        {
            private readonly RectTransform _content;
            public int Ticks;
            public float LastY;

            public RecordingHost(RectTransform content) => _content = content;

            public void OnScrollLateUpdate()
            {
                Ticks++;
                LastY = _content.anchoredPosition.y;
            }

            public void OnScrollEnabled() { }
        }

        /// <summary>A bare PuiScrollRect over <paramref name="rows"/> 30-tall rows in a 200×200 viewport.</summary>
        private (PuiScrollRect scroll, RectTransform content) BuildRaw(int rows)
        {
            var canvasGo = new GameObject("Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            _objects.Add(canvasGo);

            var root = new GameObject("Scroll", typeof(RectTransform)).GetComponent<RectTransform>();
            root.SetParent(canvasGo.transform, false);
            root.sizeDelta = new Vector2(200f, 200f);

            var viewport = new GameObject("Viewport", typeof(RectTransform)).GetComponent<RectTransform>();
            viewport.SetParent(root, false);
            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.pivot = new Vector2(0f, 1f);
            viewport.gameObject.AddComponent<RectMask2D>();

            var content = new GameObject("Content", typeof(RectTransform)).GetComponent<RectTransform>();
            content.SetParent(viewport, false);
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = Vector2.zero;
            var group = content.gameObject.AddComponent<VerticalLayoutGroup>();
            group.childControlWidth = group.childControlHeight = true;
            group.childForceExpandWidth = group.childForceExpandHeight = true;
            content.gameObject.AddComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            for (var i = 0; i < rows; i++)
            {
                var row = new GameObject($"Row{i}", typeof(RectTransform));
                row.transform.SetParent(content, false);
                row.AddComponent<LayoutElement>().preferredHeight = 30f;
            }

            var scroll = root.gameObject.AddComponent<PuiScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.inertia = true;
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            return (scroll, content);
        }

        private static PointerEventData PressAtCentre(PuiScrollRect scroll)
        {
            var rt = (RectTransform)scroll.transform;
            var screen = RectTransformUtility.WorldToScreenPoint(null, rt.TransformPoint(rt.rect.center));
            return new PointerEventData(EventSystem.current)
            {
                position = screen,
                pressPosition = screen,
                button = PointerEventData.InputButton.Left,
            };
        }

        // ───── PuiScrollRect ─────

        [UnityTest]
        public IEnumerator Host_tick_runs_after_the_ScrollRect_moved_the_content()
        {
            var (scroll, content) = BuildRaw(50);
            var host = new RecordingHost(content);
            scroll.Host = host;
            yield return null;

            var before = content.anchoredPosition.y;
            scroll.velocity = new Vector2(0f, 2000f);
            host.Ticks = 0;
            yield return null;

            Assert.Greater(host.Ticks, 0, "the hook ran");
            Assert.Greater(host.LastY, before, "it saw this frame's inertia step — it runs after base.LateUpdate");
        }

        [UnityTest]
        public IEnumerator A_shift_while_dragging_does_not_turn_into_fling_velocity()
        {
            var (scroll, _) = BuildRaw(50);
            yield return null;

            var e = PressAtCentre(scroll);
            scroll.OnInitializePotentialDrag(e);
            scroll.OnBeginDrag(e);
            for (var i = 0; i < 10; i++)
            {
                scroll.ShiftContentY(20f);   // the list correcting under a finger that does not move
                scroll.OnDrag(e);
                yield return null;
            }

            Assert.Less(Mathf.Abs(scroll.velocity.y), 50f,
                "shifts made by the list are not finger motion — releasing must not fling");
            scroll.OnEndDrag(e);
        }

        [UnityTest]
        public IEnumerator Content_moved_by_the_user_is_reported_as_user_motion()
        {
            var (scroll, _) = BuildRaw(50);
            yield return null;
            scroll.ConsumeUserMotion();

            scroll.ShiftContentY(10f);
            Assert.IsFalse(scroll.ConsumeUserMotion(), "the list's own write is not user motion");

            scroll.velocity = new Vector2(0f, 1000f);
            yield return null;
            Assert.IsTrue(scroll.ConsumeUserMotion(), "inertia moved it");
            Assert.IsFalse(scroll.ConsumeUserMotion(), "and the mark was consumed");
        }

        // ───── the virtual list in Play mode ─────

        private const string Templates =
            "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>"
            + "<Template name='Wrap'><HStack width='stretch'><Text id='label' width='stretch' wrap='true' fontSize='20'/></HStack></Template>";

        private static ScrollList OpenList(string attrs, string itemTemplate = "Row")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + Templates
                    + "<Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>"
                    + $"<ScrollList id='sl' width='150' height='200' itemTemplate='{itemTemplate}' spacing='2' padding='4' {attrs}/>"
                    + "</Frame></Screen></PromptUGUI>";
            UI.LoadDocument("t", xml);
            return UI.Open("S").Get<ScrollList>("sl");
        }

        private static Subject<IReadOnlyList<string>> Feed(ScrollList list, bool keyed)
        {
            var subject = new Subject<IReadOnlyList<string>>();
            if (keyed)
                list.BindItems(subject, (IControl row, string s) => row.Get<Text>("label").TextValue = s, key: s => s);
            else
                list.BindItems(subject, (IControl row, string s) => row.Get<Text>("label").TextValue = s);
            return subject;
        }

        private static string[] Items(int count, int from = 0)
        {
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = "i" + (from + i);
            return items;
        }

        private static RectTransform ContentOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Viewport/Content");

        private static RectTransform ViewportOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Viewport");

        private static PuiScrollRect ScrollOf(ScrollList sl) => (PuiScrollRect)sl.GameObject.GetComponent<ScrollRect>();

        private static RectTransform HostOf(IControl c) => (c as Control)?.LayoutHost ?? c.RectTransform;

        private static float TopOf(IControl row)
        {
            var h = HostOf(row);
            return -h.anchoredPosition.y - h.rect.height * (1f - h.pivot.y);
        }

        private static float S(ScrollList sl) => ContentOf(sl).anchoredPosition.y;

        private static float End(ScrollList sl) =>
            Mathf.Max(0f, ContentOf(sl).rect.height - ViewportOf(sl).rect.height);

        private static string LabelOf(IControl row) =>
            row.Get<Text>("label").GameObject.GetComponent<TMP_Text>().text;

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

        private static PointerEventData PressAtViewportCentre(ScrollList list)
        {
            var viewport = ViewportOf(list);
            var screen = RectTransformUtility.WorldToScreenPoint(null, viewport.TransformPoint(viewport.rect.center));
            return new PointerEventData(EventSystem.current)
            {
                position = screen,
                pressPosition = screen,
                button = PointerEventData.InputButton.Left,
            };
        }

        private static void AssertWindowCovers(ScrollList list, string when)
        {
            var s = S(list);
            var view = ViewportOf(list).rect.height;
            var first = list.Slots[0];
            var last = list.Slots[list.Slots.Count - 1];
            Assert.LessOrEqual(TopOf(first), s + 0.5f, $"{when}: rows cover the top of the viewport");
            Assert.GreaterOrEqual(TopOf(last) + HostOf(last).rect.height, s + view - 0.5f,
                $"{when}: rows cover the bottom of the viewport");
        }

        [UnityTest]
        public IEnumerator Fling_through_1000_items_never_shows_a_gap()
        {
            var list = OpenList("virtualize='true'");
            Feed(list, keyed: false).OnNext(Items(1000));
            yield return null;

            ScrollOf(list).velocity = new Vector2(0f, 8000f);
            var checkedFrames = 0;
            for (var frame = 0; frame < 60; frame++)
            {
                yield return null;
                if (S(list) <= 0f || S(list) >= End(list)) continue;   // the elastic ends
                checkedFrames++;
                AssertWindowCovers(list, $"frame {frame}");
            }
            Assert.Greater(checkedFrames, 5, "the fling travelled through the list");
        }

        [UnityTest]
        public IEnumerator A_push_during_a_drag_keeps_the_row_under_the_finger()
        {
            var list = OpenList("virtualize='true'");
            var feed = Feed(list, keyed: true);
            feed.OnNext(Items(1000));
            yield return null;
            var content = ContentOf(list);
            content.anchoredPosition = new Vector2(content.anchoredPosition.x, 4f + 32f * 500f);
            yield return null;

            var scroll = ScrollOf(list);
            var e = PressAtViewportCentre(list);
            scroll.OnInitializePotentialDrag(e);
            scroll.OnBeginDrag(e);
            for (var i = 0; i < 10; i++)
            {
                scroll.OnDrag(e);   // the finger rests
                yield return null;
            }
            var (label, delta) = FirstVisible(list);

            feed.OnNext(Items(1000, from: 10));   // ten items trimmed off the front while the finger is down
            yield return null;
            scroll.OnDrag(e);                     // the finger still has not moved
            yield return null;

            var row = RowShowing(list, label);
            Assert.IsNotNull(row);
            Assert.AreEqual(delta, TopOf(row) - S(list), 0.5f, "the row under the finger did not move");
            for (var i = 0; i < 5; i++)
            {
                scroll.OnDrag(e);
                yield return null;
            }
            scroll.OnEndDrag(e);
            Assert.Less(Mathf.Abs(scroll.velocity.y), 300f, "the list's correction did not turn into a fling");
        }

        [UnityTest]
        public IEnumerator StickToEnd_follows_pushes_until_the_user_drags_away()
        {
            var list = OpenList("virtualize='true' stickToEnd='true'");
            var feed = Feed(list, keyed: true);
            feed.OnNext(Items(100));
            yield return null;
            for (var i = 1; i <= 5; i++)
            {
                feed.OnNext(Items(100, from: i));
                yield return null;
                Assert.AreEqual(End(list), S(list), 0.5f, $"push {i}: still at the end");
            }

            var scroll = ScrollOf(list);
            var e = PressAtViewportCentre(list);
            scroll.OnInitializePotentialDrag(e);
            scroll.OnBeginDrag(e);
            e.position += new Vector2(0f, -300f);   // pull the content down, towards older items
            scroll.OnDrag(e);
            yield return null;
            scroll.OnEndDrag(e);
            for (var i = 0; i < 120 && scroll.velocity != Vector2.zero; i++) yield return null;
            Assume.That(S(list), Is.LessThan(End(list) - 100f), "guard: the drag moved away from the end");

            feed.OnNext(Items(100, from: 6));
            yield return null;

            Assert.Less(S(list), End(list) - 100f, "not pulled back to the end");
        }

        [UnityTest]
        public IEnumerator Stuck_to_end_survives_the_scrollbar_appearing_and_rows_rewrapping()
        {
            var list = OpenList("virtualize='true' stickToEnd='true'", "Wrap");
            var feed = Feed(list, keyed: true);
            var items = new List<string>();
            for (var n = 0; n < 12; n++)
            {
                items.Add($"m{n} a message long enough to wrap onto a second line");
                feed.OnNext(items.ToArray());
                yield return null;
                yield return null;   // the bar (AutoHideAndExpandViewport) reacts in the canvas pass
                Assert.AreEqual(End(list), S(list), 1f, $"after {n + 1} messages: still at the end");
            }
        }

        [UnityTest]
        public IEnumerator The_elastic_bounce_at_the_top_plays_out_while_syncing()
        {
            var list = OpenList("virtualize='true'");
            Feed(list, keyed: false).OnNext(Items(1000));
            yield return null;

            ScrollOf(list).velocity = new Vector2(0f, -3000f);   // flung up past the start
            var lowest = 0f;
            for (var i = 0; i < 90; i++)
            {
                yield return null;
                lowest = Mathf.Min(lowest, S(list));
            }

            Assert.Less(lowest, -5f, "the content went past the start — the pull was not snapped away");
            Assert.AreEqual(0f, S(list), 1f, "and sprang back");
        }

        [UnityTest]
        public IEnumerator Dragging_the_scrollbar_through_unmeasured_items_leaves_no_gap()
        {
            var list = OpenList("virtualize='true'", "Wrap");
            var items = new string[1000];
            for (var i = 0; i < items.Length; i++)
                items[i] = i % 3 == 0 ? $"i{i} a longer message that wraps onto another line" : $"i{i}";
            Feed(list, keyed: false).OnNext(items);
            yield return null;

            var bar = ScrollOf(list).verticalScrollbar;
            for (var step = 1; step <= 30; step++)
            {
                bar.value = 1f - step / 30f;   // the thumb moves down the track
                yield return null;
                if (S(list) <= 0f || S(list) >= End(list)) continue;
                AssertWindowCovers(list, $"step {step}");
            }
        }

        [UnityTest]
        public IEnumerator Keyed_trim_front_leaves_no_stale_row_in_Content_the_same_frame()
        {
            var list = OpenList("");   // not virtual
            var feed = Feed(list, keyed: true);
            feed.OnNext(new[] { "a", "b", "c", "d", "e" });
            yield return null;

            feed.OnNext(new[] { "b", "c", "d", "e", "f" });

            // Same frame: the retired row's Destroy has not run yet — it must already be out of Content.
            var content = ContentOf(list);
            Assert.AreEqual(5, content.childCount);
            var labels = new List<string>();
            for (var i = 0; i < content.childCount; i++)
                labels.Add(content.GetChild(i).GetComponentInChildren<TMP_Text>(true).text);
            CollectionAssert.AreEqual(new[] { "b", "c", "d", "e", "f" }, labels);
        }
    }
}
