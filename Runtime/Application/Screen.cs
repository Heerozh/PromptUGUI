using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
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
        readonly Dictionary<string, IControl> _byId = new();
        readonly List<IDisposable> _subscriptions = new();

        public string Name => _def.Name;
        public GameObject RootGameObject { get; private set; }

        public Screen(ScreenDef def, ScreenInstantiator instantiator) {
            _def = def;
            _instantiator = instantiator;
        }

        public void Open() {
            // Build the root with Canvas first so child Graphic components can register
            // with it during their Awake/OnEnable.
            var root = new GameObject(_def.Name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            root.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

            var result = _instantiator.InstantiateInto(root, _def);
            RootGameObject = result.Root;
            foreach (var kv in result.Controls)
                _byId[kv.Key] = kv.Value;
        }

        public void Close() {
            foreach (var d in _subscriptions) d.Dispose();
            _subscriptions.Clear();
            if (RootGameObject != null) {
                UnityEngine.Object.Destroy(RootGameObject);
                RootGameObject = null;
            }
            _byId.Clear();
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
    }
}
