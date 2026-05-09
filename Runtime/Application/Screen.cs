using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.IR;
using PromptUGUI.Registry;
using R3;
using UnityEngine;

namespace PromptUGUI.Application
{

    public interface IScreen : IDisposable
    {
        public string Name { get; }
        public GameObject RootGameObject { get; }
        public T Get<T>(string id) where T : class, IControl;
        public IControl Get(string id);
    }

    public sealed class Screen : IScreen
    {
        private readonly ScreenInstantiator _instantiator;
        private readonly ControlRegistry _registry;
        private readonly Dictionary<string, IControl> _byId = new();
        private readonly Dictionary<ElementNode, Control> _nodeMap = new();
        private readonly List<IDisposable> _subscriptions = new();
        private IDisposable _variantSub;

        // 已实例化的 Add 块（不论当前是否可见）。Strategy C：首次进入激活才实例化；
        // 之后 toggle 仅切根 GameObject 的 SetActive，永不 Destroy/移除字典项；
        // 只在 Close 时随 RootGameObject 整体销毁。
        private readonly Dictionary<VariantBlock, AddInstance> _addInstances = new();

        private sealed class AddInstance
        {
            public List<GameObject> Roots = new();
            public List<string> AddedIds = new();
            public List<ElementNode> AddedNodes = new();
        }

        public string Name => Def.Name;
        public GameObject RootGameObject { get; private set; }

        internal IReadOnlyDictionary<ElementNode, Control> NodeMap => _nodeMap;
        internal ScreenDef Def { get; }
        internal VariantStore Variants { get; }

        public Screen(ScreenDef def, ScreenInstantiator instantiator,
                      ControlRegistry registry, VariantStore variants)
        {
            Def = def;
            _instantiator = instantiator;
            _registry = registry;
            Variants = variants;
        }

        public void Open()
        {
            var root = new GameObject(Def.Name,
                typeof(RectTransform),
                typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler),
                typeof(UnityEngine.UI.GraphicRaycaster));
            var canvas = root.GetComponent<Canvas>();
            canvas.renderMode = Def.CanvasMode switch
            {
                CanvasMode.Camera => RenderMode.ScreenSpaceCamera,
                CanvasMode.World => RenderMode.WorldSpace,
                _ => RenderMode.ScreenSpaceOverlay,
            };
            // Pixel-art / hand-tuned palettes want vertex colors to land on the canvas
            // verbatim, without the linear→sRGB roundtrip altering them.
            canvas.vertexColorAlwaysGammaSpace = true;
            UI.CanvasConfigurator?.Invoke(canvas, Def.Name);

            // 缺少 EventSystem 时按钮等不会响应任何指针事件,这是常见的踩坑点。
            // 仅在 PlayMode 提示;EditMode 测试不需要 EventSystem。
            if (UnityEngine.Application.isPlaying &&
                UnityEngine.Object.FindAnyObjectByType<UnityEngine.EventSystems.EventSystem>() == null)
            {
                Debug.LogWarning(
                    $"[PromptUGUI] No EventSystem found in scene; pointer events " +
                    $"(Btn clicks, hovers, etc.) on Screen '{Def.Name}' will not fire. " +
                    $"Add one via GameObject → UI → Event System.");
            }

            var result = _instantiator.InstantiateInto(root, Def);
            RootGameObject = result.Root;
            foreach (var kv in result.Controls) _byId[kv.Key] = kv.Value;
            foreach (var kv in result.NodeToControl) _nodeMap[kv.Key] = kv.Value;
            foreach (var block in Def.Variants)
            {
                if (Variants.IsActive(block.When))
                    ActivateAddBlock(block);
            }
            _variantSub = Variants.Changed.Subscribe(_ => ReSolve());
        }

        public void Close()
        {
            _variantSub?.Dispose();
            _variantSub = null;
            foreach (var d in _subscriptions) d.Dispose();
            _subscriptions.Clear();
            if (RootGameObject != null)
            {
                if (UnityEngine.Application.isPlaying)
                    UnityEngine.Object.Destroy(RootGameObject);
                else
                    UnityEngine.Object.DestroyImmediate(RootGameObject);
                RootGameObject = null;
            }
            _byId.Clear();
            _nodeMap.Clear();
            _addInstances.Clear();
        }

        public T Get<T>(string idPath) where T : class, IControl
        {
            var c = Get(idPath);
            if (c is not T typed)
                throw new InvalidCastException(
                    $"id '{idPath}' is {c.GetType().Name}, not {typeof(T).Name}");
            return typed;
        }

        public IControl Get(string idPath)
        {
            var segs = idPath.Split('/');
            if (!_byId.TryGetValue(segs[0], out var current))
                throw new KeyNotFoundException(
                    $"id '{segs[0]}' not found in screen '{Name}'");
            for (var i = 1; i < segs.Length; i++)
            {
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

        public void ReSolve()
        {
            // Collect nodes belonging to currently-inactive Add blocks so we can skip
            // re-applying attributes to them below. Their SetActive(false) state must
            // not be overwritten by ApplyCommon's unconditional Hidden assignment.
            var inactiveNodes = new HashSet<ElementNode>();
            foreach (var block in Def.Variants)
            {
                if (Variants.IsActive(block.When))
                {
                    ActivateAddBlock(block);
                }
                else
                {
                    DeactivateAddBlock(block);
                    if (_addInstances.TryGetValue(block, out var inst))
                        foreach (var n in inst.AddedNodes) inactiveNodes.Add(n);
                }
            }
            // Strategy C: _nodeMap includes nodes from currently-hidden Add blocks.
            // Skip attribute re-application for inactive Add block nodes to avoid
            // ApplyCommon's Hidden=false overwriting the SetActive(false) set above.
            foreach (var kv in _nodeMap)
            {
                var node = kv.Key;
                if (inactiveNodes.Contains(node)) continue;
                var control = kv.Value;
                var entry = _registry.Resolve(node.Tag);
                ControlAttributeApplier.Apply(node, control, entry, Variants);
            }
        }

        private void ActivateAddBlock(VariantBlock block)
        {
            if (_addInstances.TryGetValue(block, out var existing))
            {
                // 已实例化过：只重新显示根 GameObject，引用与订阅保持稳定
                foreach (var go in existing.Roots)
                    go?.SetActive(true);
                return;
            }

            // 首次激活：实例化并永久挂在 Screen 的 _byId / _nodeMap 里
            var pseudoResult = new InstantiationResult
            {
                Root = RootGameObject,
                Controls = _byId,
                NodeToControl = _nodeMap,
            };

            // 用 keys 差集追踪 Add 块新增的 ids / nodes（便于诊断与未来扩展）
            var prevIds = new HashSet<string>(_byId.Keys);
            var prevNodes = new HashSet<ElementNode>(_nodeMap.Keys);

            var inst = new AddInstance();
            inst.Roots.AddRange(_instantiator.ApplyAddBlock(block, pseudoResult));

            foreach (var k in _byId.Keys)
                if (!prevIds.Contains(k)) inst.AddedIds.Add(k);
            foreach (var n in _nodeMap.Keys)
                if (!prevNodes.Contains(n)) inst.AddedNodes.Add(n);

            _addInstances[block] = inst;
        }

        private void DeactivateAddBlock(VariantBlock block)
        {
            if (!_addInstances.TryGetValue(block, out var inst)) return;
            // Strategy C：只 SetActive(false) 隐藏；不 Destroy、不从 _byId/_nodeMap 移除——
            // 让代码侧 cached 引用与 R3 订阅跨 toggle 周期持续有效。
            foreach (var go in inst.Roots)
                go?.SetActive(false);
        }
    }
}
