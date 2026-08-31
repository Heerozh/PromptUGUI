using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using PromptUGUI.Registry;
using UnityEngine;

namespace PromptUGUI.Controls
{
    public abstract class Control : IControl
    {
        public string Id { get; internal set; }
        public GameObject GameObject { get; private set; }
        public RectTransform RectTransform { get; private set; }
        private CanvasGroup _canvasGroup;
        private RectTransform _layoutHost;

        /// <summary>
        /// LayoutGroup 量算用的宿主 RectTransform，默认 = 自身 RectTransform。
        /// V/HStack 直下声明了 scale 的 &lt;Text&gt; 由 ScreenInstantiator 指向自动插入的
        /// wrapper（spec STW-D4）：ApplyCommon 的父级判断、LayoutElement 落点、Hidden 的
        /// SetActive、Dispose 的销毁对象都以它为准；内层 RectTransform 只承载视觉与
        /// box-preserving 膨胀。
        /// </summary>
        internal RectTransform LayoutHost
        {
            get => _layoutHost != null ? _layoutHost : RectTransform;
            set => _layoutHost = value;
        }

        /// <summary>包装时 = wrapper GO（SetActive / Destroy 的作用对象），否则 = 自身 GameObject。</summary>
        internal GameObject HostGameObject
            => _layoutHost != null ? _layoutHost.gameObject : GameObject;

        /// <summary>wrapper 存在时返回它（scale 变更脏标用），否则 null。仅 Screen 读。</summary>
        internal RectTransform _layoutHostForScaleDirty => _layoutHost;

        /// <summary>
        /// Whether this control's <see cref="UnityEngine.UI.Graphic"/> participates in a parent
        /// Btn / Tab / Toggle's state-driven <c>*Modulate</c> tint fan-out. Set
        /// <c>stateReact="false"</c> to opt this control (and its subtree) out — the installer then
        /// skips it (no modulate tint on hover / press / select / disable). Has no effect on the
        /// absolute <c>*Color</c> attributes (those apply to the source's bg only). Default <c>true</c>.
        /// </summary>
        [UIAttr, Preserve]
        public bool StateReact { get; set; } = true;

        private readonly List<IControl> _children = new();
        private System.Collections.Generic.List<System.IDisposable> _subscriptions;

        public bool Hidden
        {
            get => !HostGameObject.activeSelf;
            set => HostGameObject.SetActive(!value);
        }

        public virtual bool Interactable
        {
            get => CanvasGroup.interactable;
            // Only flips interactable, NOT blocksRaycasts: a disabled control must still SWALLOW the
            // pointer (standard Unity disabled-Selectable behaviour), else the click leaks through to
            // whatever sits behind it — e.g. a modal backdrop whose OnPointerDown cancels (clicking a
            // disabled CenteredSlideBox button used to close the window). Toast-style click-through
            // overlays set blocksRaycasts=false directly on their own CanvasGroup instead.
            set => CanvasGroup.interactable = value;
        }

        private CanvasGroup CanvasGroup => _canvasGroup ??= GameObject.AddComponent<CanvasGroup>();

        internal void AttachTo(GameObject go)
        {
            GameObject = go;
            RectTransform = go.GetComponent<RectTransform>()
                            ?? go.AddComponent<RectTransform>();
            OnAttached();
        }

        public virtual void OnAttached() { }

        /// <summary>
        /// 实例化子节点时 ScreenInstantiator 用作 parent 的 Transform。
        /// 默认 = 自身 RectTransform；Animation 等需要"在 transform 树里多塞一层"的控件 override 它，
        /// 这样子节点 parent 到那一层，而不是自身根 GameObject。
        /// </summary>
        /// <remarks>必须返回一个 RectTransform — uGUI 子节点要求父也是 RectTransform。</remarks>
        protected internal virtual Transform ChildHostTransform => RectTransform;

        /// <summary>
        /// 在 <see cref="ControlAttributeApplier"/> 调用 <see cref="ApplyCommon"/> 之后再触发一次，
        /// 让一些控件在 Variant ReSolve / 初始 Apply 完成后做"恢复其它逻辑写入的 RectTransform / 组件状态"
        /// 这类收尾。默认实现为空；目前只有 SafeArea 重写。
        /// </summary>
        /// <summary>
        /// Called once at the start of every attribute-application pass, before any setter runs.
        ///
        /// <para>Needed because a setter firing is the only signal a control gets that an attribute
        /// exists — <c>ControlAttributeApplier</c> skips an attribute whose value does not resolve
        /// (<c>if (v == null) continue;</c>), so "the author stopped declaring this" arrives as
        /// silence. A control that has to react to absence — the procedural surface turning off when
        /// a variant-only <c>radius.mobile</c> goes inactive — clears its per-pass state here and
        /// reconciles in <see cref="OnAfterApply"/>. That is spec §8's "compute, don't latch".</para>
        /// </summary>
        internal virtual void OnBeforeApply() { }

        internal virtual void OnAfterApply() { }

        /// <summary>
        /// 上次 <see cref="ControlAttributeApplier"/> 通过 DefaultTextAttr 写入的字符串。
        /// ReSolve 阶段拿来跟 <see cref="PeekDefaultText"/> 的当前值对比 —— 若当前值已被
        /// 调用方通过 setter 改掉(如 MessageBoxRequest.Bind 改 TextValue), 就不再被 XML
        /// 声明值覆盖；i18n locale 切换场景 (control 当前 text 还是上次 Apply 自己写的)
        /// 则正常重 Apply 翻译结果。仅 ControlAttributeApplier 读写。
        /// </summary>
        internal string _lastAppliedDefaultText;

        /// <summary>
        /// 返回 control 当前渲染的 default-text 字符串 (e.g. TMP_Text.text)。控件没有
        /// DefaultTextAttr 或文本未初始化时返回 null。仅 <see cref="ControlAttributeApplier"/>
        /// 用于检测 runtime 覆写。
        /// </summary>
        internal virtual string PeekDefaultText() => null;

        /// <summary>
        /// 上次 <see cref="ControlAttributeApplier"/> 通过 RuntimeStateAttr 应用并回读的归一化
        /// 字符串 (e.g. Tab.isOn / Slider.value)。ReSolve 阶段跟 <see cref="PeekRuntimeState"/>
        /// 当前值对比：相等 → runtime 没动过, Variant 覆盖正常重 Apply；不等 → 用户/代码改过,
        /// 不被 XML 声明值打回。仅 ControlAttributeApplier 读写。
        /// </summary>
        internal string _lastAppliedRuntimeState;

        /// <summary>
        /// 动态子树（BindItems / Markdown 经 ScreenInstantiator.InstantiateNode 实例化）里
        /// 声明了 scale 的节点的几何基线。静态节点每次 ReSolve 先经 ApplyCommon 重置
        /// RectTransform 再做 box-preserving 补偿；动态节点属性只在实例化时 Apply 一次,
        /// 所以 Screen.ApplyScales 首次应用前捕获基线、之后每次先还原, 保证补偿不跨
        /// resize / Variant 切换累积。仅 <see cref="PromptUGUI.Application.Screen"/> 读写。
        /// </summary>
        internal (UnityEngine.Vector2 AnchorMin, UnityEngine.Vector2 AnchorMax,
                  UnityEngine.Vector2 SizeDelta, UnityEngine.Vector2 AnchoredPosition)?
            _dynamicScaleBaseline;

        /// <summary>
        /// 返回 control 当前运行时独占状态值的归一化字符串 (e.g. Tab.isOn → "true"/"false",
        /// Slider.value → invariant float)。没有 RuntimeStateAttr 的控件返回 null。
        /// 仅 <see cref="ControlAttributeApplier"/> 用于检测 runtime 覆写。
        /// </summary>
        internal virtual string PeekRuntimeState() => null;

        internal void AddChild(IControl child) => _children.Add(child);

        public IReadOnlyList<IControl> Children => _children;

        private static readonly IReadOnlyDictionary<string, IControl> EmptyDict =
            new Dictionary<string, IControl>();

        private Dictionary<string, IControl> _scopedIds;

        public IReadOnlyDictionary<string, IControl> ScopedIds => _scopedIds ?? EmptyDict;

        public T Get<T>(string idPath) where T : class, IControl
        {
            var c = Get(idPath);
            return c as T ?? throw new System.InvalidCastException(
                $"control at '{idPath}' is {c?.GetType().Name ?? "null"}, not {typeof(T).Name}");
        }

        public IControl Get(string idPath)
        {
            if (string.IsNullOrEmpty(idPath))
                throw new System.ArgumentException("idPath is empty");
            var segs = idPath.Split('/');
            IControl current = this;
            foreach (var seg in segs)
            {
                if (!current.ScopedIds.TryGetValue(seg, out var next))
                    throw new System.Collections.Generic.KeyNotFoundException(
                        $"id '{seg}' not found under '{current.Id ?? current.GameObject?.name}'");
                current = next;
            }
            return current;
        }

        // 由 ScreenInstantiator 在 InsantiateRecursive 中调用：把模板内 id 累加到本 Control 的局部作用域
        internal void AddScopedId(string id, IControl c)
        {
            _scopedIds ??= new Dictionary<string, IControl>();
            _scopedIds[id] = c;
        }

        // 由 ScreenInstantiator 在遇到 IsTemplateInstanceRoot 节点时一次性挂载共享字典
        internal void ReplaceScopedIds(Dictionary<string, IControl> dict)
        {
            _scopedIds = dict;
        }

        public virtual UnityEngine.Vector2? GetNativeSize() => null;

        // BCS-D7 follow-up: when true, this control already carries a component that is itself a live
        // UnityEngine.UI.ILayoutElement (e.g. TMP on <Text>) and reports its preferred size dynamically
        // as the LayoutGroup pass constrains the cross axis. For such controls the one-time GetNativeSize
        // snapshot is not just redundant but harmful — pinned onto an explicit LayoutElement (priority 1)
        // it OUTRANKS the component's own intrinsic report (priority 0) and freezes the axis at its
        // instantiation-time measurement, so e.g. a wrap="true" <Text> can never grow past one line.
        // Controls that report this leave any author-omitted axis at the -1 sentinel inside a V/HStack,
        // letting the intrinsic ILayoutElement drive it. Default false: composite controls like <Btn>,
        // whose GetNativeSize computes "label width + padding" that no intrinsic ILayoutElement reports,
        // still need the snapshot. Only affects the LayoutGroup path (ApplyLayoutElement); free-positioning
        // still uses GetNativeSize so an omitted-size <Text> doesn't collapse to an invisible (0,0).
        protected internal virtual bool UsesIntrinsicLayoutSize => false;

        // DSS-D14: 作者省略 anchor= 时的默认 preset。基类返回 top-left（沿用既有行为）；
        // 容器类（Frame）覆写按 sizeSpec.HasWidth/HasHeight 决定每轴 stretch 还是 top/left。
        protected virtual AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
            => new(AnchorVertical.Top, AnchorHorizontal.Left);

        /// <summary>
        /// Whether this control can take part in a parent LayoutGroup's flow at all. False makes
        /// every <c>flow</c> branch below behave as if the author had written <c>flow="false"</c> —
        /// for <c>&lt;Decor&gt;</c>, whose instances hang off the host's edges and have no business
        /// claiming a slot or contributing a preferred size.
        /// </summary>
        protected internal virtual bool ParticipatesInLayout => true;

        // 通用属性应用（由 ScreenInstantiator 在子类自身属性应用之后调用）
        public void ApplyCommon(string anchor, string size, string width, string height,
                                string margin, string pivot,
                                bool? hidden, bool interactable, bool flow = true)
        {
            // Folded in once, up front, so the LayoutElement / fractional / preferred-size branches
            // below all read one answer rather than each remembering to ask twice.
            flow &= ParticipatesInLayout;

            var sizeSpec = SizeSpec.Parse(size, width, height);

            if (sizeSpec.IsNativeWidth || sizeSpec.IsNativeHeight)
            {
                var native = GetNativeSize();
                if (native.HasValue)
                    sizeSpec = sizeSpec.WithNativeResolved(native.Value);
            }

            var preset = string.IsNullOrEmpty(anchor)
                ? GetDefaultAnchor(sizeSpec)
                : AnchorPreset.Parse(anchor);

            sizeSpec.ValidateAgainst(preset);

            // spec §6.5: 父级是 VStack/HStack 时走 LayoutElement 通道；
            // GridLayoutGroup 例外（它直接用 cellSize，LayoutElement 在它下面被忽略）。
            var parentLg = LayoutHost.parent != null
                ? LayoutHost.parent.GetComponent<UnityEngine.UI.LayoutGroup>()
                : null;
            var parentIsGrid = parentLg is UnityEngine.UI.GridLayoutGroup;
            var parentIsAutoLayout = parentLg != null && !parentIsGrid;

            // flow="false"：子节点退出排版流。LayoutGroup（含 Grid）收集 rectChildren 时跳过
            // ignoreLayout 的子节点 → 它既不占排版空间也不贡献 preferred，anchor / margin /
            // size 走下面的自由定位分支，恢复完整语义（典型：Stack 里铺满的背景层 / 角标）。
            // 回流时清回 false（不摘组件），保 Variant 切换 / ReSolve 幂等。
            if (parentLg != null)
            {
                var flowLe = LayoutHost.gameObject.GetComponent<UnityEngine.UI.LayoutElement>();
                if (!flow)
                {
                    flowLe ??= LayoutHost.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
                    flowLe.ignoreLayout = true;
                }
                else if (flowLe != null)
                {
                    flowLe.ignoreLayout = false;
                }
            }

            // 百分比 ('%') 在任何 LayoutGroup 容器（V/HStack 或 Grid）里都无法表达：
            // - V/HStack 用的是 flex 权重，不是父尺寸百分比；
            // - Grid 用的是 cellSize，子节点的 anchor 直接被 GridLayoutGroup 覆写。
            // 给出可操作的提示：用加权 stretch + spacer 兄弟，或者移到自由定位父级。
            if ((parentIsAutoLayout || parentIsGrid) && flow
                && (sizeSpec.IsFractionalWidth || sizeSpec.IsFractionalHeight))
            {
                throw new System.ArgumentException(
                    "'%' (fractional) width/height cannot be used inside <VStack>/<HStack>/<Grid> — " +
                    "Unity LayoutGroup distributes by flex weight (or fixed cellSize for Grid), not parent percentage. " +
                    "Use a weighted stretch + spacer pattern instead: " +
                    "<Frame width=\"stretch\"/> <Btn width=\"stretch*2\"/> <Frame width=\"stretch\"/> " +
                    "gives a 25/50/25 split. " +
                    "Or move the child to a free-positioning parent (Frame/Screen) where '%' maps to anchor fractions. " +
                    "For a bounded range inside a stack write clamp(min, stretch, max), not clamp(min, N%, max).");
            }

            if (parentIsAutoLayout && flow)
            {
                ApplyLayoutElement(sizeSpec, preset);
                SyncClampFitter(sizeSpec, preset, margin, freePositioning: false);
                // anchor / pivot / sizeDelta / anchoredPosition: LayoutGroup 接管几何。
                // 作者写 anchor/margin 已经被 ScreenInstantiator 警告（spec §6.5）；这里静默跳过。
                // STW-D4: wrapper 模式下内层 RT 重置为全 stretch 基线——这是 ApplyScales
                // box-preserving 膨胀的输入（"ApplyCommon 先重置、ApplyScales 再膨胀"契约；
                // wrapper 本身的几何由 LayoutGroup 驱动）。
                if (_layoutHost != null)
                {
                    RectTransform.anchorMin = Vector2.zero;
                    RectTransform.anchorMax = Vector2.one;
                    RectTransform.pivot = new Vector2(0.5f, 0.5f);
                    RectTransform.sizeDelta = Vector2.zero;
                    RectTransform.anchoredPosition = Vector2.zero;
                }
            }
            else
            {
                // 'stretch' 关键字只在 V/HStack 子节点上有意义（映射到 LayoutElement.flexibleX=1）。
                // 自由布局父级（Frame/Screen/Grid）下没有 flex weight 概念，作者真要拉伸应改用 anchor="stretch"。
                // 静默忽略会让作者以为生效了，所以显式抛错。
                if (sizeSpec.IsFlexibleWidth || sizeSpec.IsFlexibleHeight)
                    throw new System.ArgumentException(
                        "'stretch' on width/height is only valid inside <VStack>/<HStack>; " +
                        "use anchor=\"stretch\" (or anchor=\"X-stretch\") + margin for free-positioning containers " +
                        "— for a bounded range here write clamp(min, N%, max), not clamp(min, stretch, max)");

                // BCS-D7: 自由定位 + anchor 两轴都不 stretch + 至少一轴没写 →
                // 若控件能提供 native size (GetNativeSize)，缺失的轴用 native 填，已写的轴保留作者值。
                // 避免 sizeDelta=(0,0) 不可见，并支持 CSS 直觉：写 height、width 仍按内容自适应。
                // size="native" 关键字处理在前 (IsNativeWidth/Height)，两条互斥。
                if (!preset.StretchX && !preset.StretchY
                    && (!sizeSpec.HasWidth || !sizeSpec.HasHeight))
                {
                    var nativeFallback = GetNativeSize();
                    if (nativeFallback.HasValue)
                    {
                        sizeSpec = sizeSpec.WithFallbackForMissing(nativeFallback.Value);
                    }
                }

                AnchorResolver.Resolve(preset,
                    out var aMin, out var aMax, out var p);

                // 分数尺寸 (e.g. width="50%") 把 anchor 改成父容器的子区间。
                // 对应轴的 pivot 强制 0.5，让 MarginResolver 的 stretch 路径（对称偏移公式）直接复用；
                // sizeDelta 由 margin 之差驱动，0 margin 时为 0（完全 anchor 驱动）。
                if (sizeSpec.IsFractionalWidth)
                {
                    ComputeFractionalAnchor(preset.H, sizeSpec.WidthFraction,
                        out var min, out var max);
                    aMin.x = min; aMax.x = max;
                    p.x = 0.5f;
                }
                if (sizeSpec.IsFractionalHeight)
                {
                    ComputeFractionalAnchor(preset.V, sizeSpec.HeightFraction,
                        out var min, out var max);
                    aMin.y = min; aMax.y = max;
                    p.y = 0.5f;
                }

                RectTransform.anchorMin = aMin;
                RectTransform.anchorMax = aMax;

                if (!string.IsNullOrEmpty(pivot))
                {
                    var parts = pivot.Split(',');
                    if (parts.Length != 2)
                        throw new System.ArgumentException(
                            $"pivot '{pivot}' must be 'x,y' (two comma-separated numbers in 0..1, e.g. '0.5,0.5')");
                    RectTransform.pivot = new Vector2(
                        ParsePivotComponent(parts[0], pivot, "x"),
                        ParsePivotComponent(parts[1], pivot, "y"));
                }
                else
                {
                    RectTransform.pivot = p;
                }

                // 分数轴行为上等同 stretch（在子区间内由 margin 收缩），MarginResolver 的 stretch 分支
                // 公式刚好满足：sizeDelta = -(l+r)（无 margin 则 0），anchoredPosition = (l-r)/2（对称居中）。
                // 这里合成一个"有效 preset"——把分数轴标记为 stretch——让 MarginResolver 走那条分支。
                var effectivePreset = new AnchorPreset(
                    sizeSpec.IsFractionalHeight ? AnchorVertical.Stretch : preset.V,
                    sizeSpec.IsFractionalWidth ? AnchorHorizontal.Stretch : preset.H);

                var lr = MarginResolver.Resolve(effectivePreset, sizeSpec, margin);
                RectTransform.anchoredPosition = lr.AnchoredPosition;
                RectTransform.sizeDelta = lr.SizeDelta;

                // clamp(min, N%, max): the baseline above is the plain % geometry; the fitter owns the
                // clamped axis from here on (spec 2026-08-30-clamp-size-design §5.1 / §6.3).
                SyncClampFitter(sizeSpec, preset, margin, freePositioning: true);
            }

            if (hidden.HasValue) Hidden = hidden.Value;
            Interactable = interactable;
        }

        private static float ParsePivotComponent(string component, string pivot, string axis)
        {
            if (!float.TryParse(component.Trim(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                throw new System.ArgumentException(
                    $"pivot '{pivot}': {axis} component '{component.Trim()}' is not a number " +
                    "(expected 'x,y' in 0..1, e.g. '0.5,0.5')");
            return v;
        }

        private void ApplyLayoutElement(SizeSpec sizeSpec, AnchorPreset preset)
        {
            // DSS-D16: 父级 V/HStack 的 cross 轴上，作者没写 size 且 preset 默认 stretch（来自 GetDefaultAnchor）
            // → LE 在该轴 preferred=0, flexible=1（CSS flex `align-items: stretch` 直译）。
            // 只 Frame 默认 anchor 会让 preset.Stretch* 在没 size 时为 true；其他控件保持 (Top, Left)，不触发。
            var parentHv = LayoutHost.parent != null
                ? LayoutHost.parent.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>()
                : null;
            var fillCrossX = parentHv is UnityEngine.UI.VerticalLayoutGroup && preset.StretchX && !sizeSpec.HasWidth;
            var fillCrossY = parentHv is UnityEngine.UI.HorizontalLayoutGroup && preset.StretchY && !sizeSpec.HasHeight;

            // 决策 LGC-D8 + BCS-D6 + BCS-D7 partial-write:
            // 任一轴没写 → 询问 GetNativeSize 作为该轴 fallback；写了的轴保留作者值。
            // 决策 LGC-D9: 没 native 时该轴留在 -1 哨兵值，让 Image/TMP 自带 ILayoutElement 主导。
            // UsesIntrinsicLayoutSize controls (e.g. <Text>) skip the native snapshot on any omitted
            // axis and leave it at the -1 sentinel so their own live ILayoutElement drives that axis.
            var needNativeFallback = (!sizeSpec.HasWidth || !sizeSpec.HasHeight) && !UsesIntrinsicLayoutSize;
            var native = needNativeFallback ? GetNativeSize() : null;
            var hasNative = native.HasValue;

            // 是否需要 LE：作者写了 size、或 native fallback 命中、或需要 cross 轴 fill。
            // 都不需要时若有残留 LE（Variant 切换可能挂过）→ 清回 -1。
            var needLE = sizeSpec.HasWidth || sizeSpec.HasHeight || hasNative || fillCrossX || fillCrossY;

            var le = LayoutHost.gameObject.GetComponent<UnityEngine.UI.LayoutElement>();
            if (!needLE)
            {
                if (le != null) WriteLayoutElement(le, -1f, -1f, -1f, -1f, -1f, -1f);
                return;
            }

            // 决策 LGC-D10: 两轴都从 -1 基线起算，清掉前一次 Variant 的残留约束。
            // 决策 LGC-D17: 显式、非 flexible 的尺寸要钉 min = preferred，否则 LayoutGroup 空间
            // 紧张时会用共享插值系数把固定尺寸子节点一起压缩（min 默认 -1=0 → 有收缩余地），
            // 违背"strictly NxN"契约。stretch/native fallback/cross-fill 轴保持 -1（可收缩）。
            var prefW = -1f;
            var prefH = -1f;
            var flexW = -1f;
            var flexH = -1f;
            var minW = -1f;
            var minH = -1f;

            if (sizeSpec.HasWidth)
            {
                if (sizeSpec.IsFlexibleWidth)
                {
                    // stretch / stretch*N: 让 LayoutGroup 把剩余空间按权重分给该子节点（VerticalLayoutGroup 跨轴
                    // 在 flexible>0 时把 requiredSpace 抬到容器内宽，HorizontalLayoutGroup 主轴则按
                    // flexible 权重分配剩余空间）。preferred=0 让 base 部分不抢权重。
                    //
                    // clamp(min, stretch, max)（spec 2026-08-30-clamp-size-design §5.2）：uGUI 主轴按
                    // lerp(min, preferred, t) 分配、交叉轴取 clamp(内宽, min, preferred)，所以
                    // min/preferred 就是 clamp 本身；有限上限时 flexible 必须为 0（否则越过 preferred），
                    // 上限开放时保留权重 —— 那是"带下限的 stretch"。这是 LGC-D17 之外唯一受支持的
                    // 可收缩区间。
                    ClampedFlexible(sizeSpec.IsClampedWidth, sizeSpec.MinWidth, sizeSpec.MaxWidth,
                        sizeSpec.WeightWidth, out prefW, out flexW, out minW);
                }
                else
                {
                    prefW = sizeSpec.Width;
                    flexW = 0f;
                    minW = sizeSpec.Width;
                }
            }
            else if (hasNative)
            {
                prefW = native.Value.x;
                // flexW 保持 -1（"无意见"），与历史 both-missing native 路径一致
            }

            if (sizeSpec.HasHeight)
            {
                if (sizeSpec.IsFlexibleHeight)
                {
                    ClampedFlexible(sizeSpec.IsClampedHeight, sizeSpec.MinHeight, sizeSpec.MaxHeight,
                        sizeSpec.WeightHeight, out prefH, out flexH, out minH);
                }
                else
                {
                    prefH = sizeSpec.Height;
                    flexH = 0f;
                    minH = sizeSpec.Height;
                }
            }
            else if (hasNative)
            {
                prefH = native.Value.y;
            }

            // DSS-D16: cross 轴 fill — 必须在 HasWidth/HasHeight 与 native 分支之后，
            // 因为它要把 preferred/-1 覆写成 0+flex=1。
            if (fillCrossX) { prefW = 0f; flexW = 1f; }
            if (fillCrossY) { prefH = 0f; flexH = 1f; }

            le ??= LayoutHost.gameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            WriteLayoutElement(le, prefW, prefH, flexW, flexH, minW, minH);
        }

        // stretch / stretch*N → (0, weight, -1); clamp(min, stretch, max) → (max, 0, min);
        // clamp(min, stretch*N, _) → (0, N, min). Open bounds map back to the LayoutElement sentinels.
        private static void ClampedFlexible(bool clamped, float min, float max, float weight,
            out float preferred, out float flexible, out float minimum)
        {
            if (!clamped)
            {
                preferred = 0f;
                flexible = weight;
                minimum = -1f;
                return;
            }
            var capped = !float.IsPositiveInfinity(max);
            preferred = capped ? max : 0f;
            flexible = capped ? 0f : weight;
            minimum = float.IsNegativeInfinity(min) ? -1f : min;
        }

        // 六个值一次算完再写。
        //
        // 决策 LGC-D18: LayoutElement 的 setter 自带 change guard —— 只有值真的变了才 SetDirty()
        // → LayoutRebuilder.MarkLayoutForRebuild：沿父链上溯到最外层布局根，每层
        // GetComponents(ILayoutGroup)，末尾还要 GetComponents(ILayoutElement) 验证自己。
        // 之前的写法是"先把六个全置 -1、再写真值"，稳态 ReSolve 里每个属性照样变两次 ——
        // 一个值没变，却每节点白脏 6 次。先算后写让那个 guard 真正生效：值没变的一遍完全免费。
        // 契约由 LayoutRebuildDirtyTests 钉住。
        private static void WriteLayoutElement(UnityEngine.UI.LayoutElement le,
            float preferredWidth, float preferredHeight,
            float flexibleWidth, float flexibleHeight,
            float minWidth, float minHeight)
        {
            le.preferredWidth = preferredWidth;
            le.preferredHeight = preferredHeight;
            le.flexibleWidth = flexibleWidth;
            le.flexibleHeight = flexibleHeight;
            le.minWidth = minWidth;
            le.minHeight = minHeight;
        }

        // Attaches / updates / retires the ClampFitter for width="clamp(min, N%, max)" (free-positioning
        // only — inside a LayoutGroup the clamp lives in LayoutElement min/preferred, see
        // ApplyLayoutElement). The component stays attached once created and is only enabled/disabled,
        // so a Variant flip between clamp and numeric keeps ReSolve idempotent (same rule as the
        // residual LayoutElement reset above). ClampFitter.SetAxis dirties only on a real spec change.
        private void SyncClampFitter(SizeSpec sizeSpec, AnchorPreset preset, string margin, bool freePositioning)
        {
            var fitter = RectTransform.GetComponent<Internal.ClampFitter>();
            var wantX = freePositioning && sizeSpec.IsClampedWidth;
            var wantY = freePositioning && sizeSpec.IsClampedHeight;
            if (!wantX && !wantY)
            {
                if (fitter == null) return;
                fitter.ClearAxis(0);
                fitter.ClearAxis(1);
                fitter.enabled = false;
                return;
            }

            MarginResolver.Parse(margin, out var mt, out var mr, out var mb, out var ml);
            fitter ??= RectTransform.gameObject.AddComponent<Internal.ClampFitter>();
            fitter.enabled = true;
            if (wantX)
                fitter.SetAxis(0, true, Internal.ClampMode.Fraction, sizeSpec.WidthFraction,
                    sizeSpec.MinWidth, sizeSpec.MaxWidth, ml, mr, ToClampAlign(preset.H));
            else
                fitter.ClearAxis(0);
            if (wantY)
                fitter.SetAxis(1, true, Internal.ClampMode.Fraction, sizeSpec.HeightFraction,
                    sizeSpec.MinHeight, sizeSpec.MaxHeight, mb, mt, ToClampAlign(preset.V));
            else
                fitter.ClearAxis(1);
        }

        private static Internal.ClampAlign ToClampAlign(AnchorHorizontal h) => h switch
        {
            AnchorHorizontal.Left => Internal.ClampAlign.Low,
            AnchorHorizontal.Right => Internal.ClampAlign.High,
            _ => Internal.ClampAlign.Center,   // Stretch is rejected by ValidateAgainst before we get here
        };

        private static Internal.ClampAlign ToClampAlign(AnchorVertical v) => v switch
        {
            AnchorVertical.Bottom => Internal.ClampAlign.Low,
            AnchorVertical.Top => Internal.ClampAlign.High,
            _ => Internal.ClampAlign.Center,
        };

        // 把 anchor 预设里的"端点"对齐方式 + 分数 转成具体的 anchorMin/Max 子区间。
        // 入参：H/V 的端点（Left/Right/Top/Bottom/Center/Stretch），分数 0..1
        // 出参：父空间的 [min, max] 子区间
        //   left/bottom  → [0, f]
        //   right/top    → [1-f, 1]
        //   center       → [(1-f)/2, (1+f)/2]
        //   stretch      → 由 ValidateAgainst 提前拒掉（HasX + StretchX 冲突），这里走不到
        private static void ComputeFractionalAnchor(AnchorHorizontal h, float fraction,
            out float min, out float max)
        {
            switch (h)
            {
                case AnchorHorizontal.Left:
                    min = 0f;
                    max = fraction;
                    break;
                case AnchorHorizontal.Right:
                    min = 1f - fraction;
                    max = 1f;
                    break;
                case AnchorHorizontal.Center:
                    min = (1f - fraction) * 0.5f;
                    max = (1f + fraction) * 0.5f;
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(h), "fractional + stretch on same axis is rejected by ValidateAgainst");
            }
        }

        private static void ComputeFractionalAnchor(AnchorVertical v, float fraction,
            out float min, out float max)
        {
            switch (v)
            {
                case AnchorVertical.Bottom:
                    min = 0f;
                    max = fraction;
                    break;
                case AnchorVertical.Top:
                    min = 1f - fraction;
                    max = 1f;
                    break;
                case AnchorVertical.Center:
                    min = (1f - fraction) * 0.5f;
                    max = (1f + fraction) * 0.5f;
                    break;
                default:
                    throw new System.ArgumentOutOfRangeException(
                        nameof(v), "fractional + stretch on same axis is rejected by ValidateAgainst");
            }
        }

        /// <summary>把 R3 订阅绑到本 Control 生命周期（卡片重建 / 关窗时随 Dispose 释放）。对称 Screen.Track。</summary>
        public void Track(System.IDisposable d)
            => (_subscriptions ??= new System.Collections.Generic.List<System.IDisposable>()).Add(d);

        // 释放自身订阅袋 + 递归子树兜底：动态卡子树的内层 Control 不会被单独 Dispose（只销毁根 GO 级联），
        // 故 .AddTo(innerControl) 必须靠这条递归，否则泄漏。只碰订阅袋，不额外销毁 GO（GO 由根 Destroy 级联）。
        private void DisposeSubscriptionsRecursive()
        {
            if (_subscriptions != null)
            {
                for (int i = _subscriptions.Count - 1; i >= 0; i--) _subscriptions[i]?.Dispose();
                _subscriptions.Clear();
                _subscriptions = null;
            }
            foreach (var c in _children)
                if (c is Control cc) cc.DisposeSubscriptionsRecursive();
        }

        public virtual void Dispose()
        {
            // 先退订（自身 + 子树）——teardown 可能读 GO；避免往半销毁 GO fire。
            DisposeSubscriptionsRecursive();
            if (HostGameObject == null) return;
            // 与 Screen.Close 一致：EditMode 用 DestroyImmediate。
            if (UnityEngine.Application.isPlaying) Object.Destroy(HostGameObject);
            else Object.DestroyImmediate(HostGameObject);
        }
    }
}
