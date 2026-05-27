using System.Collections.Generic;
using PromptUGUI.Application;
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
        private readonly List<Tab> _tabs = new();
        private readonly Subject<Tab> _selectionChanged = new();
        private Sprite _sprite;
        private Sprite _selectedSprite;
        private bool _selectedSpriteDeclared;

        internal ToggleGroup InternalToggleGroup => _group;

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

        [UIAttr(IsSprite = true), Preserve]
        public string Sprite { set => _sprite = UI.ResolveSprite(value); }

        [UIAttr(IsSprite = true), Preserve]
        public string SelectedSprite
        {
            set
            {
                _selectedSpriteDeclared = true;
                _selectedSprite = UI.ResolveSprite(value);
            }
        }

        internal override void OnAfterApply()
        {
            CollectStaticTabs();
            PushVisualToTabs();
            SyncInitialSelection();
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
            _tabs.Clear();
            foreach (var child in Children)
                if (child is Tab tab) _tabs.Add(tab);
        }

        private void PushVisualToTabs()
        {
            foreach (var t in _tabs)
            {
                t.ApplyBgSprite(_sprite);
                if (_selectedSpriteDeclared) t.EnsureOverlay(_selectedSprite);
            }
        }

        private void ApplyDirection()
        {
            if (_layout != null)
            {
                if (UnityEngine.Application.isPlaying) Object.Destroy((Component)_layout);
                else Object.DestroyImmediate((Component)_layout);
                _layout = null;
            }
            _layout = _direction == "vertical"
                ? (LayoutGroup)GameObject.AddComponent<VerticalLayoutGroup>()
                : GameObject.AddComponent<HorizontalLayoutGroup>();
        }

        public Observable<Tab> OnSelectionChanged => _selectionChanged;

        public override void Dispose()
        {
            _selectionChanged.Dispose();
            base.Dispose();
        }
    }
}
