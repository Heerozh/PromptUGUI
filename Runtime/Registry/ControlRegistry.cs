using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using UnityEngine;

namespace PromptUGUI.Registry {
    public sealed class ControlRegistry {
        public sealed class Entry {
            public Type ControlType;
            public GameObject Prefab;       // null = 内置原语，由 ScreenInstantiator 直接 new GameObject
            public ControlMeta Meta;
            public string DefaultTextAttr;  // null = 不支持文本简写
        }

        readonly Dictionary<string, Entry> _byTag = new();

        public void Register<T>(string tag, GameObject prefab,
                                string defaultTextAttr = null)
            where T : Control, new() {
            if (_byTag.ContainsKey(tag))
                throw new InvalidOperationException($"tag '{tag}' already registered");
            _byTag[tag] = new Entry {
                ControlType = typeof(T),
                Prefab = prefab,
                Meta = ControlMeta.Build(typeof(T)),
                DefaultTextAttr = defaultTextAttr,
            };
        }

        public Entry Resolve(string tag) {
            if (!_byTag.TryGetValue(tag, out var e))
                throw new InvalidOperationException($"unregistered tag '{tag}'");
            return e;
        }

        public bool Has(string tag) => _byTag.ContainsKey(tag);
    }
}
