using System;
using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Controls;
using PromptUGUI.Controls.Internal;
using UnityEngine;

namespace PromptUGUI.Tests.EditMode.Controls
{
    /// <summary>
    /// <c>ItemBinding</c>（2026-09-29 scrolllist-virtualization spec §5.5）：一次 <c>BindItems</c> 在列表眼里只剩下标 ——
    /// 推送先校验、拍快照，再给出 <c>newToOld</c>（新第 j 项来自当前行的第几项，-1 = 新项）。有 key 时按 key 对齐，
    /// 上一次推送来自任一同 <c>TKey</c> 的 key 绑定也算（VIR-P4）；否则按位置。
    /// </summary>
    public class ItemBindingTests
    {
        private class FakeRow : IControl
        {
            public string Id => null;
            public GameObject GameObject => null;
            public RectTransform RectTransform => null;
            public bool Hidden { get; set; }
            public bool Interactable { get; set; }
            public IReadOnlyDictionary<string, IControl> ScopedIds => new Dictionary<string, IControl>();
            public T Get<T>(string idPath) where T : class, IControl => null;
            public IControl Get(string idPath) => null;
            public void Dispose() { }
        }

        private sealed class OtherRow : FakeRow { }

        private sealed class RowOfA : IControl
        {
            public string Id => null;
            public GameObject GameObject => null;
            public RectTransform RectTransform => null;
            public bool Hidden { get; set; }
            public bool Interactable { get; set; }
            public IReadOnlyDictionary<string, IControl> ScopedIds => new Dictionary<string, IControl>();
            public T Get<T>(string idPath) where T : class, IControl => null;
            public IControl Get(string idPath) => null;
            public void Dispose() { }
        }

        private static IReadOnlyList<string> L(params string[] items) => items;

        private static KeyedBinding<string, FakeRow, string> Keyed() =>
            new KeyedBinding<string, FakeRow, string>((row, s) => { }, s => s);

        private static PositionalBinding<string, FakeRow> Positional() =>
            new PositionalBinding<string, FakeRow>((row, s) => { });

        /// <summary>Accepts a push and returns the meaningful part of newToOld.</summary>
        private static int[] Accept<T>(ItemBinding<T, FakeRow> binding, IReadOnlyList<T> items,
                                       IItemBinding previous, int previousCount)
        {
            var map = new int[1];
            binding.Accept(items, previous, previousCount, ref map);
            var result = new int[items.Count];
            Array.Copy(map, result, items.Count);
            return result;
        }

        // ───── by position ─────

        [Test]
        public void Positional_maps_by_index()
        {
            var b = Positional();
            CollectionAssert.AreEqual(new[] { -1, -1, -1 }, Accept(b, L("a", "b", "c"), null, 0), "first push: all new");
            CollectionAssert.AreEqual(new[] { 0, 1, 2, -1, -1 }, Accept(b, L("a", "b", "c", "d", "e"), b, 3));
            CollectionAssert.AreEqual(new[] { 0, 1 }, Accept(b, L("x", "y"), b, 5));
            Assert.AreEqual(2, b.Count);
        }

        // ───── by key ─────

        [Test]
        public void Keyed_first_push_is_all_new()
        {
            CollectionAssert.AreEqual(new[] { -1, -1 }, Accept(Keyed(), L("a", "b"), null, 0));
        }

        [Test]
        public void Keyed_append()
        {
            var b = Keyed();
            Accept(b, L("a", "b", "c"), null, 0);
            CollectionAssert.AreEqual(new[] { 0, 1, 2, -1 }, Accept(b, L("a", "b", "c", "d"), b, 3));
        }

        [Test]
        public void Keyed_trim_front()
        {
            var b = Keyed();
            Accept(b, L("a", "b", "c"), null, 0);
            CollectionAssert.AreEqual(new[] { 1, 2, -1 }, Accept(b, L("b", "c", "d"), b, 3));
        }

        [Test]
        public void Keyed_prepend()
        {
            var b = Keyed();
            Accept(b, L("b", "c"), null, 0);
            CollectionAssert.AreEqual(new[] { -1, 0, 1 }, Accept(b, L("a", "b", "c"), b, 2));
        }

        [Test]
        public void Keyed_permute()
        {
            var b = Keyed();
            Accept(b, L("a", "b", "c"), null, 0);
            CollectionAssert.AreEqual(new[] { 2, 0, 1 }, Accept(b, L("c", "a", "b"), b, 3));
        }

        [Test]
        public void Keyed_after_another_keyed_binding_with_the_same_key_type_maps_by_key()
        {
            // The common host shape: every push is a fresh BindItems(Observable.Return(list), …, key).
            var first = Keyed();
            Accept(first, L("a", "b", "c"), null, 0);
            var second = Keyed();
            CollectionAssert.AreEqual(new[] { 1, 2, -1 }, Accept(second, L("b", "c", "d"), first, 3));
        }

        [Test]
        public void Keyed_after_a_positional_binding_is_positional()
        {
            var positional = Positional();
            Accept(positional, L("a", "b", "c"), null, 0);
            var keyed = Keyed();
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, Accept(keyed, L("c", "a", "b"), positional, 3),
                "no key table to consult → rows stay where they are");
        }

        [Test]
        public void Positional_after_a_keyed_binding_is_positional()
        {
            var keyed = Keyed();
            Accept(keyed, L("a", "b", "c"), null, 0);
            CollectionAssert.AreEqual(new[] { 0, 1 }, Accept(Positional(), L("x", "y"), keyed, 3));
        }

        [Test]
        public void Duplicate_key_throws_and_keeps_the_last_accepted_push()
        {
            var b = Keyed();
            Accept(b, L("a", "b", "c"), null, 0);

            var map = new int[1];
            var ex = Assert.Throws<ArgumentException>(() => b.Accept(L("a", "b", "a"), b, 3, ref map));
            StringAssert.Contains("share the key", ex.Message);
            StringAssert.Contains("'a'", ex.Message);
            StringAssert.Contains("0", ex.Message);
            StringAssert.Contains("2", ex.Message);
            Assert.AreEqual(3, b.Count, "the rejected push was not taken");

            CollectionAssert.AreEqual(new[] { 2, 0 }, Accept(b, L("c", "a"), b, 3),
                "the next push still diffs against the last ACCEPTED push [a,b,c]");
        }

        [Test]
        public void Null_key_throws()
        {
            var b = Keyed();
            var map = new int[1];
            var ex = Assert.Throws<ArgumentException>(() => b.Accept(L("a", null), null, 0, ref map));
            StringAssert.Contains("null key", ex.Message);
            StringAssert.Contains("1", ex.Message);
            Assert.AreEqual(0, b.Count);
        }

        [Test]
        public void Int_keys_work()
        {
            var b = new KeyedBinding<int, FakeRow, int>((row, i) => { }, i => i);
            Accept(b, new[] { 1, 2, 3 }, null, 0);
            CollectionAssert.AreEqual(new[] { 1, 2, -1 }, Accept(b, new[] { 2, 3, 4 }, b, 3));
        }

        [Test]
        public void Keys_of_a_different_item_type_but_the_same_key_type_still_map()
        {
            var ids = new KeyedBinding<int, FakeRow, int>((row, i) => { }, i => i);
            Accept(ids, new[] { 7, 8 }, null, 0);
            var named = new KeyedBinding<string, FakeRow, int>((row, s) => { }, s => s.Length);
            CollectionAssert.AreEqual(new[] { 1, -1 }, Accept(named, L("12345678", "123"), ids, 2));
        }

        // ───── snapshot + bind ─────

        [Test]
        public void Items_are_snapshotted()
        {
            var bound = new List<string>();
            var b = new PositionalBinding<string, FakeRow>((row, s) => bound.Add(s));
            var host = new List<string> { "a", "b" };
            var map = new int[1];
            b.Accept(host, null, 0, ref map);

            host[0] = "z";
            host.Add("c");

            Assert.AreEqual(2, b.Count, "a later edit of the pushed list does not change the push");
            Assert.IsTrue(b.TryBind(new FakeRow(), 0));
            Assert.AreEqual("a", bound[0]);
        }

        [Test]
        public void TryBind_binds_the_item_at_the_index()
        {
            var bound = new List<string>();
            var b = new PositionalBinding<string, FakeRow>((row, s) => bound.Add(s));
            Accept(b, L("a", "b", "c"), null, 0);
            Assert.IsTrue(b.TryBind(new FakeRow(), 2));
            Assert.IsTrue(b.TryBind(new FakeRow(), 0));
            CollectionAssert.AreEqual(new[] { "c", "a" }, bound);
        }

        [Test]
        public void TryBind_returns_false_for_the_wrong_slot_type()
        {
            var b = new PositionalBinding<string, OtherRow>((row, s) => { });
            var map = new int[1];
            b.Accept(L("a"), null, 0, ref map);
            Assert.IsFalse(b.TryBind(new FakeRow(), 0), "a FakeRow is not an OtherRow");
            Assert.IsFalse(b.TryBind(new RowOfA(), 0));
            Assert.IsTrue(b.TryBind(new OtherRow(), 0));
            Assert.AreEqual(typeof(OtherRow), b.SlotType);
        }

        [Test]
        public void Accept_grows_the_map_buffer()
        {
            var b = Positional();
            var map = new int[1];
            b.Accept(L("a", "b", "c", "d", "e"), null, 0, ref map);
            Assert.GreaterOrEqual(map.Length, 5);
        }
    }
}
