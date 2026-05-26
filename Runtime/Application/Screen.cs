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
        private bool _isReapplyingScaler;

        internal Controls.Internal.ToggleGroupRegistry ToggleGroups { get; private set; }

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

        // 转发自挂在 RootGameObject 上的 RectDimensionsRelay。订阅时机：UI.Open 返回之后。
        // 触发时机:根 Canvas 的 RectTransform 维度变化(屏幕方向切换、Camera/World canvas 主动改 size 等)。
        // 生命周期:Close 时连同 RootGameObject 一起销毁,并主动清空所有订阅。
        public event Action RectTransformDimensionsChanged;

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
            // Build the whole hierarchy under an INACTIVE root: every AddComponent in
            // the InstantiateInto recursion below then runs without firing OnEnable.
            // OnEnable is deferred to the single SetActive(true) at the end of Open().
            // Crucially this collapses N incremental UnityEngine.UI.Selectable.OnEnable
            // calls (each mutates a shared static registry and is fragile when
            // interleaved with the build) into one batched activation pass — the same
            // path Object.Instantiate / scene-load take. Also far fewer layout rebuilds.
            root.SetActive(false);
            // 必须先于 ApplyCanvasScaler / OnAttached / setter 阶段设好：pixel-mode
            // 的 ReadCanvasRectSize 与 UI.OwnerScreenOf 都会反查 RootGameObject。
            RootGameObject = root;
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
            ApplyCanvasScaler(root.GetComponent<UnityEngine.UI.CanvasScaler>());
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

            ToggleGroups = new Controls.Internal.ToggleGroupRegistry(root.transform);

            var relay = root.AddComponent<RectDimensionsRelay>();
            relay.OnDimensionsChanged = OnCanvasDimensionsChanged;

            // deferApply: the InstantiateInto recursion attaches every control but does
            // NOT apply attributes yet — nodes are collected into result.ApplyOrder
            // (DFS post-order). Attribute application is deferred until after the
            // SetActive(true) below, because ApplyCommon → GetNativeSize measures TMP
            // text and TMP must be Awake (the GameObject active) to measure.
            var result = _instantiator.InstantiateInto(root, Def, deferApply: true);
            foreach (var kv in result.Controls) _byId[kv.Key] = kv.Value;
            foreach (var kv in result.NodeToControl) _nodeMap[kv.Key] = kv.Value;
            foreach (var block in Def.Variants)
            {
                if (Variants.IsActive(block.When))
                    ActivateAddBlock(block, result.ApplyOrder);
            }
            // Single batched activation: fires every component's OnEnable — crucially
            // UnityEngine.UI.Selectable.OnEnable — in one pass now that the whole tree
            // (incl. Add blocks above) is built. See the SetActive(false) note above.
            root.SetActive(true);
            // Attributes applied last, on the now-Awake/active components, in the same
            // DFS post-order the recursion would have used inline.
            foreach (var node in result.ApplyOrder)
                ControlAttributeApplier.Apply(node, _nodeMap[node],
                                              _registry.Resolve(node.Tag), Variants);
            // scale must run after _nodeMap is populated and attributes have been applied
            // (so it doesn't fight ApplyCommon writes). Independent of canvas factor.
            ApplyScales();
            _variantSub = Variants.Changed.Subscribe(_ => ReSolve());
        }

        private void ApplyCanvasScaler(UnityEngine.UI.CanvasScaler scaler)
        {
            var mode = ResolveScaleMode();
            // scale-mode=pixel naturally pairs with Canvas.pixelPerfect — scale-mode
            // handles the integer outer scale, pixelPerfect snaps each UI vertex inside
            // to integer pixels (anchor/margin math can otherwise leave sub-pixel
            // positions). pixelPerfect is a no-op on World Space canvases, safe to set
            // unconditionally. CanvasConfigurator (Open-time) runs after and can opt
            // out for screens that need smooth tweens despite pixel scaling.
            scaler.GetComponent<UnityEngine.Canvas>().pixelPerfect = mode == ScaleMode.Pixel;
            if (mode == ScaleMode.Pixel) ApplyPixel(scaler);
            else ApplyAuto(scaler);
        }

        private ScaleMode ResolveScaleMode()
        {
            var raw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
                Def.Root, "scale-mode", Variants);
            if (string.IsNullOrEmpty(raw)) return UI.DefaultScaleMode;
            return raw == "pixel" ? ScaleMode.Pixel : ScaleMode.Auto;
        }

        private void ApplyAuto(UnityEngine.UI.CanvasScaler scaler)
        {
            var raw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
                Def.Root, "reference", Variants);
            var parsed = PromptUGUI.Application.ReferenceResolutionParser.Parse(
                raw, $"<Screen name='{Def.Name}' reference> (runtime)");
            if (!parsed.HasValue)
            {
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                return;
            }
            var size = parsed.Value;
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = size;
            scaler.matchWidthOrHeight = size.x >= size.y ? 0f : 1f;
        }

        private void ApplyPixel(UnityEngine.UI.CanvasScaler scaler)
        {
            var refRaw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
                Def.Root, "reference", Variants);
            var design = PromptUGUI.Application.ReferenceResolutionParser.Parse(
                refRaw, $"<Screen name='{Def.Name}' reference> (pixel-mode runtime)");
            if (!design.HasValue)
            {
                UnityEngine.Debug.LogError(
                    $"[PromptUGUI] <Screen name='{Def.Name}' scale-mode='pixel'>: " +
                    $"requires a reference='WxH' to compute integer scale factor. " +
                    $"Falling back to ConstantPixelSize, scaleFactor=1.");
                scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
                scaler.scaleFactor = 1f;
                return;
            }
            var canvasSize = UI.CanvasSizeOverride != null
                ? UI.CanvasSizeOverride()
                : ReadCanvasRectSize();
            var factor = PixelScaleSolver.Solve(canvasSize, design.Value);
            if (UI.MinPixelScale > 0f && factor < UI.MinPixelScale)
                factor = UI.MinPixelScale;
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = factor;
        }

        // Applies per-element 'scale' attribute as RectTransform.localScale directly
        // (relative to layout box; works in any scale-mode). Called at Open after the
        // attribute apply loop, and at ReSolve when variants change. No dependence on
        // canvas factor, so OnCanvasDimensionsChanged does not need to re-apply.
        //
        // Walks every Control in _nodeMap so nodes that declared 'scale' only via a
        // variant override are still tracked (resolves to null → identity reset).
        private void ApplyScales()
        {
            if (_nodeMap.Count == 0) return;
            foreach (var kv in _nodeMap)
            {
                var node = kv.Key;
                var declaredBase = node.Attributes.ContainsKey("scale");
                var declaredVariant = node.VariantOverrides.ContainsKey("scale");
                if (!declaredBase && !declaredVariant) continue;

                var raw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
                    node, "scale", Variants);
                var rt = kv.Value.RectTransform;
                if (rt == null) continue;

                if (string.IsNullOrEmpty(raw)
                    || !float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                       System.Globalization.CultureInfo.InvariantCulture, out var v)
                    || v <= 0f)
                {
                    rt.localScale = Vector3.one;
                    continue;
                }

                rt.localScale = new Vector3(v, v, 1f);
            }
        }

        private UnityEngine.Vector2 ReadCanvasRectSize()
        {
            // 必须读 Canvas.pixelRect(物理屏幕像素),不能读 RectTransform.rect。
            // 原因:在 ConstantPixelSize 模式下 RT.rect 等于 Screen.size / scaleFactor —
            // 我们改 scaleFactor 后 RT.rect 跟着变,下一帧 ApplyPixel 读到不同 rect 又算出
            // 不同 factor,形成反馈循环让 scaleFactor 在 1 ↔ N 之间闪烁。Canvas.pixelRect
            // 与 scaleFactor 无关(返回的是实际渲染输出像素),切断反馈链。
            var canvas = RootGameObject.GetComponent<UnityEngine.Canvas>();
            var pr = canvas.pixelRect;
            return new UnityEngine.Vector2(pr.width, pr.height);
        }

        private void OnCanvasDimensionsChanged()
        {
            // Forward to public subscribers first.
            RectTransformDimensionsChanged?.Invoke();
            // Pixel mode needs to recompute scaleFactor when canvas size changes;
            // Auto mode does its work via Unity's ScaleWithScreenSize internally, so
            // reapplying is idempotent and cheap. Guard against re-entry just in case
            // a subscriber happens to mutate the RectTransform during the callback.
            if (_isReapplyingScaler) return;
            _isReapplyingScaler = true;
            try
            {
                var scaler = RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>();
                if (scaler != null) ApplyCanvasScaler(scaler);
            }
            finally { _isReapplyingScaler = false; }
        }

        public void Close()
        {
            _variantSub?.Dispose();
            _variantSub = null;
            foreach (var d in _subscriptions) d.Dispose();
            _subscriptions.Clear();
            // 主动清空订阅,避免 GO 销毁过程中 Unity 再触发 OnRectTransformDimensionsChange 时
            // 还把回调派给已 Close 的 Screen 上的 stale 订阅者。
            RectTransformDimensionsChanged = null;
            // Dispose all controls before destroying the GameObject so that running
            // motions (e.g. LitMotion handles) are cancelled before the objects
            // become invalid. DestroyImmediate / Destroy are deferred in PlayMode,
            // so without explicit cancellation LitMotion callbacks can fire on
            // already-destroyed RectTransforms.
            foreach (var c in _nodeMap.Values) c.Dispose();
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
            ToggleGroups?.Clear();
            ToggleGroups = null;
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
                ControlAttributeApplier.Apply(node, control, entry, Variants, initial: false);
            }
            ApplyCanvasScaler(RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>());
            ApplyScales();
        }

        // deferApplyTo 非 null（Screen.Open 首次构建）：Add 子树属性 Apply 延迟收进该列表，
        // 由 Open 在 SetActive(true) 之后统一执行；null（ReSolve 运行时激活，树已 active）：就地 Apply。
        private void ActivateAddBlock(VariantBlock block, List<ElementNode> deferApplyTo = null)
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
            inst.Roots.AddRange(_instantiator.ApplyAddBlock(block, pseudoResult, deferApplyTo));

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
