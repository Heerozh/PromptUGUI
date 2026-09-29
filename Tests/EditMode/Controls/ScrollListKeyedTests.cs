using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Controls;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.TestTools;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// 带 key 的 <c>BindItems</c>（2026-09-29 scrolllist-virtualization spec §5.5）：行跟着项的 key 走 —— 追加只建一行、
    /// 裁头只退役一行、换序只挪兄弟序，其余行原样复用（每行仍在每次推送时重绑）。重复 / null key 拒绝整次推送，列表保持上一次的样子。
    /// 退役的行先移出 Content 再销毁，bind 开始时 Content 里恰好是新的行。
    /// </summary>
    public class ScrollListKeyedTests
    {
        [SetUp] public void SetUp() => UI.ResetForTests();
        [TearDown] public void TearDown() => UI.ResetForTests();

        private const string RowTemplate =
            "<Template name='Row'><Frame height='30'><Text id='label'>x</Text></Frame></Template>";

        private static ScrollList OpenList(string attrs = "", string children = "")
        {
            var xml = "<?xml version='1.0' encoding='utf-8'?>\n<PromptUGUI version='1'>" + RowTemplate
                    + "<Screen name='S'><Frame id='box' anchor='top-left' width='400' height='600'>"
                    + $"<ScrollList id='sl' width='150' height='400' itemTemplate='Row' {attrs}>{children}</ScrollList>"
                    + "</Frame></Screen></PromptUGUI>";
            UI.LoadDocument("test", xml);
            var screen = UI.Open("S");
            Canvas.ForceUpdateCanvases();
            return screen.Get<ScrollList>("sl");
        }

        private static RectTransform ContentOf(ScrollList sl) =>
            (RectTransform)sl.GameObject.transform.Find("Viewport/Content");

        private static RectTransform HostOf(IControl c) => (c as Control)?.LayoutHost ?? c.RectTransform;

        /// <summary>
        /// One push through a FRESH <c>BindItems(Observable.Return(...), …, key)</c> — the common host shape;
        /// keys still line the rows up across bindings (VIR-P4). Returns the rows in data order.
        /// </summary>
        private static List<IControl> PushKeyed(ScrollList list, Action<IControl, string> extra, params string[] keys)
        {
            var rows = new List<IControl>();
            list.BindItems(
                Observable.Return<IReadOnlyList<string>>(keys),
                (IControl row, string s) =>
                {
                    rows.Add(row);
                    row.Get<Text>("label").TextValue = s;
                    extra?.Invoke(row, s);
                },
                key: s => s);
            Canvas.ForceUpdateCanvases();
            return rows;
        }

        private static List<IControl> PushKeyed(ScrollList list, params string[] keys) => PushKeyed(list, null, keys);

        private static string LabelOf(IControl row) =>
            row.Get<Text>("label").GameObject.GetComponent<TMP_Text>().text;

        private static string[] LabelsInSiblingOrder(ScrollList list)
        {
            var content = ContentOf(list);
            var result = new List<string>();
            for (var i = 0; i < content.childCount; i++)
            {
                var tmp = content.GetChild(i).GetComponentInChildren<TMP_Text>(true);
                if (tmp != null) result.Add(tmp.text);
            }
            return result.ToArray();
        }

        private sealed class CountingDisposable : IDisposable
        {
            public int Disposed;
            public void Dispose() => Disposed++;
        }

        // ───── rows follow keys ─────

        [Test]
        public void Keyed_append_keeps_every_row_and_adds_one()
        {
            var list = OpenList();
            var first = PushKeyed(list, "a", "b", "c");

            var second = PushKeyed(list, "a", "b", "c", "d");

            for (var i = 0; i < 3; i++) Assert.AreSame(first[i], second[i], $"row {i} reused");
            Assert.IsFalse(first.Contains(second[3]), "the appended item got a new row");
            Assert.AreEqual(4, ContentOf(list).childCount);
            CollectionAssert.AreEqual(new[] { "a", "b", "c", "d" }, LabelsInSiblingOrder(list));
            Assert.AreEqual(4, list.ItemCount);
        }

        [Test]
        public void Keyed_trim_front_destroys_only_the_first_row()
        {
            var list = OpenList();
            var first = PushKeyed(list, "a", "b", "c", "d");

            var second = PushKeyed(list, "b", "c", "d", "e");

            Assert.AreSame(first[1], second[0]);
            Assert.AreSame(first[2], second[1]);
            Assert.AreSame(first[3], second[2]);
            Assert.IsTrue(first[0].GameObject == null, "the row of the trimmed item is gone");
            Assert.AreEqual(4, ContentOf(list).childCount);
            CollectionAssert.AreEqual(new[] { "b", "c", "d", "e" }, LabelsInSiblingOrder(list));
        }

        [Test]
        public void Keyed_prepend_keeps_old_rows_behind_new_ones()
        {
            var list = OpenList();
            var first = PushKeyed(list, "b", "c");

            var second = PushKeyed(list, "a", "b", "c");

            Assert.IsFalse(first.Contains(second[0]));
            Assert.AreSame(first[0], second[1]);
            Assert.AreSame(first[1], second[2]);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, LabelsInSiblingOrder(list));
        }

        [Test]
        public void Keyed_permutation_moves_rows_with_their_keys()
        {
            var list = OpenList();
            var first = PushKeyed(list, "a", "b", "c");
            var text = new Dictionary<IControl, TMP_Text>();
            foreach (var r in first) text[r] = r.Get<Text>("label").GameObject.GetComponent<TMP_Text>();

            var second = PushKeyed(list, "c", "a", "b");

            Assert.AreSame(first[2], second[0]);
            Assert.AreSame(first[0], second[1]);
            Assert.AreSame(first[1], second[2]);
            foreach (var r in second) Assert.AreEqual(LabelOf(r), text[r].text, "each row kept its own text");
            CollectionAssert.AreEqual(new[] { "c", "a", "b" }, LabelsInSiblingOrder(list));
            for (var i = 0; i < list.Slots.Count; i++)
                Assert.AreEqual(i, HostOf(list.Slots[i]).GetSiblingIndex(),
                    $"slot {i} is sibling {i} — ReorderDriver relies on it");
        }

        [Test]
        public void Keyed_pushes_through_one_subscription_follow_keys_too()
        {
            var list = OpenList();
            var subject = new Subject<IReadOnlyList<string>>();
            var rows = new List<IControl>();
            list.BindItems(subject, (IControl row, string s) =>
            {
                rows.Add(row);
                row.Get<Text>("label").TextValue = s;
            }, key: s => s);

            subject.OnNext(new[] { "a", "b", "c" });
            var first = new List<IControl>(rows);
            rows.Clear();
            subject.OnNext(new[] { "b", "c", "d" });

            Assert.AreSame(first[1], rows[0]);
            Assert.AreSame(first[2], rows[1]);
            Assert.IsTrue(first[0].GameObject == null);
            CollectionAssert.AreEqual(new[] { "b", "c", "d" }, LabelsInSiblingOrder(list));
        }

        [Test]
        public void ReuseItems_false_ignores_the_key_and_rebuilds_all()
        {
            var list = OpenList("reuseItems='false'");
            var first = PushKeyed(list, "a", "b");

            var second = PushKeyed(list, "a", "b");

            for (var i = 0; i < 2; i++) Assert.IsFalse(first.Contains(second[i]), $"row {i} is new");
        }

        [Test]
        public void First_keyed_push_destroys_the_static_placeholders()
        {
            var list = OpenList("", "<Frame id='ph' height='30'/>");
            Assume.That(list.SlotCount, Is.EqualTo(1));

            PushKeyed(list, "a", "b");

            Assert.AreEqual(2, ContentOf(list).childCount);
            CollectionAssert.AreEqual(new[] { "a", "b" }, LabelsInSiblingOrder(list));
        }

        // ───── rejected pushes ─────

        [Test]
        public void Duplicate_key_rejects_the_whole_push()
        {
            var list = OpenList();
            var first = PushKeyed(list, "a", "b", "c");

            LogAssert.Expect(LogType.Exception, new Regex("share the key"));
            var bound = PushKeyed(list, "x", "y", "x");

            Assert.AreEqual(0, bound.Count, "nothing was bound");
            Assert.AreEqual(3, list.ItemCount);
            for (var i = 0; i < 3; i++) Assert.AreSame(first[i], list.Slots[i]);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, LabelsInSiblingOrder(list));
        }

        [Test]
        public void Null_key_rejects_the_whole_push()
        {
            var list = OpenList();
            PushKeyed(list, "a", "b");

            LogAssert.Expect(LogType.Exception, new Regex("null key"));
            var bound = PushKeyed(list, "x", null);

            Assert.AreEqual(0, bound.Count);
            CollectionAssert.AreEqual(new[] { "a", "b" }, LabelsInSiblingOrder(list));
        }

        // ───── bind contract ─────

        [Test]
        public void Every_push_rebinds_every_row_and_releases_row_subscriptions()
        {
            var list = OpenList();
            var bags = new Dictionary<IControl, CountingDisposable>();
            var first = PushKeyed(list, (row, s) =>
            {
                var d = new CountingDisposable();
                d.AddTo(row);
                bags[row] = d;
            }, "a", "b", "c");

            var releasedBeforeRebind = new List<bool>();
            var second = PushKeyed(list, (row, s) => releasedBeforeRebind.Add(bags[row].Disposed == 1), "c", "a", "b");

            Assert.AreEqual(3, second.Count, "every row was bound again");
            CollectionAssert.AreEqual(new[] { true, true, true }, releasedBeforeRebind,
                "a row's .AddTo(row) bag is released before its next bind");
        }

        [Test]
        public void Null_push_is_an_empty_list()
        {
            var list = OpenList();
            var first = PushKeyed(list, "a", "b");

            list.BindItems(Observable.Return<IReadOnlyList<string>>(null), (IControl row, string s) => { }, key: s => s);

            Assert.AreEqual(0, list.ItemCount);
            Assert.AreEqual(0, ContentOf(list).childCount);
            Assert.IsTrue(first[0].GameObject == null);
        }

        [Test]
        public void A_push_after_Dispose_is_ignored()
        {
            var list = OpenList();
            var subject = new Subject<IReadOnlyList<string>>();
            list.BindItems(subject, (IControl row, string s) => { }, key: s => s);
            subject.OnNext(new[] { "a" });

            list.Dispose();
            subject.OnNext(new[] { "b" });   // an unexpected exception log would fail the test

            LogAssert.NoUnexpectedReceived();
        }

        [Test]
        public void Retired_rows_are_out_of_Content_when_bind_runs()
        {
            var list = OpenList();
            PushKeyed(list, "a", "b", "c", "d");

            int? childCountAtFirstBind = null;
            PushKeyed(list, (row, s) => childCountAtFirstBind ??= ContentOf(list).childCount, "c", "d");

            Assert.AreEqual(2, childCountAtFirstBind, "structure is final before any bind runs");
        }
    }
}
