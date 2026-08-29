using System;
using System.Collections.Generic;
using PromptUGUI.Controls.Internal;
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

        // Tab-group semantics (static collection / BindItems / initial selection / subscriptions)
        // live in TabGroupCore, shared with <TabMenu>. TabBar owns only the presentation: which
        // LayoutGroup arranges the tabs, and how it sizes them.
        private readonly TabGroupCore _core;

        public TabBar()
        {
            _core = new TabGroupCore(this, () => RectTransform);
        }

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
        public string ItemTemplate { set => _core.ItemTemplate = value; }

        public int Count => _core.Tabs.Count;

        public int SelectedIndex => _core.SelectedIndex;

        public Tab SelectedTab => _core.SelectedTab;

        public Tab GetAt(int index) => _core.Tabs[index];

        public IDisposable BindItems<T>(
            Observable<IReadOnlyList<T>> source,
            Action<Tab, T> bind)
            => BindItems<T, Tab>(source, bind);

        public IDisposable BindItems<T, TSlot>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind) where TSlot : class, IControl
            => _core.BindItems(source, bind);

        internal override void OnAfterApply()
        {
            _core.CollectStatic(Children);
            _core.SyncInitialSelection();
            _core.WireTabSubscriptions();
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
                case HorizontalLayoutGroup h: h.spacing = _spacing; ApplyChildSizing(h); break;
                case VerticalLayoutGroup v: v.spacing = _spacing; ApplyChildSizing(v); break;
            }
            if (string.IsNullOrEmpty(_padding) || _layout == null) return;
            _layout.padding = PaddingParser.Parse(_padding, _layout.padding);
        }

        /// <summary>
        /// 与 <see cref="VStack"/> / <see cref="HStack"/> 同款：childControl* 必须 true，
        /// 否则 LayoutGroup 只摆位置不改尺寸，<c>Control.ApplyCommon</c> 为 layout child 写的
        /// LayoutElement 全部落空，Tab 永远停在默认 100×100（撑穿轨道、相邻互相重叠）。
        /// forceExpand* 必须 false —— Unity 的 GetChildSizes 末尾会做
        /// <c>if (childForceExpand) flexible = Max(flexible, 1)</c>，留 true 会把
        /// <c>width="84"</c> 这类定尺寸 Tab 也一起拉伸，等于换个方向继续无视作者写的值。
        /// 每次 ApplySpacingPadding 都重设：direction 切换会重建 LayoutGroup。
        /// </summary>
        private static void ApplyChildSizing(HorizontalOrVerticalLayoutGroup lg)
        {
            lg.childControlWidth = true;
            lg.childControlHeight = true;
            lg.childForceExpandWidth = false;
            lg.childForceExpandHeight = false;
        }

        public Observable<Tab> OnSelectionChanged => _core.SelectionChanged;

        public override void Dispose()
        {
            _core.Dispose();
            base.Dispose();
        }
    }
}
