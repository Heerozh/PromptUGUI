using System;
using System.Collections.Generic;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// One <c>ScrollList.BindItems</c> call as the list sees it — through indices only (spec
    /// 2026-09-29-scrolllist-virtualization §5.5). <c>Accept</c> validates a push, takes a snapshot of it and
    /// says where each of its items came from; the list then binds row by row with <see cref="TryBind"/>.
    /// Keys never leave the generic half, so a value-type id never boxes.
    /// </summary>
    internal interface IItemBinding
    {
        /// <summary>Items in the last accepted push.</summary>
        public int Count { get; }

        /// <summary>The row type the bind callback expects — for the cast error message.</summary>
        public Type SlotType { get; }

        /// <summary>
        /// Binds item <paramref name="index"/> of the last accepted push onto <paramref name="row"/>;
        /// false (and nothing bound) when the row is not a <see cref="SlotType"/>.
        /// </summary>
        public bool TryBind(IControl row, int index);
    }

    /// <summary>The key → index table of a keyed binding's last accepted push.</summary>
    internal interface IKeyIndex<in TKey>
    {
        public bool TryGetIndex(TKey key, out int index);
    }

    internal abstract class ItemBinding<T, TSlot> : IItemBinding where TSlot : class, IControl
    {
        private readonly Action<TSlot, T> _bind;

        // Snapshot of the last accepted push (VIR-P8): a virtual list binds lazily while it scrolls, so
        // a host that edits the list it pushed must not be able to shift what a row shows or index past
        // its end. Reused across pushes — no steady-state allocation.
        private readonly List<T> _items = new();

        protected ItemBinding(Action<TSlot, T> bind) =>
            _bind = bind ?? throw new ArgumentNullException(nameof(bind));

        public int Count => _items.Count;

        public Type SlotType => typeof(TSlot);

        public bool TryBind(IControl row, int index)
        {
            if (row is not TSlot slot) return false;
            _bind(slot, _items[index]);
            return true;
        }

        /// <summary>
        /// Validates <paramref name="items"/> and takes a snapshot of it. <paramref name="newToOld"/> is grown
        /// as needed; its first <c>items.Count</c> entries become, for each new item, its index in the push
        /// that produced the rows the list shows now (<paramref name="previous"/>, which had
        /// <paramref name="previousCount"/> items), or −1 for an item that is new. An invalid push throws
        /// before anything is taken — the binding still describes the last accepted push.
        /// </summary>
        public abstract void Accept(IReadOnlyList<T> items, IItemBinding previous, int previousCount,
                                    ref int[] newToOld);

        protected void Take(IReadOnlyList<T> items)
        {
            _items.Clear();
            // Indexed copy, not AddRange: an IReadOnlyList is not necessarily an ICollection, and the
            // enumerator path would allocate on every push.
            for (var i = 0; i < items.Count; i++) _items.Add(items[i]);
        }

        protected static void Grow(ref int[] map, int count)
        {
            if (map != null && map.Length >= count) return;
            var size = map == null || map.Length == 0 ? 16 : map.Length;
            while (size < count) size *= 2;
            map = new int[size];
        }

        protected static void MapByPosition(int count, int previousCount, int[] map)
        {
            for (var j = 0; j < count; j++) map[j] = j < previousCount ? j : -1;
        }
    }

    /// <summary>No key: item j is bound onto row j (the 2026-09-14 row-reuse contract).</summary>
    internal sealed class PositionalBinding<T, TSlot> : ItemBinding<T, TSlot> where TSlot : class, IControl
    {
        public PositionalBinding(Action<TSlot, T> bind) : base(bind) { }

        public override void Accept(IReadOnlyList<T> items, IItemBinding previous, int previousCount,
                                    ref int[] newToOld)
        {
            Grow(ref newToOld, items.Count);
            MapByPosition(items.Count, previousCount, newToOld);
            Take(items);
        }
    }

    /// <summary>
    /// Rows follow their items by key. The previous push is matched by key whenever it came from ANY keyed
    /// binding with the same key type (VIR-P4) — the common host shape is a fresh
    /// <c>BindItems(Observable.Return(list), …, key)</c> per push, where "the same binding object" would
    /// never hold. A null or duplicate key rejects the whole push.
    /// </summary>
    internal sealed class KeyedBinding<T, TSlot, TKey> : ItemBinding<T, TSlot>, IKeyIndex<TKey>
        where TSlot : class, IControl
    {
        private readonly Func<T, TKey> _key;

        // Two tables, swapped per accepted push: _current answers IKeyIndex for the last accepted push
        // while the next one is validated into _next.
        private Dictionary<TKey, int> _current = new();
        private Dictionary<TKey, int> _next = new();
        private TKey[] _keys = new TKey[16];

        public KeyedBinding(Action<TSlot, T> bind, Func<T, TKey> key) : base(bind) =>
            _key = key ?? throw new ArgumentNullException(nameof(key));

        public bool TryGetIndex(TKey key, out int index) => _current.TryGetValue(key, out index);

        public override void Accept(IReadOnlyList<T> items, IItemBinding previous, int previousCount,
                                    ref int[] newToOld)
        {
            var n = items.Count;
            if (_keys.Length < n) _keys = new TKey[Math.Max(n, _keys.Length * 2)];

            // Validate first: the key delegate runs exactly once per item, and nothing is taken until
            // every key has passed.
            _next.Clear();
            for (var j = 0; j < n; j++)
            {
                var k = _key(items[j]);
                if (k == null)
                {
                    Array.Clear(_keys, 0, j);
                    throw new ArgumentException(
                        $"BindItems key: item {j} has a null key — every item needs a unique, non-null key.");
                }
                if (_next.TryGetValue(k, out var first))
                {
                    Array.Clear(_keys, 0, j);
                    throw new ArgumentException(
                        $"BindItems key: items {first} and {j} share the key '{k}' — keys must be unique within one push.");
                }
                _next.Add(k, j);
                _keys[j] = k;
            }

            Grow(ref newToOld, n);
            if (previous is IKeyIndex<TKey> index)
            {
                // previous may be this binding: _current still holds the last accepted push here.
                for (var j = 0; j < n; j++)
                    newToOld[j] = index.TryGetIndex(_keys[j], out var o) && o < previousCount ? o : -1;
            }
            else
            {
                MapByPosition(n, previousCount, newToOld);
            }

            (_current, _next) = (_next, _current);
            Take(items);
            Array.Clear(_keys, 0, n);   // keys may be references; do not keep them alive
        }
    }
}
