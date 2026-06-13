using System;
using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls
{
    public sealed class TabBar : Control
    {
        private ToggleGroup _group;
        private LayoutGroup _layout;
        private string _direction = "horizontal";
        private float _spacing;
        private string _padding;
        private readonly List<Tab> _tabs = new();
        private readonly Subject<Tab> _selectionChanged = new();

        // BindItems / itemTemplate state — factory resolution is deferred to first
        // Rebuild so that OwnerScreenOf(this) sees the Screen registered in UI._open
        // (XML setter time may pre-date Open(); also lets tests rely on default "Tab").
        private string _itemTemplate = "Tab";
        private Func<RectTransform, IControl> _factory;
        private IDisposable _itemsSub;

        // Per-Tab subscriptions kept alive until next rebuild / Dispose; reset
        // both on dynamic Rebuild and on static OnAfterApply so reapply replaces them.
        private CompositeDisposable _tabSubs;

        // BindItems 接管卡片来源后置位：之后 ReSolve 触发的 CollectStaticTabs 不得把
        // 已 Dispose 的静态 Tab 收回 _tabs（镜像 CarouselView._bound）。
        private bool _bound;

        public override void OnAttached()
        {
            _group = GameObject.AddComponent<ToggleGroup>();
            _group.allowSwitchOff = false;
            ApplyDirection();
        }

        [UIAttr, Preserve]
        public string Direction
        {
            set
            {
                _direction = string.IsNullOrEmpty(value) ? "horizontal" : value;
                ApplyDirection();
            }
        }

        [UIAttr, Preserve]
        public float Spacing { set { _spacing = value; ApplySpacingPadding(); } }

        [UIAttr, Preserve]
        public string Padding { set { _padding = value; ApplySpacingPadding(); } }

        [UIAttr, Preserve]
        public string ItemTemplate
        {
            set { _itemTemplate = string.IsNullOrEmpty(value) ? "Tab" : value; _factory = null; }
        }

        public int Count => _tabs.Count;

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

        public Tab GetAt(int index) => _tabs[index];

        public IDisposable BindItems<T>(
            Observable<IReadOnlyList<T>> source,
            Action<Tab, T> bind)
            => BindItems<T, Tab>(source, bind);

        public IDisposable BindItems<T, TSlot>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind) where TSlot : class, IControl
        {
            _itemsSub?.Dispose();
            _itemsSub = source.Subscribe(items => Rebuild(items, bind));
            return _itemsSub;
        }

        private void Rebuild<T, TSlot>(IReadOnlyList<T> items, Action<TSlot, T> bind)
            where TSlot : class, IControl
        {
            if (_factory == null) _factory = ResolveFactory(_itemTemplate);
            ClearTabs();
            for (int i = 0; i < items.Count; i++)
            {
                var node = _factory(RectTransform);
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

        // Tab is a pure C# Control (not a MonoBehaviour), so GetComponentInChildren
        // can't find it. ScrollList-style template wrappers expose the full id scope on
        // the root via ReplaceScopedIds — look there first; if the template has no
        // id'd Tab, fall back to a recursive Children walk so wrappers without ids still work.
        private static Tab FindTabIn(IControl node)
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
            var owner = UI.OwnerScreenOf(this);
            if (owner?.Def?.Templates != null && owner.Def.Templates.TryGetValue(tag, out var tpl))
            {
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
                $"<TabBar itemTemplate='{tag}'>: tag is neither a registered Control nor a Template");
        }

        internal override void OnAfterApply()
        {
            CollectStaticTabs();
            SyncInitialSelection();
            WireTabSubscriptions();
        }

        private void WireTabSubscriptions()
        {
            _tabSubs?.Dispose();
            _tabSubs = new CompositeDisposable();
            foreach (var t in _tabs)
            {
                var captured = t;
                captured.OnValueChanged
                    .Where(on => on)
                    .Subscribe(_ => _selectionChanged.OnNext(captured))
                    .AddTo(_tabSubs);
            }
        }

        private void SyncInitialSelection()
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

        private void CollectStaticTabs()
        {
            if (_bound) return;
            _tabs.Clear();
            foreach (var child in Children)
            {
                if (child is Tab tab) { _tabs.Add(tab); continue; }
                // Template-wrapper case: a <Tab> nested inside a Template-expanded
                // child (e.g. <FileTab><Frame><Tab/></Frame></FileTab>). Reuse the
                // same recursive walk used by BindItems for itemTemplate.
                var found = FindTabIn(child);
                if (found != null) _tabs.Add(found);
            }
        }

        private void ApplyDirection()
        {
            var wantVertical = _direction == "vertical";

            // Idempotent: re-applying the SAME direction (an explicit base value, or any ReSolve) must
            // reuse the existing group, never destroy+recreate. Object.Destroy is deferred to end-of-frame
            // in play mode, so a destroy-then-AddComponent in one frame collides with the not-yet-removed
            // group (LayoutGroup is [DisallowMultipleComponent]); the add fails and the deferred destroy
            // then strands the TabBar with no layout group (every Tab piles up at the origin).
            if (_layout != null && (_layout is VerticalLayoutGroup) == wantVertical)
            {
                ApplySpacingPadding();
                return;
            }

            // Genuine H<->V swap: destroy synchronously so the [DisallowMultipleComponent] slot is free
            // before AddComponent. DestroyImmediate in BOTH modes — Object.Destroy's deferral is exactly
            // what broke this in play mode, and this runs off the app/ReSolve call stack (never a
            // physics/animation/OnValidate callback), where DestroyImmediate on a component is safe.
            if (_layout != null)
            {
                UnityEngine.Object.DestroyImmediate((Component)_layout);
                _layout = null;
            }
            _layout = wantVertical
                ? (LayoutGroup)GameObject.AddComponent<VerticalLayoutGroup>()
                : GameObject.AddComponent<HorizontalLayoutGroup>();
            ApplySpacingPadding();
        }

        private void ApplySpacingPadding()
        {
            switch (_layout)
            {
                case HorizontalLayoutGroup h: h.spacing = _spacing; break;
                case VerticalLayoutGroup v: v.spacing = _spacing; break;
            }
            if (string.IsNullOrEmpty(_padding) || _layout == null) return;
            var parts = _padding.Split(',');
            int t = 0, r = 0, b = 0, l = 0;
            switch (parts.Length)
            {
                case 1: int.TryParse(parts[0], out t); r = b = l = t; break;
                case 2:
                    int.TryParse(parts[0], out t); b = t;
                    int.TryParse(parts[1], out r); l = r; break;
                case 4:
                    int.TryParse(parts[0], out t);
                    int.TryParse(parts[1], out r);
                    int.TryParse(parts[2], out b);
                    int.TryParse(parts[3], out l); break;
            }
            _layout.padding = new RectOffset(l, r, t, b);
        }

        public Observable<Tab> OnSelectionChanged => _selectionChanged;

        public override void Dispose()
        {
            _tabSubs?.Dispose();
            _itemsSub?.Dispose();
            _selectionChanged.Dispose();
            base.Dispose();
        }
    }
}
