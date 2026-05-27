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

        internal ToggleGroup InternalToggleGroup => _group;

        public override void OnAttached()
        {
            _group = GameObject.AddComponent<ToggleGroup>();
            _group.allowSwitchOff = false;
            ApplyDirection();
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
