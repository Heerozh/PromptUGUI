using System;
using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.Parser;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The tab-group semantics shared by <see cref="TabBar"/> (tabs laid out in a bar) and
    /// <see cref="TabMenu"/> (the same tabs folded into a popup): collecting static
    /// <c>&lt;Tab&gt;</c> children, <c>BindItems</c> + <c>itemTemplate</c> rebuilds, initial-selection
    /// reconciliation, and the per-tab subscriptions feeding <see cref="SelectionChanged"/>.
    ///
    /// <para>One implementation, two hosts — the presentation (layout group vs. popup panel) is the
    /// only thing the two controls own themselves. The owner supplies the RectTransform new items
    /// parent into via <c>itemHost</c>, since <see cref="TabBar"/> hosts them on itself while
    /// <see cref="TabMenu"/> hosts them inside its popup's content node.</para>
    /// </summary>
    internal sealed class TabGroupCore : IDisposable
    {
        private readonly Control _owner;
        private readonly Func<RectTransform> _itemHost;
        private readonly List<Tab> _tabs = new();
        // The row each tab belongs to, index for index: what itemTemplate instantiated (a wrapper
        // template's root, the Tab itself for the default template), or for a static tab the
        // owner's direct child that holds it. A rebuild retires whole rows — disposing only the Tab
        // would leave the rest of a wrapper row (its root and the Tab's siblings) in the item host.
        private readonly List<IControl> _rows = new();
        private readonly Subject<Tab> _selectionChanged = new();
        // Retired rows wait here, inactive, until their deferred Destroy (Play mode). Same arrangement
        // as ScrollList's pool, for the same reasons: out of the item host's layout and raycasts at
        // once, and a disabled layout group so a ReSolve replaying a row parked here takes the
        // layout-group branch of ApplyCommon.
        private RectTransform _pool;

        // BindItems / itemTemplate state — factory resolution is deferred to first
        // Rebuild so that OwnerScreenOf(owner) sees the Screen registered in UI._open
        // (XML setter time may pre-date Open(); also lets tests rely on default "Tab").
        private string _itemTemplate = "Tab";
        private Func<RectTransform, IControl> _factory;
        private IDisposable _itemsSub;

        // Keyed BindItems (same contract as ScrollList's, spec 2026-10-13 §2): the binding whose last
        // push produced the current rows — null when they came from anywhere else (static children, a
        // push without a key), which a keyed push then replaces whole — the itemTemplate they were
        // built from, and the scratch of one rebuild.
        private IItemBinding _keyed;
        private string _rowsTemplate;
        private int[] _remap = new int[16];
        private readonly List<IControl> _prevRows = new();
        private readonly List<Tab> _prevTabs = new();

        // Per-Tab subscriptions kept alive until next rebuild / Dispose; reset
        // both on dynamic Rebuild and on static OnAfterApply so reapply replaces them.
        private CompositeDisposable _tabSubs;

        // BindItems 接管卡片来源后置位：之后 ReSolve 触发的 CollectStatic 不得把
        // 已 Dispose 的静态 Tab 收回 _tabs（镜像 CarouselView._bound）。
        private bool _bound;

        // 「无选中」是否是合法静止态。两条来源：作者写的 allowSwitchOff=，或代码调过一次
        // ClearSelection()。任一成立就关掉 SyncInitialSelection 的补选 —— 它挂在 OnAfterApply
        // 上，每次 ReSolve（Variant / 主题 / resize）都跑，不关就会把玩家刚关掉的页面选回来。
        private bool _allowSwitchOff;
        private bool _selectionCleared;

        // 宿主的 ToggleGroup（TabBar / TabMenu 在 OnAttached 里交过来）。ClearSelection 需要它：
        // allowSwitchOff=false 时 uGUI 会把「关掉最后一个 on」原地弹回 true。
        private ToggleGroup _group;

        public TabGroupCore(Control owner, Func<RectTransform> itemHost)
        {
            _owner = owner;
            _itemHost = itemHost;
        }

        public string ItemTemplate
        {
            set { _itemTemplate = string.IsNullOrEmpty(value) ? "Tab" : value; _factory = null; }
        }

        /// <summary>The owner's mutual-exclusion group, handed over in its <c>OnAttached</c>.</summary>
        public ToggleGroup Group
        {
            set { _group = value; if (_group != null) _group.allowSwitchOff = _allowSwitchOff; }
        }

        /// <summary>
        /// Whether "nothing selected" is a legal resting state: the group opens with no tab on
        /// (unless one declares <c>isOn="true"</c>) and clicking the active tab turns it off.
        /// Default false — TB-D7's original contract, unchanged for every bar that doesn't ask.
        /// </summary>
        public bool AllowSwitchOff
        {
            set { _allowSwitchOff = value; if (_group != null) _group.allowSwitchOff = value; }
        }

        /// <summary>
        /// Whether an unselected group still auto-selects its first tab. False once the author has
        /// declared an empty selection legal — either way, what the owner must mirror when it
        /// predicts the selection ahead of <see cref="SyncInitialSelection"/> (TabMenu's handle
        /// measures itself that way).
        /// </summary>
        public bool AutoSelectsFirst => !_allowSwitchOff && !_selectionCleared;

        public IReadOnlyList<Tab> Tabs => _tabs;

        public Observable<Tab> SelectionChanged => _selectionChanged;

        public int SelectedIndex
        {
            get
            {
                for (int i = 0; i < _tabs.Count; i++)
                    if (_tabs[i].IsOn) return i;
                return -1;
            }
        }

        public Tab SelectedTab
        {
            get
            {
                var idx = SelectedIndex;
                return idx >= 0 ? _tabs[idx] : null;
            }
        }

        public IDisposable BindItems<T, TSlot>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind,
            Action beforeRebuild = null,
            Action afterRebuild = null) where TSlot : class, IControl
        {
            _itemsSub?.Dispose();
            _itemsSub = source.Subscribe(items =>
            {
                beforeRebuild?.Invoke();
                Rebuild(items, bind);
                afterRebuild?.Invoke();
            });
            return _itemsSub;
        }

        /// <summary>
        /// Rows follow their items by <paramref name="key"/>: a row whose key was in the previous keyed
        /// push is reused in place — re-bound, moved to its new position, its Tab's <c>isOn</c> and
        /// everything it drives (bound page, <c>checked@</c> animations) left exactly as they were — a
        /// new key gets a new row, a vanished key's row is retired. Every row is re-bound on every push.
        /// The selection therefore follows its key; when the selected key is gone,
        /// <see cref="SelectionChanged"/> announces what is selected now (<c>null</c>, or the first tab
        /// for a group that auto-selects). A null or duplicate key rejects the whole push before
        /// anything changes (logged through R3; the rows stay as they were).
        /// </summary>
        public IDisposable BindItems<T, TSlot, TKey>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind,
            Func<T, TKey> key,
            Action beforeRebuild = null,
            Action afterRebuild = null) where TSlot : class, IControl
        {
            var binding = new KeyedBinding<T, TSlot, TKey>(bind, key ?? throw new ArgumentNullException(nameof(key)));
            _itemsSub?.Dispose();
            _itemsSub = source.Subscribe(items =>
            {
                items ??= Array.Empty<T>();
                // Matched by key against any keyed push of the same key type (a fresh BindItems per push
                // works too); rows from anywhere else, or from another itemTemplate, are not reused.
                var reuse = _keyed != null && _rowsTemplate == _itemTemplate;
                binding.Accept(items, reuse ? _keyed : null, reuse ? _rows.Count : 0, ref _remap);
                beforeRebuild?.Invoke();
                KeyedRebuild(binding, items.Count);
                afterRebuild?.Invoke();
            });
            return _itemsSub;
        }

        private void KeyedRebuild(IItemBinding binding, int count)
        {
            if (_factory == null) _factory = ResolveFactory(_itemTemplate);
            var previous = SelectedTab;
            _bound = true;
            _tabSubs?.Dispose();
            _tabSubs = null;

            _prevRows.Clear();
            _prevRows.AddRange(_rows);
            _prevTabs.Clear();
            _prevTabs.AddRange(_tabs);
            _rows.Clear();
            _tabs.Clear();
            var host = _itemHost();
            for (var j = 0; j < count; j++)
            {
                var from = _remap[j];
                if (from >= 0 && from < _prevRows.Count && _prevRows[from] is { } kept && kept.GameObject != null)
                {
                    _prevRows[from] = null;
                    // Same contract as a reused ScrollList row: what the previous bind hung on the row
                    // goes before the row is bound again.
                    if (kept is Control c) c.ReleaseSubscriptions();
                    _rows.Add(kept);
                    _tabs.Add(_prevTabs[from]);
                    continue;
                }
                var node = _factory(host);
                var tab = node as Tab ?? FindTabIn(node)
                    ?? throw new InvalidCastException(
                        $"itemTemplate='{_itemTemplate}' root contains no <Tab>; cannot bind.");
                _rows.Add(node);
                _tabs.Add(tab);
            }
            foreach (var old in _prevRows)
                if (old != null) Retire(old);
            _prevRows.Clear();
            _prevTabs.Clear();
            PinRowOrder();

            // Structure first, then bind: a bind callback already sees the final rows, in order.
            for (var j = 0; j < count; j++)
                if (!binding.TryBind(_rows[j], j))
                    throw new InvalidCastException(
                        $"itemTemplate='{_itemTemplate}' instantiated {_rows[j].GetType().Name}, " +
                        $"but BindItems expected {binding.SlotType.Name}");
            _keyed = binding;
            _rowsTemplate = _itemTemplate;

            SyncInitialSelection();
            WireTabSubscriptions();
            // A reused selected row changed nothing. Otherwise the selection went with its row, and
            // what replaced it — nothing, or the auto-selected first tab — is announced.
            var now = SelectedTab;
            if (_tabs.Count == 0 || !ReferenceEquals(previous, now)) _selectionChanged.OnNext(now);
        }

        /// <summary>
        /// Lays the rows out in data order. New rows were appended after everything else, reused ones
        /// sit where they were: only when that is out of order are they moved, all to the end in order —
        /// static children that are not rows (a separator) stay ahead of them, as they always did.
        /// </summary>
        private void PinRowOrder()
        {
            var last = -1;
            var ordered = true;
            foreach (var row in _rows)
            {
                var index = HostOf(row).GetSiblingIndex();
                if (index < last) { ordered = false; break; }
                last = index;
            }
            if (ordered) return;
            foreach (var row in _rows) HostOf(row).SetAsLastSibling();
        }

        private static RectTransform HostOf(IControl row) => row is Control c ? c.LayoutHost : row.RectTransform;

        private void Rebuild<T, TSlot>(IReadOnlyList<T> items, Action<TSlot, T> bind)
            where TSlot : class, IControl
        {
            if (_factory == null) _factory = ResolveFactory(_itemTemplate);
            ClearRows();
            _keyed = null;
            var host = _itemHost();
            for (int i = 0; i < items.Count; i++)
            {
                var node = _factory(host);
                var typed = node as TSlot;
                if (typed == null)
                    throw new InvalidCastException(
                        $"itemTemplate='{_itemTemplate}' instantiated {node.GetType().Name}, expected {typeof(TSlot).Name}");

                var tab = node as Tab ?? FindTabIn(node);
                if (tab == null)
                    throw new InvalidCastException(
                        $"itemTemplate='{_itemTemplate}' root contains no <Tab>; cannot bind.");

                _tabs.Add(tab);
                _rows.Add(node);
                // Tab.OnAttached already wired ToggleGroup via FindAncestorToggleGroup.
                // Per-tab sprite / selectedSprite live on Tab itself — set them on the
                // itemTemplate body (e.g. <Template name="MyTab"><Tab sprite="..."/></Template>)
                // if every dynamic Tab should share the same visual.
                bind(typed, items[i]);
            }
            SyncInitialSelection();
            WireTabSubscriptions();
            if (_tabs.Count == 0) _selectionChanged.OnNext(null);
        }

        private void ClearRows()
        {
            _bound = true;
            _tabSubs?.Dispose();
            _tabSubs = null;
            foreach (var row in _rows) Retire(row);
            _rows.Clear();
            _tabs.Clear();
        }

        /// <summary>
        /// Takes a row out of the item host, then disposes every control in it. Out first: in Play
        /// mode <c>Destroy</c> waits for the end of the frame, and until then the row would still be
        /// laid out, still be clickable, and still be measured by a <see cref="TabMenu"/> sizing its
        /// popup straight after the rebuild.
        /// </summary>
        private void Retire(IControl row)
        {
            var host = HostOf(row);
            if (host != null) host.SetParent(EnsurePool(), worldPositionStays: false);
            DisposeTree(row);
        }

        /// <summary>
        /// Disposes a row's controls deepest first, the root last. The root's own Dispose already
        /// releases every <c>.AddTo(control)</c> bag below it and destroys the whole GameObject
        /// subtree, but only the controls' own Dispose unhooks what they hold outside it (a Tab's
        /// <c>Locale.Changed</c> handler, an Animation's running motions). A static row's controls
        /// are disposed again when the Screen closes — Dispose is safe to repeat, as for every
        /// static card a BindItems replaces.
        /// </summary>
        private static void DisposeTree(IControl node)
        {
            if (node is Control ctrl)
                for (var i = ctrl.Children.Count - 1; i >= 0; i--) DisposeTree(ctrl.Children[i]);
            node.Dispose();
        }

        private RectTransform EnsurePool()
        {
            if (_pool != null) return _pool;
            _pool = ProceduralBuilders.AddChild(_owner.RectTransform, "Pool");
            _pool.gameObject.AddComponent<VerticalLayoutGroup>().enabled = false;
            _pool.gameObject.SetActive(false);
            return _pool;
        }

        /// <summary>
        /// Tab is a pure C# Control (not a MonoBehaviour), so GetComponentInChildren
        /// can't find it. ScrollList-style template wrappers expose the full id scope on
        /// the root via ReplaceScopedIds — look there first; if the template has no
        /// id'd Tab, fall back to a recursive Children walk so wrappers without ids still work.
        /// </summary>
        public static Tab FindTabIn(IControl node)
        {
            foreach (var c in node.ScopedIds.Values)
                if (c is Tab t) return t;
            if (node is Control ctrl)
            {
                foreach (var child in ctrl.Children)
                {
                    if (child is Tab t) return t;
                    var nested = FindTabIn(child);
                    if (nested != null) return nested;
                }
            }
            return null;
        }

        private Func<RectTransform, IControl> ResolveFactory(string tag)
        {
            // Same resolver as ScrollList / Carousel / Screen.Instantiate; only the not-found error is ours.
            var owner = UI.OwnerScreenOf(_owner);
            if (TemplateFactoryResolver.TryResolve(owner, tag, $"itemTemplate='{tag}'", out var factory))
                return factory;
            throw new ParseException(
                $"<{_owner.GetType().Name} itemTemplate='{tag}'>: tag is neither a registered Control nor a Template");
        }

        public void WireTabSubscriptions()
        {
            _tabSubs?.Dispose();
            _tabSubs = new CompositeDisposable();
            foreach (var t in _tabs)
            {
                var captured = t;
                captured.OnValueChanged
                    .Subscribe(on =>
                    {
                        if (on)
                        {
                            // Before announcing: SelectedTab reports the FIRST tab whose IsOn is set,
                            // so a stale second selection would be handed to subscribers.
                            EnforceExclusive(captured);
                            _selectionChanged.OnNext(captured);
                            return;
                        }
                        // A tab going off only means "no selection" when nothing took over. uGUI
                        // turns the loser off from inside NotifyToggleOn — after the winner's isOn
                        // already reads true — so an ordinary switch never reaches the emit below;
                        // only a switch-off click or ClearSelection does.
                        if (SelectedIndex < 0) _selectionChanged.OnNext(null);
                    })
                    .AddTo(_tabSubs);
            }
        }

        /// <summary>
        /// Turns every tab but <paramref name="winner"/> off.
        /// </summary>
        /// <remarks>
        /// The group's <c>ToggleGroup</c> normally does this, but only for tabs that are
        /// <em>active</em>: uGUI's <c>Toggle.Set</c> gates the <c>NotifyToggleOn</c> call on
        /// <c>IsActive()</c>, and a toggle also unregisters itself from the group in
        /// <c>OnDisable</c>. What this covers is the group being inactive <em>as a whole</em>: a
        /// <see cref="TabMenu"/> keeps its rows inside a collapsed popup, and a <see cref="TabBar"/>
        /// on a page that is itself hidden is in the same position. There a code-driven
        /// <c>tab.IsOn = true</c> would otherwise leave the previous tab on as well — two selected
        /// tabs, two visible bound pages — and the loser's <c>IsOn = false</c> bypasses the group
        /// just like the winner's did, so it simply sticks.
        ///
        /// <para>It does NOT make an inactive tab selectable next to <em>active</em> ones (a
        /// <c>hidden="true"</c> tab in a visible bar). Turning off an active loser runs uGUI's group
        /// logic, and a group that forbids switch-off sees no registered member on — the inactive
        /// winner never registered — so it flips the loser straight back on, which re-enters here
        /// and turns the winner off again after its bound page has already been shown. That is why
        /// <see cref="Tab.IsOn"/> refuses such an assignment before it can reach this method: a
        /// hidden tab is not a page switch, and a page's sub-views switch inside the page.</para>
        ///
        /// <para>Redundant for an all-active <see cref="TabBar"/>, where the ToggleGroup got there
        /// first, and harmless: assigning <c>isOn</c> a value it already holds returns early in
        /// uGUI, so no event is re-raised and the recursion terminates immediately.</para>
        /// </remarks>
        private void EnforceExclusive(Tab winner)
        {
            for (int i = 0; i < _tabs.Count; i++)
            {
                var t = _tabs[i];
                if (!ReferenceEquals(t, winner) && t.IsOn) t.IsOn = false;
            }
        }

        public void SyncInitialSelection()
        {
            if (_tabs.Count == 0) return;

            // (1) Reconcile: any unselected Tab with a bind clears its Frame
            foreach (var t in _tabs)
                if (!t.IsOn) t.ForceSyncBindFrame(isOn: false);

            // (2) Auto-select first if nothing on — unless an empty selection is legal here, in
            // which case there is nothing to repair and re-selecting tab 0 on every ReSolve would
            // re-open a page the player just closed.
            if (SelectedIndex < 0 && AutoSelectsFirst) _tabs[0].IsOn = true;
        }

        /// <summary>
        /// Turns every tab off — the "no page open" state a bound page's own close button wants.
        /// Announces the empty selection as a <c>null</c> on <see cref="SelectionChanged"/>.
        /// </summary>
        /// <remarks>
        /// Legal on any group, with or without <c>allowSwitchOff</c>: clearing from code is a
        /// deliberate act, only clearing by <em>click</em> is what the attribute governs. Two
        /// things would otherwise undo it, and both are handled here:
        /// <list type="bullet">
        /// <item>uGUI restores the last ON member of a group that forbids switch-off, so the ban is
        /// lifted for the assignment and restored immediately after.</item>
        /// <item><see cref="SyncInitialSelection"/> runs on every <c>OnAfterApply</c>, i.e. on every
        /// ReSolve, so the latch below permanently retires the auto-select for this group. It never
        /// resets: an owner that has asked for an empty selection once owns the selection from then
        /// on, including across a <c>BindItems</c> rebuild.</item>
        /// </list>
        /// </remarks>
        public void ClearSelection()
        {
            _selectionCleared = true;
            if (_tabs.Count == 0) return;

            var lifted = _group != null && !_group.allowSwitchOff;
            if (lifted) _group.allowSwitchOff = true;
            try
            {
                // At most one tab is on, and uGUI ignores a no-op assignment, so exactly one
                // onValueChanged(false) fires — hence exactly one null on SelectionChanged.
                foreach (var t in _tabs) t.IsOn = false;
            }
            finally
            {
                if (lifted) _group.allowSwitchOff = false;
            }
        }

        public void CollectStatic(IReadOnlyList<IControl> children)
        {
            if (_bound) return;
            _tabs.Clear();
            _rows.Clear();
            foreach (var child in children)
            {
                // Template-wrapper case: a <Tab> nested inside a Template-expanded
                // child (e.g. <FileTab><Frame><Tab/></Frame></FileTab>). Reuse the
                // same recursive walk used by BindItems for itemTemplate. Either way the
                // row is the direct child — that is what a first BindItems push retires.
                var tab = child as Tab ?? FindTabIn(child);
                if (tab == null) continue;
                _tabs.Add(tab);
                _rows.Add(child);
            }
        }

        public void Dispose()
        {
            _tabSubs?.Dispose();
            _itemsSub?.Dispose();
            _selectionChanged.Dispose();
        }
    }
}
