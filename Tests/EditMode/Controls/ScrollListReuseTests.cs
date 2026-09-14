using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>ScrollList.BindItems</c> 行复用（2026-09-14 scrolllist-row-reuse spec）：推送不再整表销毁重建，
    /// 第 i 项绑到第 i 行——数量不变零实例化、+k 只建 k 张、−k 只销毁尾巴；复用前释放行上 <c>.AddTo(slot)</c>
    /// 的订阅袋。退回整表重建的四种情形：静态占位卡在场的首次绑定、<c>itemTemplate</c> 名变了、
    /// 某行被外部销毁（只重建那一行并钉回原位）、<c>reuseItems="false"</c>。
    /// </summary>
    public class ScrollListReuseTests
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

        /// <summary>推 <paramref name="count"/> 项（文本 "prefix+i"），返回本次 bind 回调拿到的行。</summary>
        private static List<IControl> Push(ScrollList list, int count, string prefix = "i",
                                           Action<IControl, string> extra = null)
        {
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = prefix + i;
            var rows = new List<IControl>();
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(items),
                (IControl slot, string s) =>
                {
                    rows.Add(slot);
                    slot.Get<Text>("label").TextValue = s;
                    extra?.Invoke(slot, s);
                });
            Canvas.ForceUpdateCanvases();
            Assert.AreEqual(count, rows.Count, "guard: bind ran once per item");
            return rows;
        }

        private sealed class CountingDisposable : IDisposable
        {
            public int Disposed;
            public void Dispose() => Disposed++;
        }

        // ───── 位置复用 ─────

        [Test]
        public void Same_count_rebind_reuses_every_row()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'/>")
                .Get<ScrollList>("sl");
            var first = Push(list, 3, "a");
            var gos = new[] { first[0].GameObject, first[1].GameObject, first[2].GameObject };

            var second = Push(list, 3, "b");

            for (var i = 0; i < 3; i++)
            {
                Assert.AreSame(first[i], second[i], $"row {i} is the same IControl");
                Assert.AreSame(gos[i], second[i].GameObject, $"row {i} keeps its GameObject");
                Assert.AreEqual("b" + i, LabelOf(second[i]), "bind wrote the new value");
            }
            Assert.AreEqual(3, ContentOf(list).childCount, "nothing was instantiated or destroyed");
            Assert.AreEqual(3, list.SlotCount);
        }

        [Test]
        public void More_items_instantiate_only_the_extra()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'/>")
                .Get<ScrollList>("sl");
            var first = Push(list, 3);

            var second = Push(list, 5);

            for (var i = 0; i < 3; i++) Assert.AreSame(first[i], second[i], $"row {i} reused");
            Assert.IsFalse(first.Contains(second[3]), "row 3 is new");
            Assert.IsFalse(first.Contains(second[4]), "row 4 is new");
            Assert.AreEqual(5, ContentOf(list).childCount);
            Assert.AreEqual(5, list.SlotCount);
        }

        [Test]
        public void Fewer_items_dispose_only_the_tail()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'/>")
                .Get<ScrollList>("sl");
            var first = Push(list, 5);

            var second = Push(list, 2);

            Assert.AreSame(first[0], second[0]);
            Assert.AreSame(first[1], second[1]);
            Assert.IsFalse(first[0].GameObject == null);
            Assert.IsFalse(first[1].GameObject == null);
            for (var i = 2; i < 5; i++)
                Assert.IsTrue(first[i].GameObject == null, $"row {i} is the tail: destroyed (EditMode = immediately)");
            Assert.AreEqual(2, ContentOf(list).childCount);
            Assert.AreEqual(2, list.SlotCount);
        }

        [Test]
        public void Rebind_releases_row_subscriptions_before_bind()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'/>")
                .Get<ScrollList>("sl");
            var subs = new Dictionary<IControl, CountingDisposable>();
            var disposedBeforeSecondBind = new List<bool>();
            var pushes = 0;

            void Extra(IControl slot, string _)
            {
                if (pushes == 1 && subs.TryGetValue(slot, out var prev))
                    disposedBeforeSecondBind.Add(prev.Disposed == 1);
                var d = new CountingDisposable();
                d.AddTo(slot);
                subs[slot] = d;
            }

            var first = Push(list, 3, "a", Extra);
            var firstSubs = new[] { subs[first[0]], subs[first[1]], subs[first[2]] };
            pushes = 1;
            Push(list, 3, "b", Extra);

            CollectionAssert.AreEqual(new[] { true, true, true }, disposedBeforeSecondBind,
                "the previous push's .AddTo(slot) bag is released BEFORE the row is bound again");
            foreach (var d in firstSubs) Assert.AreEqual(1, d.Disposed, "released exactly once");
            foreach (var r in first) Assert.AreEqual(0, subs[r].Disposed, "the new push's bag is live");
        }

        // ───── 退回整表重建 ─────

        [Test]
        public void First_bind_still_destroys_static_placeholders()
        {
            var screen = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'>"
                            + "<Frame id='a' height='30'/><Frame id='b' height='30'/></ScrollList>");
            var list = screen.Get<ScrollList>("sl");
            var staticA = screen.Get<Frame>("a");
            var staticB = screen.Get<Frame>("b");

            var first = Push(list, 3);
            Assert.IsTrue(staticA.GameObject == null, "the placeholder is gone (2026-09-10 rule, unchanged)");
            Assert.IsTrue(staticB.GameObject == null);
            Assert.AreEqual(3, ContentOf(list).childCount);

            var second = Push(list, 3);
            for (var i = 0; i < 3; i++) Assert.AreSame(first[i], second[i], "the bound rows are reused");
            Assert.AreEqual(3, ContentOf(list).childCount, "the placeholders do not come back");
        }

        [Test]
        public void ItemTemplate_change_between_binds_rebuilds_all()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'/>",
                            RowTemplate
                            + "<Template name='Row2'><Frame height='30'><Text id='label'>y</Text>"
                            + "<Image id='badge'/></Frame></Template>")
                .Get<ScrollList>("sl");
            var first = Push(list, 3);

            list.ItemTemplate = "Row2";
            var second = Push(list, 3);

            for (var i = 0; i < 3; i++)
            {
                Assert.IsTrue(first[i].GameObject == null, $"old row {i} destroyed");
                Assert.IsFalse(first.Contains(second[i]), $"new row {i} is a new instance");
                Assert.IsNotNull(second[i].Get<Image>("badge"), $"new row {i} comes from Row2");
            }
            Assert.AreEqual(3, ContentOf(list).childCount);
        }

        [Test]
        public void Same_template_name_reassigned_still_reuses()
        {
            // ReSolve replays every [UIAttr] (a new factory delegate each time) — the name is what
            // decides, not the delegate.
            var screen = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'/>");
            var list = screen.Get<ScrollList>("sl");
            var first = Push(list, 3);

            screen.ReSolve();
            var second = Push(list, 3);

            for (var i = 0; i < 3; i++) Assert.AreSame(first[i], second[i]);
        }

        [Test]
        public void Externally_destroyed_row_is_rebuilt_in_place()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'/>")
                .Get<ScrollList>("sl");
            var first = Push(list, 3);

            first[1].Dispose();
            Assume.That(first[1].GameObject == null, "guard: EditMode destroys immediately");
            var second = Push(list, 3);

            Assert.AreSame(first[0], second[0]);
            Assert.AreSame(first[2], second[2]);
            Assert.AreNotSame(first[1], second[1], "the dead row was rebuilt");
            Assert.IsFalse(second[1].GameObject == null);
            Assert.AreEqual(1, second[1].RectTransform.GetSiblingIndex(), "pinned back to its slot");
            Assert.AreEqual(3, ContentOf(list).childCount);
            Assert.AreEqual(3, list.SlotCount);
        }

        [Test]
        public void ReuseItems_false_rebuilds_all()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row' reuseItems='false'/>")
                .Get<ScrollList>("sl");
            var first = Push(list, 3);

            var second = Push(list, 3);

            for (var i = 0; i < 3; i++)
            {
                Assert.IsTrue(first[i].GameObject == null, $"old row {i} destroyed");
                Assert.IsFalse(first.Contains(second[i]), $"row {i} is a new instance");
            }
            Assert.AreEqual(3, ContentOf(list).childCount);
        }

        // ───── 顺序与 ReSolve ─────

        [Test]
        public void Grid_mode_keeps_sibling_order_after_rebind()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'"
                            + " columns='2' cellSize='40x40'/>")
                .Get<ScrollList>("sl");

            foreach (var count in new[] { 3, 5, 4 })
            {
                var rows = Push(list, count);
                for (var i = 0; i < count; i++)
                    Assert.AreEqual(i, rows[i].RectTransform.GetSiblingIndex(), $"count={count}: row {i}");
                Assert.AreEqual(count, ContentOf(list).childCount);
            }
        }

        [Test]
        public void Reused_rows_still_follow_ReSolve()
        {
            var list = Open("<ScrollList id='sl' width='150' height='200' itemTemplate='Row'/>",
                            "<Template name='Row'><Frame height='30'><Text id='label'>x</Text>"
                            + "<Image id='bg' color='#112233' color.alt='#445566'/></Frame></Template>")
                .Get<ScrollList>("sl");
            Push(list, 3, "a");
            var rows = Push(list, 3, "b");
            Assume.That(ColorOf(rows[0]), Is.EqualTo("112233"));

            UI.Variants.Set("alt", true);

            foreach (var r in rows)
                Assert.AreEqual("445566", ColorOf(r), "a reused row is still a registered dynamic subtree");
        }

        private static string LabelOf(IControl row) =>
            row.Get<Text>("label").GameObject.GetComponent<TMP_Text>().text;

        private static string ColorOf(IControl row) =>
            ColorUtility.ToHtmlStringRGB(row.Get<Image>("bg").GameObject.GetComponent<UnityImage>().color);
    }
}
