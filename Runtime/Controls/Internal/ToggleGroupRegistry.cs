using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>Screen 级别的 string→ToggleGroup 缓存；同 group 名共享一个 ToggleGroup 组件。</summary>
    internal sealed class ToggleGroupRegistry
    {
        private readonly Transform _hostParent;
        private readonly Dictionary<string, ToggleGroup> _groups = new();

        public ToggleGroupRegistry(Transform hostParent) { _hostParent = hostParent; }

        public ToggleGroup GetOrCreate(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            if (_groups.TryGetValue(name, out var g) && g != null) return g;
            var go = new GameObject($"ToggleGroup:{name}", typeof(RectTransform));
            go.transform.SetParent(_hostParent, worldPositionStays: false);
            g = go.AddComponent<ToggleGroup>();
            _groups[name] = g;
            return g;
        }

        public void Clear() => _groups.Clear();
    }
}
