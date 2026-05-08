using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using UnityEngine;

namespace PromptUGUI.Application {

    public interface IScreen : IDisposable {
        string Name { get; }
        GameObject RootGameObject { get; }
        T Get<T>(string id) where T : class, IControl;
        IControl Get(string id);
    }

    public sealed class Screen : IScreen {
        readonly ScreenDef _def;
        readonly ScreenInstantiator _instantiator;
        readonly ControlRegistry _registry;
        readonly VariantStore _variants;
        readonly Dictionary<string, IControl> _byId = new();
        readonly Dictionary<ElementNode, Control> _nodeMap = new();
        readonly List<IDisposable> _subscriptions = new();

        public string Name => _def.Name;
        public GameObject RootGameObject { get; private set; }

        internal IReadOnlyDictionary<ElementNode, Control> NodeMap => _nodeMap;
        internal ScreenDef Def => _def;
        internal VariantStore Variants => _variants;

        public Screen(ScreenDef def, ScreenInstantiator instantiator,
                      ControlRegistry registry, VariantStore variants) {
            _def = def;
            _instantiator = instantiator;
            _registry = registry;
            _variants = variants;
        }

        public void Open() {
            var root = new GameObject(_def.Name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var result = _instantiator.InstantiateInto(root, _def);
            RootGameObject = result.Root;
            foreach (var kv in result.Controls) _byId[kv.Key] = kv.Value;
            foreach (var kv in result.NodeToControl) _nodeMap[kv.Key] = kv.Value;
        }

        public void Close() {
            foreach (var d in _subscriptions) d.Dispose();
            _subscriptions.Clear();
            if (RootGameObject != null) {
                UnityEngine.Object.Destroy(RootGameObject);
                RootGameObject = null;
            }
            _byId.Clear();
            _nodeMap.Clear();
        }

        public T Get<T>(string idPath) where T : class, IControl {
            var c = Get(idPath);
            if (c is not T typed)
                throw new InvalidCastException(
                    $"id '{idPath}' is {c.GetType().Name}, not {typeof(T).Name}");
            return typed;
        }

        public IControl Get(string idPath) {
            var segs = idPath.Split('/');
            if (!_byId.TryGetValue(segs[0], out var current))
                throw new KeyNotFoundException(
                    $"id '{segs[0]}' not found in screen '{Name}'");
            for (int i = 1; i < segs.Length; i++) {
                var seg = segs[i];
                if (!current.ScopedIds.TryGetValue(seg, out var next))
                    throw new KeyNotFoundException(
                        $"id '{seg}' not found under '{string.Join("/", segs, 0, i)}' in screen '{Name}'");
                current = next;
            }
            return current;
        }

        public void Track(IDisposable d) => _subscriptions.Add(d);

        public void Dispose() => Close();

        // ReSolve and Add-block management land in Tasks 11/12.
    }
}
