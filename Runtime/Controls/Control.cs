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

        public bool Hidden
        {
            get => !GameObject.activeSelf;
            set => GameObject.SetActive(!value);
        }

        public bool Interactable
        {
            get => CanvasGroup.interactable;
            set { CanvasGroup.interactable = value; CanvasGroup.blocksRaycasts = value; }
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

        // DSS-D14: 作者省略 anchor= 时的默认 preset。基类返回 top-left（沿用既有行为）；
        // 容器类（Frame）覆写按 sizeSpec.HasWidth/HasHeight 决定每轴 stretch 还是 top/left。
        protected virtual AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
            => new(AnchorVertical.Top, AnchorHorizontal.Left);

        // 通用属性应用（由 ScreenInstantiator 在子类自身属性应用之后调用）
        public void ApplyCommon(string anchor, string size, string width, string height,
                                string margin, string pivot,
                                bool? hidden, bool interactable)
        {
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
            var parentLg = RectTransform.parent != null
                ? RectTransform.parent.GetComponent<UnityEngine.UI.LayoutGroup>()
                : null;
            var parentIsGrid = parentLg is UnityEngine.UI.GridLayoutGroup;
            var parentIsAutoLayout = parentLg != null && !parentIsGrid;

            // 百分比 ('%') 在任何 LayoutGroup 容器（V/HStack 或 Grid）里都无法表达：
            // - V/HStack 用的是 flex 权重，不是父尺寸百分比；
            // - Grid 用的是 cellSize，子节点的 anchor 直接被 GridLayoutGroup 覆写。
            // 给出可操作的提示：用加权 stretch + spacer 兄弟，或者移到自由定位父级。
            if ((parentIsAutoLayout || parentIsGrid)
                && (sizeSpec.IsFractionalWidth || sizeSpec.IsFractionalHeight))
            {
                throw new System.ArgumentException(
                    "'%' (fractional) width/height cannot be used inside <VStack>/<HStack>/<Grid> — " +
                    "Unity LayoutGroup distributes by flex weight (or fixed cellSize for Grid), not parent percentage. " +
                    "Use a weighted stretch + spacer pattern instead: " +
                    "<Frame width=\"stretch\"/> <Btn width=\"stretch*2\"/> <Frame width=\"stretch\"/> " +
                    "gives a 25/50/25 split. " +
                    "Or move the child to a free-positioning parent (Frame/Screen) where '%' maps to anchor fractions.");
            }

            if (parentIsAutoLayout)
            {
                ApplyLayoutElement(sizeSpec, preset);
                // anchor / pivot / sizeDelta / anchoredPosition: LayoutGroup 接管几何。
                // 作者写 anchor/margin 已经被 ScreenInstantiator 警告（spec §6.5）；这里静默跳过。
            }
            else
            {
                // 'stretch' 关键字只在 V/HStack 子节点上有意义（映射到 LayoutElement.flexibleX=1）。
                // 自由布局父级（Frame/Screen/Grid）下没有 flex weight 概念，作者真要拉伸应改用 anchor="stretch"。
                // 静默忽略会让作者以为生效了，所以显式抛错。
                if (sizeSpec.IsFlexibleWidth || sizeSpec.IsFlexibleHeight)
                    throw new System.ArgumentException(
                        "'stretch' on width/height is only valid inside <VStack>/<HStack>; " +
                        "use anchor=\"stretch\" (or anchor=\"X-stretch\") + margin for free-positioning containers");

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
            var parentHv = RectTransform.parent != null
                ? RectTransform.parent.GetComponent<UnityEngine.UI.HorizontalOrVerticalLayoutGroup>()
                : null;
            var fillCrossX = parentHv is UnityEngine.UI.VerticalLayoutGroup && preset.StretchX && !sizeSpec.HasWidth;
            var fillCrossY = parentHv is UnityEngine.UI.HorizontalLayoutGroup && preset.StretchY && !sizeSpec.HasHeight;

            // 决策 LGC-D8 + BCS-D6 + BCS-D7 partial-write:
            // 任一轴没写 → 询问 GetNativeSize 作为该轴 fallback；写了的轴保留作者值。
            // 决策 LGC-D9: 没 native 时该轴留在 -1 哨兵值，让 Image/TMP 自带 ILayoutElement 主导。
            // 决策 LGC-D10: 每次都先把两轴全置 -1，清掉前一次 Variant 的残留约束。
            var needNativeFallback = !sizeSpec.HasWidth || !sizeSpec.HasHeight;
            var native = needNativeFallback ? GetNativeSize() : null;
            var hasNative = native.HasValue;

            // 是否需要 LE：作者写了 size、或 native fallback 命中、或需要 cross 轴 fill。
            // 都不需要时若有残留 LE（Variant 切换可能挂过）→ 清回 -1。
            var needLE = sizeSpec.HasWidth || sizeSpec.HasHeight || hasNative || fillCrossX || fillCrossY;

            var le = GameObject.GetComponent<UnityEngine.UI.LayoutElement>();
            if (!needLE)
            {
                if (le != null)
                {
                    le.preferredWidth = -1;
                    le.preferredHeight = -1;
                    le.flexibleWidth = -1;
                    le.flexibleHeight = -1;
                }
                return;
            }

            le ??= GameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredWidth = -1;
            le.preferredHeight = -1;
            le.flexibleWidth = -1;
            le.flexibleHeight = -1;

            if (sizeSpec.HasWidth)
            {
                if (sizeSpec.IsFlexibleWidth)
                {
                    // stretch / stretch*N: 让 LayoutGroup 把剩余空间按权重分给该子节点（VerticalLayoutGroup 跨轴
                    // 在 flexible>0 时把 requiredSpace 抬到容器内宽，HorizontalLayoutGroup 主轴则按
                    // flexible 权重分配剩余空间）。preferred=0 让 base 部分不抢权重。
                    le.preferredWidth = 0;
                    le.flexibleWidth = sizeSpec.WeightWidth;
                }
                else
                {
                    le.preferredWidth = sizeSpec.Width;
                    le.flexibleWidth = 0;
                }
            }
            else if (hasNative)
            {
                le.preferredWidth = native.Value.x;
                // flexibleWidth 保持 -1（"无意见"），与历史 both-missing native 路径一致
            }

            if (sizeSpec.HasHeight)
            {
                if (sizeSpec.IsFlexibleHeight)
                {
                    le.preferredHeight = 0;
                    le.flexibleHeight = sizeSpec.WeightHeight;
                }
                else
                {
                    le.preferredHeight = sizeSpec.Height;
                    le.flexibleHeight = 0;
                }
            }
            else if (hasNative)
            {
                le.preferredHeight = native.Value.y;
            }

            // DSS-D16: cross 轴 fill — 必须在 HasWidth/HasHeight 与 native 分支之后，
            // 因为它要把 preferred/-1 覆写成 0+flex=1。
            if (fillCrossX) { le.preferredWidth = 0; le.flexibleWidth = 1; }
            if (fillCrossY) { le.preferredHeight = 0; le.flexibleHeight = 1; }
        }

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

        public virtual void Dispose()
        {
            if (GameObject == null) return;
            // 与 Screen.Close 一致：EditMode 下用 DestroyImmediate，避免 "Destroy may not be called" 警告。
            if (UnityEngine.Application.isPlaying) Object.Destroy(GameObject);
            else Object.DestroyImmediate(GameObject);
        }
    }
}
