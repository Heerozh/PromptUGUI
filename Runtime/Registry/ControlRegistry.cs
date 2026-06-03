using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Registry
{
    public sealed class ControlRegistry
    {
        public sealed class Entry
        {
            public Type ControlType;
            public GameObject Prefab;       // null = 内置原语，由 ScreenInstantiator 直接 new GameObject
            public ControlMeta Meta;
            public string DefaultTextAttr;  // null = 不支持文本简写
            // Attribute whose declared value is the INITIAL runtime state only (e.g. Tab/Toggle
            // "isOn" selection). Applied once at Open; a ReSolve (resize / Variant / Theme) must
            // NOT re-apply it, or the user's runtime selection snaps back to the declared default.
            public string RuntimeStateAttr;  // null = 无运行时独占状态属性
        }

        private readonly Dictionary<string, Entry> _byTag = new();

        public void Register<T>(string tag, GameObject prefab,
                                string defaultTextAttr = null,
                                string runtimeStateAttr = null)
            where T : Control, new()
        {
            _byTag[tag] = new Entry
            {
                ControlType = typeof(T),
                Prefab = prefab,
                Meta = ControlMeta.Build(typeof(T)),
                DefaultTextAttr = defaultTextAttr,
                RuntimeStateAttr = runtimeStateAttr,
            };
        }

        public Entry Resolve(string tag)
        {
            if (!_byTag.TryGetValue(tag, out var e))
                throw new InvalidOperationException($"unregistered tag '{tag}'");
            return e;
        }

        public bool Has(string tag) => _byTag.ContainsKey(tag);

        public System.Collections.Generic.IEnumerable<(string Tag, Entry Entry)> All
        {
            get
            {
                foreach (var kv in _byTag)
                    yield return (kv.Key, kv.Value);
            }
        }
    }
}
