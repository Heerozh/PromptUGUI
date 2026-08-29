using System;
using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using R3;
using UnityEngine;

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
        private readonly Subject<Tab> _selectionChanged = new();

        // BindItems / itemTemplate state — factory resolution is deferred to first
        // Rebuild so that OwnerScreenOf(owner) sees the Screen registered in UI._open
        // (XML setter time may pre-date Open(); also lets tests rely on default "Tab").
        private string _itemTemplate = "Tab";
        private Func<RectTransform, IControl> _factory;
        private IDisposable _itemsSub;

        // Per-Tab subscriptions kept alive until next rebuild / Dispose; reset
        // both on dynamic Rebuild and on static OnAfterApply so reapply replaces them.
        private CompositeDisposable _tabSubs;

        // BindItems 接管卡片来源后置位：之后 ReSolve 触发的 CollectStatic 不得把
        // 已 Dispose 的静态 Tab 收回 _tabs（镜像 CarouselView._bound）。
        private bool _bound;

        public TabGroupCore(Control owner, Func<RectTransform> itemHost)
        {
            _owner = owner;
            _itemHost = itemHost;
        }

        public string ItemTemplate
        {
            set { _itemTemplate = string.IsNullOrEmpty(value) ? "Tab" : value; _factory = null; }
        }

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

        private void Rebuild<T, TSlot>(IReadOnlyList<T> items, Action<TSlot, T> bind)
            where TSlot : class, IControl
        {
            if (_factory == null) _factory = ResolveFactory(_itemTemplate);
            ClearTabs();
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

        private void ClearTabs()
        {
            _bound = true;
            _tabSubs?.Dispose();
            _tabSubs = null;
            foreach (var t in _tabs) t.Dispose();
            _tabs.Clear();
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
            var owner = UI.OwnerScreenOf(_owner);
            if (owner?.Def?.Templates != null && owner.Def.Templates.TryGetValue(tag, out var tpl))
            {
                ItemTemplateGuard.EnsureInstantiable(tag, tpl);
                return parent =>
                {
                    var instantiator = UI.GetInstantiator();
                    return instantiator.InstantiateNode(tpl.Body, parent, owner);
                };
            }
            if (UI.Registry.Has(tag))
            {
                return parent =>
                {
                    var instantiator = UI.GetInstantiator();
                    var node = new ElementNode(tag);
                    return instantiator.InstantiateNode(node, parent, owner);
                };
            }
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
                    .Where(on => on)
                    .Subscribe(_ =>
                    {
                        // Before announcing: SelectedTab reports the FIRST tab whose IsOn is set, so
                        // a stale second selection would be handed to subscribers.
                        EnforceExclusive(captured);
                        _selectionChanged.OnNext(captured);
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
        /// <c>OnDisable</c>. A <see cref="TabMenu"/> keeps its rows inside a collapsed (inactive)
        /// popup, so a code-driven <c>tab.IsOn = true</c> while the menu is closed would otherwise
        /// leave the previous tab on as well — two selected tabs, two visible bound pages.
        ///
        /// <para>Redundant for <see cref="TabBar"/>, where the ToggleGroup got there first, and
        /// harmless: assigning <c>isOn</c> a value it already holds returns early in uGUI, so no
        /// event is re-raised and the recursion terminates immediately.</para>
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

            // (2) Auto-select first if nothing on
            bool anyOn = false;
            foreach (var t in _tabs) if (t.IsOn) { anyOn = true; break; }
            if (!anyOn) _tabs[0].IsOn = true;
        }

        public void CollectStatic(IReadOnlyList<IControl> children)
        {
            if (_bound) return;
            _tabs.Clear();
            foreach (var child in children)
            {
                if (child is Tab tab) { _tabs.Add(tab); continue; }
                // Template-wrapper case: a <Tab> nested inside a Template-expanded
                // child (e.g. <FileTab><Frame><Tab/></Frame></FileTab>). Reuse the
                // same recursive walk used by BindItems for itemTemplate.
                var found = FindTabIn(child);
                if (found != null) _tabs.Add(found);
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
