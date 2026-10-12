using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using R3;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// A hugging <c>&lt;ScrollList&gt;</c> (<c>height="hug"</c> / <c>clamp(_, hug, N)</c>) follows its
    /// rows AFTER the list has been laid out once — rows pushed later, rows hidden. The rows mark
    /// Content dirty; the ClampFitter / HugElement that sizes the list lives two levels up, on the
    /// root, so the Viewport between them has to carry the dirty mark up and let the parent's pass
    /// back down to Content (<see cref="LayoutLink"/>). <see cref="HugSizingTests"/> only binds before
    /// the first layout pass, which the root's own pending rebuild happens to cover.
    /// </summary>
    public class ScrollListHugReflowTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private static void Drain() => Canvas.ForceUpdateCanvases();

        private const string RowTemplate = "<Template name='Row'><Frame height='32'/></Template>";

        private static PromptUGUI.Application.Screen Load(string body)
        {
            UI.LoadDocument("test",
                "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>\n" + RowTemplate +
                "<Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>" + body +
                "</Frame></Screen>\n</PromptUGUI>");
            return UI.Open("S");
        }

        private static string List(string attrs, string width = "150") =>
            $"<ScrollList id='list' width='{width}' sprite='' spacing='0' itemTemplate='Row' {attrs}/>";

        private static ReactiveProperty<IReadOnlyList<string>> Bind(ScrollList list, int rows)
        {
            var items = new ReactiveProperty<IReadOnlyList<string>>(Items(rows));
            list.BindItems(items, (IControl slot, string _) => { });
            return items;
        }

        private static string[] Items(int n)
        {
            var items = new string[n];
            for (var i = 0; i < n; i++) items[i] = "r" + i;
            return items;
        }

        private static RectTransform Content(ScrollList list) =>
            (RectTransform)list.RectTransform.Find("Viewport/Content");

        private static bool Linked(ScrollList list)
        {
            var link = list.RectTransform.Find("Viewport").GetComponent<LayoutLink>();
            return link != null && link.enabled;
        }

        // ───── free positioning (ClampFitter) ─────

        [Test]
        public void Free_positioned_list_bound_after_its_first_layout_grows_to_its_rows()
        {
            var list = Load(List("anchor='top-left' height='clamp(_, hug, 200)'")).Get<ScrollList>("list");
            Drain();   // laid out once, still empty
            Assume.That(list.RectTransform.rect.height, Is.EqualTo(0f).Within(0.01f), "empty: nothing to hug");

            Bind(list, rows: 3);
            Drain();

            Assert.AreEqual(96f, list.RectTransform.rect.height, 0.01f, "3 rows of 32");
        }

        [Test]
        public void Free_positioned_list_follows_later_pushes_up_to_its_cap()
        {
            var list = Load(List("anchor='top-left' height='clamp(_, hug, 200)'")).Get<ScrollList>("list");
            var items = Bind(list, rows: 2);
            Drain();
            Assume.That(list.RectTransform.rect.height, Is.EqualTo(64f).Within(0.01f), "bound before the first layout");

            items.Value = Items(4);
            Drain();

            Assert.AreEqual(128f, list.RectTransform.rect.height, 0.01f, "4 rows of 32");

            items.Value = Items(10);
            Drain();

            Assert.AreEqual(200f, list.RectTransform.rect.height, 0.01f, "320 of rows capped at 200");
            Assert.Greater(Content(list).rect.height, 200f, "…and the rest scrolls");
        }

        [Test]
        public void Free_positioned_list_shrinks_when_a_row_is_hidden()
        {
            var list = Load(List("anchor='top-left' height='hug'")).Get<ScrollList>("list");
            Bind(list, rows: 3);
            Drain();
            Assume.That(list.RectTransform.rect.height, Is.EqualTo(96f).Within(0.01f), "bound before the first layout");

            Content(list).GetChild(0).gameObject.SetActive(false);
            Drain();

            Assert.AreEqual(64f, list.RectTransform.rect.height, 0.01f, "2 rows of 32 left");
        }

        // ───── inside a stack (HugElement) ─────

        [Test]
        public void List_in_a_stack_bound_after_its_first_layout_pushes_its_sibling_down()
        {
            var s = Load(
                "<VStack id='outer' anchor='top-left' width='150' height='400' spacing='0'>" +
                List("height='clamp(_, hug, 200)'") +
                "<Btn id='below' height='20'/></VStack>");
            Drain();
            var list = s.Get<ScrollList>("list");
            var below = s.Get<Btn>("below").RectTransform;
            var before = below.anchoredPosition.y;

            Bind(list, rows: 3);
            Drain();

            Assert.AreEqual(96f, list.RectTransform.rect.height, 0.01f, "3 rows of 32");
            Assert.AreEqual(before - 96f, below.anchoredPosition.y, 0.01f, "the sibling moves down by the rows");
        }

        // ───── the <Collapsible> recipe (controls-collapsible.md → Dynamic rows) ─────

        [Test]
        public void List_in_a_collapsible_bound_after_open_opens_the_panel_to_its_rows()
        {
            var s = Load(
                "<Collapsible id='c' anchor='top-right' width='150' headerHeight='24' transition='0'>" +
                List("height='clamp(_, hug, 200)'", width: "stretch") + "</Collapsible>");
            Drain();
            var c = s.Get<Collapsible>("c");
            var list = s.Get<ScrollList>("list");

            Bind(list, rows: 3);
            Drain();

            Assert.AreEqual(96f, list.RectTransform.rect.height, 0.5f, "3 rows of 32");
            Assert.AreEqual(24f + 96f, c.RectTransform.rect.height, 0.5f, "header + the list");
        }

        // ───── only a hugging list is linked ─────

        [Test]
        public void A_list_whose_size_ignores_its_rows_keeps_content_a_layout_root_of_its_own()
        {
            // Its rows cannot change its size, so dirtying the page above it on every row change
            // (a virtual list does that on every scrolled frame) would buy nothing.
            var list = Load(List("anchor='top-left' height='120'")).Get<ScrollList>("list");
            Bind(list, rows: 3);
            Drain();

            Assert.IsFalse(Linked(list));
        }

        // ───── a virtual list: the sync hands the hug up, not every dirty mark ─────
        //
        // A windowing list dirties Content on every frame its window moves. Linking it would re-lay out the
        // page above it on every scrolled frame; only a change of the (clamped) hug answer has to go up.

        private static string VirtualList(string attrs, string width = "150") =>
            $"<ScrollList id='list' width='{width}' sprite='' spacing='0' itemTemplate='Row' virtualize='true' {attrs}/>";

        [Test]
        public void Virtual_list_bound_after_its_first_layout_grows_to_its_rows()
        {
            var list = Load(VirtualList("anchor='top-left' height='clamp(_, hug, 200)'")).Get<ScrollList>("list");
            Drain();

            Bind(list, rows: 3);
            Drain();

            Assert.AreEqual(96f, list.RectTransform.rect.height, 0.01f, "3 rows of 32");
        }

        [Test]
        public void Virtual_list_in_a_stack_follows_pushes_up_to_its_cap()
        {
            var s = Load(
                "<VStack id='outer' anchor='top-left' width='150' height='400' spacing='0'>" +
                VirtualList("height='clamp(_, hug, 200)'") + "<Btn id='below' height='20'/></VStack>");
            Drain();
            var list = s.Get<ScrollList>("list");
            var below = s.Get<Btn>("below").RectTransform;
            var top = below.anchoredPosition.y;

            var items = Bind(list, rows: 2);
            Drain();
            Assert.AreEqual(64f, list.RectTransform.rect.height, 0.01f, "2 rows of 32");

            items.Value = Items(4);
            Drain();
            Assert.AreEqual(128f, list.RectTransform.rect.height, 0.01f, "4 rows of 32");
            Assert.AreEqual(top - 128f, below.anchoredPosition.y, 0.01f, "the sibling follows");

            items.Value = Items(100);
            Drain();
            Assert.AreEqual(200f, list.RectTransform.rect.height, 0.01f, "capped");
        }

        [Test]
        public void Virtual_list_at_its_cap_scrolls_without_dirtying_the_page_above_it()
        {
            var s = Load(
                "<VStack id='outer' anchor='top-left' width='150' height='400' spacing='0'>" +
                VirtualList("height='clamp(_, hug, 200)'") + "<Btn id='below' height='20'/></VStack>");
            Drain();
            var list = s.Get<ScrollList>("list");
            Bind(list, rows: 100);
            Drain();
            list.RefreshWindow();
            Drain();
            Assume.That(list.RectTransform.rect.height, Is.EqualTo(200f).Within(0.01f), "pinned at its cap");
            Assume.That(QueuedLayoutRoots(), Is.Empty, "guard: the queue drained");

            var content = Content(list);
            for (var i = 0; i < 5; i++)
            {
                content.anchoredPosition += new Vector2(0f, 300f);   // well past the realized window
                list.RefreshWindow();

                var outer = s.Get<VStack>("outer").RectTransform;
                foreach (var root in QueuedLayoutRoots())
                    Assert.IsFalse(root == list.RectTransform || root == outer,
                        $"scroll {i}: {root.name} queued — the window's dirty marks reached the page above the list");
                Drain();
            }
            Assert.AreEqual(200f, list.RectTransform.rect.height, 0.01f);
        }

        private static readonly System.Reflection.FieldInfo LayoutQueueField = typeof(UnityEngine.UI.CanvasUpdateRegistry)
            .GetField("m_LayoutRebuildQueue",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        private static List<Transform> QueuedLayoutRoots()
        {
            Assert.IsNotNull(LayoutQueueField,
                "CanvasUpdateRegistry.m_LayoutRebuildQueue is gone — this Unity version needs a new seam");
            // IndexedSet does not enumerate (GetEnumerator throws): Count + the indexer.
            var queue = LayoutQueueField.GetValue(UnityEngine.UI.CanvasUpdateRegistry.instance);
            var type = queue.GetType();
            var count = (int)type.GetProperty("Count").GetValue(queue);
            var item = type.GetProperty("Item");
            var roots = new List<Transform>();
            for (var i = 0; i < count; i++)
            {
                var e = (UnityEngine.UI.ICanvasElement)item.GetValue(queue, new object[] { i });
                if (e.transform != null) roots.Add(e.transform);
            }
            return roots;
        }

        [Test]
        public void Variant_flip_to_hug_links_the_viewport_and_back_unlinks_it()
        {
            var list = Load(List("anchor='top-left' height='120' height.compact='clamp(_, hug, 200)'"))
                .Get<ScrollList>("list");
            var items = Bind(list, rows: 3);
            Drain();
            Assume.That(list.RectTransform.rect.height, Is.EqualTo(120f).Within(0.01f));

            UI.Variants.Set("compact", true);
            Drain();
            Assert.IsTrue(Linked(list), "hug: the rows size the list");
            Assert.AreEqual(96f, list.RectTransform.rect.height, 0.01f);

            items.Value = Items(4);
            Drain();
            Assert.AreEqual(128f, list.RectTransform.rect.height, 0.01f, "and it keeps following them");

            UI.Variants.Set("compact", false);
            Drain();
            Assert.IsFalse(Linked(list), "a number again: unlinked");
            Assert.AreEqual(120f, list.RectTransform.rect.height, 0.01f);
        }
    }
}
