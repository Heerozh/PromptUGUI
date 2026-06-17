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
        private System.Action<string> _themeHandler;
        private bool _isReapplyingScaler;
        private bool _isPixelMode;
        // The pixel/auto factor that ApplyCanvasScaler last applied; 'Nx' scale divides by it.
        private float _canvasFactor = 1f;
        // True if any node declares a factor-dependent scale — scale="Nx" or scale="<r>r"
        // (base or variant). Gates the resize path: such Screens re-run ReSolve (re-baseline +
        // recompute) on canvas resize; others keep the lightweight ApplyCanvasScaler-only path
        // (zero behavior change).
        private bool _hasFactorScale;

        // Non-null only during Open()'s apply pass. Tab.bind queues its initial page-hide
        // here (see DeferDuringOpen) so a bound page is not deactivated before its own
        // auto-sized descendants finish measuring: a <Btn>/<Toggle>/<Dropdown> label is a
        // TMP created via AddComponent in the apply pass, and a TMP added to an already
        // inactive GameObject never runs Awake/OnEnable, so its preferredWidth measures
        // garbage that freezes into the LayoutElement. Drained right after ApplyScales.
        private List<Action> _deferredOpenActions;

        internal Controls.Internal.ToggleGroupRegistry ToggleGroups { get; private set; }

        // BindItems / Markdown 等经 ScreenInstantiator.InstantiateNode 动态实例化的子树。
        // 它们不能进 _nodeMap（同一 ElementNode 会对应 N 个卡片实例），但 scale 仍须由
        // Screen 统一应用——Nx / <r>r 依赖 _canvasFactor，且 resize / Variant ReSolve 要重算。
        // 只登记含 scale 声明（base 或 variant）的子树；卡片被 BindItems 重建销毁后由
        // PruneDeadDynamicSubtrees 按 Root.GameObject == null 剔除。
        private sealed class DynamicSubtree
        {
            public Control Root;
            public Dictionary<ElementNode, Control> Nodes;
        }
        private readonly List<DynamicSubtree> _dynamicSubtrees = new();

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
            // DFS post-order the recursion would have used inline. Open the deferral window
            // first: any bound-page hide a TabBar triggers mid-pass is queued, not run, so
            // every control measures its content while still active.
            _deferredOpenActions = new List<Action>();
            foreach (var node in result.ApplyOrder)
                ControlAttributeApplier.Apply(node, _nodeMap[node],
                                              _registry.Resolve(node.Tag), Variants);
            // scale must run after _nodeMap is populated and attributes have been applied
            // (so it doesn't fight ApplyCommon writes).
            RecomputeFactorScale();
            ApplyScales();
            AttachPixelSnaps(root);
            // Measuring is done (apply pass + ApplyScales). Run the deferred initial hides
            // now, then close the window so all later (runtime) toggles hide immediately.
            var deferredHides = _deferredOpenActions;
            _deferredOpenActions = null;
            foreach (var hide in deferredHides) hide();
            _variantSub = Variants.Changed.Subscribe(_ => ReSolve());
            _themeHandler = _ => ReSolve();
            UI.Theme.Changed += _themeHandler;
        }

        // Run <paramref name="action"/> after Open()'s measuring pass, or immediately if no
        // Open is in progress (runtime toggles / ReSolve). Used by Tab.bind to postpone the
        // initial deactivation of an unselected page so its auto-sized descendants measure
        // while active. See the _deferredOpenActions field note.
        internal void DeferDuringOpen(Action action)
        {
            if (_deferredOpenActions != null) _deferredOpenActions.Add(action);
            else action();
        }

        private void ApplyCanvasScaler(UnityEngine.UI.CanvasScaler scaler)
        {
            var mode = ResolveScaleMode();
            _isPixelMode = mode == ScaleMode.Pixel;
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
                _canvasFactor = 1f;
                return;
            }
            var size = parsed.Value;
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = size;
            scaler.matchWidthOrHeight = size.x >= size.y ? 0f : 1f;
            // Cache the effective factor for 'Nx' scale. Replicates Unity's
            // ScaleWithScreenSize output at the match endpoints we use (0 → width-locked,
            // 1 → height-locked). Same screen-size source as pixel mode.
            var screenPx = UI.CanvasSizeOverride != null
                ? UI.CanvasSizeOverride()
                : ReadCanvasRectSize();
            _canvasFactor = size.x >= size.y
                ? (size.x > 0f ? screenPx.x / size.x : 1f)
                : (size.y > 0f ? screenPx.y / size.y : 1f);
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
                _canvasFactor = 1f;
                return;
            }
            var canvasSize = UI.CanvasSizeOverride != null
                ? UI.CanvasSizeOverride()
                : ReadCanvasRectSize();
            var factor = PixelScaleSolver.Solve(
                canvasSize, design.Value, UI.PixelScalePowerOfTwo);
            if (UI.MinPixelScale > 0f && factor < UI.MinPixelScale)
                factor = UI.MinPixelScale;
            scaler.uiScaleMode = UnityEngine.UI.CanvasScaler.ScaleMode.ConstantPixelSize;
            scaler.scaleFactor = factor;
            _canvasFactor = factor;
        }

        // Applies per-element 'scale' attribute as RectTransform.localScale (relative to
        // the layout box; works in any scale-mode), then box-preserving compensation so the
        // declared anchor/size/margin keeps describing the VISUAL box — 'scale' only changes
        // render density, not the box. Called at Open after the attribute apply loop, and at
        // ReSolve when variants change; both run ApplyCommon first (resets the RectTransform
        // to its margin-resolved baseline), so reading that baseline here is idempotent.
        //
        // Plain-multiplier 'scale="N"' has no dependence on canvas factor. The device-density
        // form 'scale="Nx"' and the canvas-relative form 'scale="<r>r"' both divide by
        // _canvasFactor, so a factor change (canvas resize) must re-run this — routed via
        // ReSolve in OnCanvasDimensionsChanged when _hasFactorScale.
        //
        // Walks every Control in _nodeMap so nodes that declared 'scale' only via a
        // variant override are still tracked (resolves to null → identity reset).
        private void ApplyScales()
        {
            foreach (var kv in _nodeMap)
                ApplyScaleToNode(kv.Key, kv.Value, dynamicBaseline: false);
            PruneDeadDynamicSubtrees();
            foreach (var subtree in _dynamicSubtrees)
                ApplyScalesTo(subtree.Nodes);
        }

        // Pixel 模式下给子树里每个 TMP 文本挂 PixelSnap——Canvas.pixelPerfect 不吸 TMP 字形，
        // 这里把文本渲染原点吸到设备整数像素。幂等（已挂则跳过）；Auto 模式 no-op。
        // 见 spec 2026-06-17-pixel-position-snap (PPS-D1/D2)。
        private void AttachPixelSnaps(UnityEngine.GameObject subtreeRoot)
        {
            if (!_isPixelMode || subtreeRoot == null) return;
            var texts = subtreeRoot.GetComponentsInChildren<TMPro.TMP_Text>(includeInactive: true);
            foreach (var t in texts)
                if (t.GetComponent<Controls.Internal.PixelSnap>() == null)
                    t.gameObject.AddComponent<Controls.Internal.PixelSnap>();
        }

        private void ApplyScalesTo(Dictionary<ElementNode, Control> nodes)
        {
            foreach (var kv in nodes)
                ApplyScaleToNode(kv.Key, kv.Value, dynamicBaseline: true);
        }

        private void ApplyScaleToNode(ElementNode node, Control control, bool dynamicBaseline)
        {
            ApplyScaleToNodeCore(node, control, dynamicBaseline);
            // STW-D7(2): wrapper 模式下 scale 变更（Variant / resize 重算 Nx、<r>r）后内层
            // localScale 已变，但 TMP 文本没变 → TEXT_CHANGED 不会响——这里替它标脏
            // wrapper（_layoutHostForScaleDirty），LayoutRebuilder 自动上溯到外层 LayoutGroup，
            // 让 bridge 的 ×s 新值参与下一次布局 pass。
            if (control._layoutHostForScaleDirty != null)
                UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(control._layoutHostForScaleDirty);
        }

        // dynamicBaseline: 静态节点（_nodeMap）靠 ReSolve 的 ApplyCommon 把 RectTransform
        // 重置到 margin-resolved 基线，box-preserving 补偿才幂等；动态子树节点属性只在
        // 实例化时 Apply 一次，没有这个重置——首次应用前捕获基线，之后每次先还原。
        private void ApplyScaleToNodeCore(ElementNode node, Control control, bool dynamicBaseline)
        {
            var declaredBase = node.Attributes.ContainsKey("scale");
            var declaredVariant = node.VariantOverrides.ContainsKey("scale");
            if (!declaredBase && !declaredVariant) return;

            var raw = PromptUGUI.Variants.VariantResolver.ResolveAttribute(
                node, "scale", Variants);
            var rt = control.RectTransform;
            if (rt == null) return;

            if (dynamicBaseline)
            {
                if (control._dynamicScaleBaseline is { } b)
                {
                    rt.anchorMin = b.AnchorMin;
                    rt.anchorMax = b.AnchorMax;
                    rt.sizeDelta = b.SizeDelta;
                    rt.anchoredPosition = b.AnchoredPosition;
                }
                else
                {
                    control._dynamicScaleBaseline =
                        (rt.anchorMin, rt.anchorMax, rt.sizeDelta, rt.anchoredPosition);
                }
            }

            if (TryParseDeviceScale(raw, out var devN))
            {
                var f = _canvasFactor > 0f ? _canvasFactor : 1f;
                var dv = devN / f;
                rt.localScale = new Vector3(dv, dv, 1f);
                ApplyBoxPreservingCompensation(rt, dv);
                return;
            }

            if (TryParseRelativeScale(raw, out var relR))
            {
                var f = _canvasFactor > 0f ? _canvasFactor : 1f;
                // round-half-up to the nearest integer effective (>= 1), then divide the
                // factor back out: net physical-px/unit = effective (integer → pixel-aligned),
                // and grows with f (responds to window size). See CRS-D3/D4/D5.
                var eff = Mathf.Max(1f, Mathf.Floor(f * relR + 0.5f));
                var dv = eff / f;
                rt.localScale = new Vector3(dv, dv, 1f);
                ApplyBoxPreservingCompensation(rt, dv);
                return;
            }

            if (string.IsNullOrEmpty(raw)
                || !float.TryParse(raw, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out var v)
                || v <= 0f)
            {
                // Unresolved / non-numeric (e.g. <Animation scale="1:0.5">) → identity.
                // The baseline geometry was restored above (dynamic) or by ApplyCommon (static).
                rt.localScale = Vector3.one;
                return;
            }

            rt.localScale = new Vector3(v, v, 1f);
            ApplyBoxPreservingCompensation(rt, v);
        }

        // ScreenInstantiator.InstantiateNode（BindItems / Markdown 动态实例化）完成后调用。
        // 无 scale 声明的子树不登记（绝大多数列表卡片），零额外开销。
        internal void RegisterDynamicSubtree(Control root, Dictionary<ElementNode, Control> nodes)
        {
            AttachPixelSnaps(root.GameObject);
            PruneDeadDynamicSubtrees();
            var hasScale = false;
            foreach (var node in nodes.Keys)
            {
                if (!hasScale && (node.Attributes.ContainsKey("scale")
                                  || node.VariantOverrides.ContainsKey("scale")))
                    hasScale = true;
                // 动态子树可能是 Screen 里唯一的 factor scale 来源；resize 门控必须看到它。
                if (DeclaresFactorScale(node)) _hasFactorScale = true;
            }
            if (!hasScale) return;
            _dynamicSubtrees.Add(new DynamicSubtree { Root = root, Nodes = nodes });
            ApplyScalesTo(nodes);
        }

        private void PruneDeadDynamicSubtrees()
        {
            for (var i = _dynamicSubtrees.Count - 1; i >= 0; i--)
                if (_dynamicSubtrees[i].Root.GameObject == null)
                    _dynamicSubtrees.RemoveAt(i);
        }

        // Sets _hasFactorScale if any currently-instantiated node uses a factor-dependent scale
        // form (scale="Nx" or scale="<r>r"). Called at Open and re-run in ReSolve: Add-block
        // activation (Strategy C) can introduce such nodes into _nodeMap after Open. Activated
        // nodes stay in _nodeMap, so the flag is effectively sticky once any such node exists.
        private void RecomputeFactorScale()
        {
            _hasFactorScale = HasFactorScaleNode();
        }

        private bool HasFactorScaleNode()
        {
            foreach (var node in _nodeMap.Keys)
                if (DeclaresFactorScale(node)) return true;
            foreach (var subtree in _dynamicSubtrees)
                foreach (var node in subtree.Nodes.Keys)
                    if (DeclaresFactorScale(node)) return true;
            return false;
        }

        // Whether a node declares a factor-dependent scale (Nx or <r>r) in its base attribute
        // or any variant override.
        private static bool DeclaresFactorScale(ElementNode node)
        {
            if (node.Attributes.TryGetValue("scale", out var baseVal)
                && (TryParseDeviceScale(baseVal, out _) || TryParseRelativeScale(baseVal, out _))) return true;
            if (node.VariantOverrides.TryGetValue("scale", out var list))
                foreach (var (_, value) in list)
                    if (TryParseDeviceScale(value, out _) || TryParseRelativeScale(value, out _)) return true;
            return false;
        }

        // scale="Nx" (N positive integer): localScale = N / canvasFactor → renders the
        // element at exactly N physical pixels per design-unit, independent of the auto
        // factor. Returns false for the plain-multiplier form (handled by float.TryParse).
        private static bool TryParseDeviceScale(string raw, out int n)
        {
            n = 0;
            if (string.IsNullOrEmpty(raw) || raw.Length < 2 || raw[raw.Length - 1] != 'x') return false;
            return int.TryParse(raw.Substring(0, raw.Length - 1),
                System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out n) && n >= 1;
        }

        // scale="<r>r" (r positive float): localScale = max(1, round(canvasFactor·r)) / canvasFactor
        // → scales relative to the factor but snaps net physical-px/unit to the nearest integer
        // so it stays pixel-aligned at any factor. Returns false for the 'Nx' and plain-multiplier
        // forms (handled by TryParseDeviceScale / float.TryParse).
        private static bool TryParseRelativeScale(string raw, out float r)
        {
            r = 0f;
            if (string.IsNullOrEmpty(raw) || raw.Length < 2 || raw[raw.Length - 1] != 'r') return false;
            return float.TryParse(raw.Substring(0, raw.Length - 1),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out r) && r > 0f;
        }

        // Inflates a just-baselined RectTransform by 1/scale so that localScale=scale renders
        // it back to its declared box (XML skill, "Relative scale"). Per axis: widen the anchor span by 1/scale
        // about its center, and divide sizeDelta by scale. A point (fixed) axis has span 0, so
        // only its sizeDelta changes and the anchored edge stays put; a stretch / fractional
        // axis has pivot 0.5, so widening about the center keeps the box centered. anchoredPosition
        // is unchanged. Skipped under a LayoutGroup parent — see the guard below for the full why.
        private static void ApplyBoxPreservingCompensation(RectTransform rt, float scale)
        {
            // Box-preserving is INTENTIONALLY skipped for a direct child of a LayoutGroup
            // (VStack/HStack/Grid). Two fundamental reasons — do NOT "fix" this by deleting the guard:
            //
            //   1. Mechanism: a LayoutGroup with childControlWidth/Height=true re-drives the child's
            //      anchors (forced to (0,1)), sizeDelta and anchoredPosition through a
            //      DrivenRectTransformTracker on EVERY layout pass. The one-shot anchor/sizeDelta
            //      inflation below would be overwritten on the next rebuild — it cannot stick.
            //   2. Contract: box-preserving means "the declared anchor/size/margin IS the visual box;
            //      scale only changes render density." A LayoutGroup child has no declared box — the
            //      group computes it from siblings / spacing / flexible weights. There is no stable
            //      box to preserve, so the contract is ill-defined here.
            //
            // Unity's "Use Child Scale" toggle (childScaleWidth/Height on a Horizontal/Vertical
            // LayoutGroup; Grid has none) does NOT substitute: it only shrinks the SPACE BUDGET of
            // FIXED-size children on the packing axis so siblings pack tight (fixes the "small text
            // gap"). It never makes a flexible (width="stretch") child fill its row at higher
            // density — SetChildAlongAxisWithScale still sets sizeDelta to the full allocated size,
            // so localScale halves the visual regardless; and space freed by scaling a fixed child
            // is absorbed by any stretch sibling, so it's often a visual no-op (verified empirically).
            //
            // Supported fix: give the scaled element a real declared box — wrap it in a <Frame>
            // (free-positioning), put layout sizing (width="stretch" / fixed height) on the Frame and
            // scale="..." + anchor="stretch" on the inner element. Then this method runs (parent is
            // not a LayoutGroup) and the element renders at density inside the Frame's box. See the
            // XML skill "Relative scale" LayoutGroup-skip caveat.
            var parent = rt.parent;
            if (parent != null && parent.GetComponent<UnityEngine.UI.LayoutGroup>() != null)
                return;

            var inv = 1f / scale;
            var baseMin = rt.anchorMin;
            var baseMax = rt.anchorMax;
            var baseSize = rt.sizeDelta;
            var basePos = rt.anchoredPosition;

            var cx = (baseMin.x + baseMax.x) * 0.5f;
            var cy = (baseMin.y + baseMax.y) * 0.5f;
            var hx = (baseMax.x - baseMin.x) * 0.5f * inv;
            var hy = (baseMax.y - baseMin.y) * 0.5f * inv;

            rt.anchorMin = new Vector2(cx - hx, cy - hy);
            rt.anchorMax = new Vector2(cx + hx, cy + hy);
            // Re-anchoring makes Unity re-derive sizeDelta / anchoredPosition to hold the
            // current offsets; overwrite both so the result is a pure function of the baseline.
            rt.sizeDelta = baseSize * inv;
            rt.anchoredPosition = basePos;
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
                if (scaler == null) return;
                if (_hasFactorScale)
                    // 'Nx' localScale depends on the factor: re-baseline + recompute via the
                    // tested ReSolve path (ApplyCommon → ApplyCanvasScaler → ApplyScales) so the
                    // box-preserving inflation does not accumulate.
                    ReSolve();
                else
                    ApplyCanvasScaler(scaler);
            }
            finally { _isReapplyingScaler = false; }
        }

        public void Close()
        {
            _variantSub?.Dispose();
            _variantSub = null;
            if (_themeHandler != null)
            {
                UI.Theme.Changed -= _themeHandler;
                _themeHandler = null;
            }
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
            _dynamicSubtrees.Clear();
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
            // re-applying attributes to them below. Their SetActive(false) state must not be
            // clobbered by ApplyCommon — a node declaring hidden="false" would be un-hidden
            // (Hidden is written only when declared), and interactable/geometry rewrite unconditionally.
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
            // Skip attribute re-application for inactive Add block nodes to avoid ApplyCommon
            // un-hiding a node that declares hidden="false" (Hidden is applied only when the node
            // declares it — `if (hidden.HasValue)` in Control.ApplyCommon — see spec §8.3).
            foreach (var kv in _nodeMap)
            {
                var node = kv.Key;
                if (inactiveNodes.Contains(node)) continue;
                var control = kv.Value;
                // BindItems 重建（Carousel/TabBar）会销毁静态 XML 卡的 GameObject，
                // 但其 ElementNode 仍留在 _nodeMap —— 跳过已销毁的控件（同 ApplyScales 的 rt==null 防御）。
                if (control.GameObject == null) continue;
                var entry = _registry.Resolve(node.Tag);
                ControlAttributeApplier.Apply(node, control, entry, Variants, initial: false);
            }
            RecomputeFactorScale();
            ApplyCanvasScaler(RootGameObject.GetComponent<UnityEngine.UI.CanvasScaler>());
            ApplyScales();
            AttachPixelSnaps(RootGameObject);
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
