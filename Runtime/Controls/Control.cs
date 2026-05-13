using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using UnityEngine;

namespace PromptUGUI.Controls
{
    public abstract class Control : IControl
    {
        public string Id { get; internal set; }
        public GameObject GameObject { get; private set; }
        public RectTransform RectTransform { get; private set; }
        private CanvasGroup _canvasGroup;

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
        /// 在 <see cref="ControlAttributeApplier"/> 调用 <see cref="ApplyCommon"/> 之后再触发一次，
        /// 让一些控件在 Variant ReSolve / 初始 Apply 完成后做"恢复其它逻辑写入的 RectTransform / 组件状态"
        /// 这类收尾。默认实现为空；目前只有 SafeArea 重写。
        /// </summary>
        internal virtual void OnAfterApply() { }

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

        // 通用属性应用（由 ScreenInstantiator 在子类自身属性应用之后调用）
        public void ApplyCommon(string anchor, string size, string width, string height,
                                string margin, string pivot,
                                bool hidden, bool interactable)
        {
            var preset = string.IsNullOrEmpty(anchor)
                ? new AnchorPreset(AnchorVertical.Top, AnchorHorizontal.Left)
                : AnchorPreset.Parse(anchor);

            var sizeSpec = SizeSpec.Parse(size, width, height);

            if (sizeSpec.IsNativeWidth || sizeSpec.IsNativeHeight)
            {
                var native = GetNativeSize();
                if (native.HasValue)
                    sizeSpec = sizeSpec.WithNativeResolved(native.Value);
            }

            sizeSpec.ValidateAgainst(preset);

            // spec §6.5: 父级是 VStack/HStack 时走 LayoutElement 通道；
            // GridLayoutGroup 例外（它直接用 cellSize，LayoutElement 在它下面被忽略）。
            var parentLg = RectTransform.parent != null
                ? RectTransform.parent.GetComponent<UnityEngine.UI.LayoutGroup>()
                : null;
            var parentIsAutoLayout = parentLg != null
                && !(parentLg is UnityEngine.UI.GridLayoutGroup);

            if (parentIsAutoLayout)
            {
                ApplyLayoutElement(sizeSpec);
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

                AnchorResolver.Resolve(preset,
                    out var aMin, out var aMax, out var p);
                RectTransform.anchorMin = aMin;
                RectTransform.anchorMax = aMax;

                if (!string.IsNullOrEmpty(pivot))
                {
                    var parts = pivot.Split(',');
                    RectTransform.pivot = new Vector2(
                        float.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
                        float.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    RectTransform.pivot = p;
                }

                var lr = MarginResolver.Resolve(preset, sizeSpec, margin);
                RectTransform.anchoredPosition = lr.AnchoredPosition;
                RectTransform.sizeDelta = lr.SizeDelta;
            }

            Hidden = hidden;
            Interactable = interactable;
        }

        private void ApplyLayoutElement(SizeSpec sizeSpec)
        {
            // 决策 LGC-D8: 作者没写任何 size 属性 → 不挂 LayoutElement（让 Image/TMP 自带 ILayoutElement 主导）
            // 决策 LGC-D9: 按轴路由 — 未写的轴留在 -1 哨兵值
            // 决策 LGC-D10: 每次都先把两轴全置 -1，清掉前一次 Variant 的残留约束
            if (!sizeSpec.HasWidth && !sizeSpec.HasHeight)
            {
                // 前一次 Variant 可能挂过 LayoutElement，本次没尺寸 → 还原成"无约束"
                var existing = GameObject.GetComponent<UnityEngine.UI.LayoutElement>();
                if (existing != null)
                {
                    existing.preferredWidth = -1;
                    existing.preferredHeight = -1;
                    existing.flexibleWidth = -1;
                    existing.flexibleHeight = -1;
                }
                return;
            }
            var le = GameObject.GetComponent<UnityEngine.UI.LayoutElement>()
                     ?? GameObject.AddComponent<UnityEngine.UI.LayoutElement>();
            le.preferredWidth = -1;
            le.preferredHeight = -1;
            le.flexibleWidth = -1;
            le.flexibleHeight = -1;
            if (sizeSpec.HasWidth)
            {
                if (sizeSpec.IsFlexibleWidth)
                {
                    // stretch: 让 LayoutGroup 把剩余空间全分给该子节点（VerticalLayoutGroup 跨轴
                    // 在 flexible>0 时把 requiredSpace 抬到容器内宽，HorizontalLayoutGroup 主轴则按
                    // flexible 权重分配剩余空间）。preferred=0 让 base 部分不抢权重。
                    le.preferredWidth = 0;
                    le.flexibleWidth = 1;
                }
                else
                {
                    le.preferredWidth = sizeSpec.Width;
                    le.flexibleWidth = 0;
                }
            }
            if (sizeSpec.HasHeight)
            {
                if (sizeSpec.IsFlexibleHeight)
                {
                    le.preferredHeight = 0;
                    le.flexibleHeight = 1;
                }
                else
                {
                    le.preferredHeight = sizeSpec.Height;
                    le.flexibleHeight = 0;
                }
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
