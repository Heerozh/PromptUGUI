using System;
using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class ScrollList : ProceduralControl, IHugContent
    {
        private UnityImage _bg;

        // height="hug" on a list means "as tall as the rows", not "as tall as the viewport I already
        // am" — so the content node answers, not this rect. SelfReportsContentSize stays false: the
        // ScrollList root carries no ILayoutElement, so inside a stack a HugElement has to publish it
        // (FND §1.4.3).
        float IHugContent.ContentSize(int axis)
            => _content != null ? LayoutUtility.GetPreferredSize(_content, axis) : 0f;

        private protected override GameObject SurfaceHost => GameObject;
        private UnityImage _frame;
        private bool _maskExplicit;
        private ScrollRect _scroll;
        private RectTransform _viewport;
        private RectTransform _content;
        private LayoutGroup _layoutGroup;
        private string _direction = "vertical";
        private Scrollbar _vertScrollbar;
        private Scrollbar _horizScrollbar;
        // 滚动条由 Direction setter 懒建（且 direction 切换会启用另一根），所以皮肤属性
        // 存原始字符串、建完再回放 —— 同 spacing / padding 的 pending 模式。
        private string _scrollbarSprite;
        private string _scrollbarColor;
        private string _scrollbarHandleSprite;
        private string _scrollbarHandleColor;
        private string _itemTemplate;
        // spacing 两轴分开存：单列只用得上 V、单行只用得上 H，网格两个都用。padding 同理存解析后的
        // 四段而不是原串 —— 换组之后新组件的 padding 是全零，必须能原样重放。
        private float _spacingV;
        private float _spacingH;
        private int _padT, _padR, _padB, _padL;
        private Func<RectTransform, IControl> _factory;
        private readonly List<IControl> _slots = new();

        // DSS-D4: ScrollList 视口默认值（避免 0x0 不可见）；实际项目几乎都会显式写 size。
        private const float DefaultMainAxisLength = 200f;
        private const float DefaultCrossAxisLength = 160f;

        public override Vector2? GetNativeSize()
            => _direction == "horizontal"
                ? new Vector2(DefaultMainAxisLength, DefaultCrossAxisLength)
                : new Vector2(DefaultCrossAxisLength, DefaultMainAxisLength);

        public int SlotCount => _slots.Count;

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultContainerColor;
            ProceduralBuilders.ApplyDefaultInsetSprite(_bg);
            _scroll = GameObject.GetComponent<ScrollRect>() ?? GameObject.AddComponent<ScrollRect>();

            _viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
            _viewport.pivot = new Vector2(0f, 1f);
            // Viewport mask 三态 + 默认 pugui_9slice_mask 圆角，见 ApplyViewportMask 注释 / spec §2.3。
            ProceduralBuilders.ApplyViewportMask(_viewport, null, ProceduralBuilders.SpriteMaskRoundedRect);
            _scroll.viewport = _viewport;

            _content = ProceduralBuilders.AddChild(_viewport, "Content");
            _scroll.content = _content;

            _scroll.movementType = ScrollRect.MovementType.Elastic;
            _scroll.elasticity = 0.1f;
            _scroll.inertia = true;
            _scroll.decelerationRate = 0.135f;
            _scroll.scrollSensitivity = 1f;

            ApplyDirection();
        }

        private void ApplyDirection()
        {
            var wantHorizontal = _direction == "horizontal";

            // Ensure Content carries the right LayoutGroup type, reusing it when unchanged. Object.Destroy is
            // deferred to end-of-frame in play mode, so a destroy-then-AddComponent in one frame collides with
            // the not-yet-removed group (LayoutGroup is [DisallowMultipleComponent]) — the add fails and the
            // deferred destroy then strands Content with no layout group (items collapse). Only swap on a real
            // type change, and DestroyImmediate so the slot is free before AddComponent (safe here: off the
            // app/ReSolve call stack, never a physics/animation/OnValidate callback). Mirrors TabBar.ApplyDirection.
            if (_layoutGroup == null || (_layoutGroup is HorizontalLayoutGroup) != wantHorizontal)
            {
                if (_layoutGroup != null)
                {
                    UnityEngine.Object.DestroyImmediate(_layoutGroup);
                    _layoutGroup = null;
                }
                _layoutGroup = wantHorizontal
                    ? (LayoutGroup)_content.gameObject.AddComponent<HorizontalLayoutGroup>()
                    : _content.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            var fitter = _content.GetComponent<ContentSizeFitter>()
                         ?? _content.gameObject.AddComponent<ContentSizeFitter>();

            if (wantHorizontal)
            {
                _scroll.horizontal = true;
                _scroll.vertical = false;
                // 左侧锚点：竖向铺满 viewport，水平方向由 ContentSizeFitter 撑开
                _content.anchorMin = new Vector2(0f, 0f);
                _content.anchorMax = new Vector2(0f, 1f);
                _content.pivot = new Vector2(0f, 0.5f);
                _content.sizeDelta = Vector2.zero;
                _content.anchoredPosition = Vector2.zero;
                fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
                fitter.verticalFit = ContentSizeFitter.FitMode.Unconstrained;
            }
            else
            {
                _scroll.horizontal = false;
                _scroll.vertical = true;
                // 顶部锚点：水平方向铺满 viewport，竖向由 ContentSizeFitter 撑开
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = new Vector2(1f, 1f);
                _content.pivot = new Vector2(0.5f, 1f);
                _content.sizeDelta = Vector2.zero;
                _content.anchoredPosition = Vector2.zero;
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            ApplyGroupMetrics();

            if (wantHorizontal)
            {
                EnsureHorizontalScrollbar();
                if (_vertScrollbar != null) _vertScrollbar.gameObject.SetActive(false);
            }
            else
            {
                EnsureVerticalScrollbar();
                if (_horizScrollbar != null) _horizScrollbar.gameObject.SetActive(false);
            }
        }

        // 每次换组之后都要重放一遍：ControlAttributeApplier 遍历的是 HashSet，属性到达顺序不可依赖，
        // 所以 setter 只存值、这里统一下发到当前的 LayoutGroup 实例。LayoutGroup 的 setter 走
        // SetProperty（值相等就不 dirty），因此 ReSolve 里的空转重放不会引发多余的 layout rebuild。
        private void ApplyGroupMetrics()
        {
            if (_layoutGroup == null) return;
            switch (_layoutGroup)
            {
                // GridLayoutGroup.spacing 是 Vector2 (x = horizontal, y = vertical)
                case GridLayoutGroup g: g.spacing = new Vector2(_spacingH, _spacingV); break;
                case HorizontalLayoutGroup h: h.spacing = _spacingH; break;
                case VerticalLayoutGroup v: v.spacing = _spacingV; break;
            }
            _layoutGroup.padding = new RectOffset(_padL, _padR, _padT, _padB);
        }

        // "X" | "V,H" —— 与 <Grid spacing> 和两段 padding 同序（竖向在前）。
        private static void ParseSpacing(string s, out float v, out float h)
        {
            v = h = 0f;
            if (string.IsNullOrEmpty(s)) return;
            var parts = s.Split(',');
            switch (parts.Length)
            {
                case 1: v = h = ParseSpacingPart(parts[0]); return;
                case 2: v = ParseSpacingPart(parts[0]); h = ParseSpacingPart(parts[1]); return;
                default:
                    throw new ArgumentException(
                    $"spacing '{s}' must be 1 or 2 numbers (a single gap, or \"V,H\")");
            }
        }

        private static float ParseSpacingPart(string p)
        {
            p = p.Trim();
            return (p.Length == 0 || p == "_")
                ? 0f
                : float.Parse(p, NumberStyles.Float, CultureInfo.InvariantCulture);
        }

        [UIAttr, Preserve]
        public string ItemTemplate
        {
            set
            {
                _itemTemplate = value;
                _factory = ResolveFactory(value);
            }
        }

        [UIAttr, Preserve]
        public string Direction
        {
            set { _direction = string.IsNullOrEmpty(value) ? "vertical" : value; ApplyDirection(); }
        }

        /// <summary>
        /// Gap between items. One value = both axes; <c>"V,H"</c> = vertical, horizontal — the same
        /// written order as <c>&lt;Grid spacing&gt;</c> and the two-part <c>padding</c>. A single
        /// column only ever uses V and a single row only H; grid mode uses both.
        /// </summary>
        [UIAttr, Preserve]
        public string Spacing
        {
            set { ParseSpacing(value, out _spacingV, out _spacingH); ApplyGroupMetrics(); }
        }

        [UIAttr, Preserve]
        public string Padding
        {
            set
            {
                VStack.ParseTRBL(value, out _padT, out _padR, out _padB, out _padL);
                ApplyGroupMetrics();
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                Internal.ColorApplier.Apply(_bg, spec);
                Surface.SetFill(spec);
            }
        }

        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_bg, value);
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set => _bg.sprite = UI.ResolveSprite(value);
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Mask
        {
            set
            {
                _maskExplicit = true;
                ProceduralBuilders.ApplyViewportMask(
                    _viewport, value, ProceduralBuilders.SpriteMaskRoundedRect);
            }
        }

        private UnityImage EnsureFrame()
        {
            // 边框层：内容/滚动条之上、不被 mask（spec §2.1）。懒创建；层序由 OnAfterApply 钉住。
            _frame ??= ProceduralBuilders.AddImage(RectTransform, "Frame", raycast: false);
            return _frame;
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Frame
        {
            set
            {
                var img = EnsureFrame();
                img.sprite = UI.ResolveSprite(value);
                ProceduralBuilders.AutoSlice(img);
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string FrameColor
        {
            set => Internal.ColorApplier.Apply(EnsureFrame(), UI.Theme.ResolveSpec(value));
        }

        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            // mask 未显式写时跟随 bg sprite：有图→圆角 stencil，sprite=""→直角 RectMask2D
            // （对齐 InputField 的 mask-tracks-border 先例；显式 mask= 一旦写过即 latch，跳过这里）。
            if (!_maskExplicit)
                ProceduralBuilders.ApplyViewportMask(
                    _viewport, _bg != null && _bg.sprite != null ? null : "",
                    ProceduralBuilders.SpriteMaskRoundedRect);
            // Scrollbar 由 Direction setter 懒建，可能晚于 frame 入树 —— 每轮 apply 后把 frame 钉回最顶。
            if (_frame != null) _frame.transform.SetAsLastSibling();
        }

        private Func<RectTransform, IControl> ResolveFactory(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var owner = PromptUGUI.Application.UI.OwnerScreenOf(this);
            // 1) Template
            if (owner?.Def?.Templates != null && owner.Def.Templates.TryGetValue(tag, out var tpl))
            {
                Internal.ItemTemplateGuard.EnsureInstantiable(tag, tpl);
                return parent =>
                {
                    var instantiator = PromptUGUI.Application.UI.GetInstantiator();
                    return instantiator.InstantiateNode(tpl.Body, parent, owner);
                };
            }
            // 2) Control class
            if (PromptUGUI.Application.UI.Registry.Has(tag))
            {
                return parent =>
                {
                    var instantiator = PromptUGUI.Application.UI.GetInstantiator();
                    var node = new ElementNode(tag);
                    return instantiator.InstantiateNode(node, parent, owner);
                };
            }
            throw new ParseException(
                $"<ScrollList itemTemplate='{tag}'>: tag is neither a registered Control nor a Template");
        }

        public IDisposable BindItems<T, TSlot>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind)
            where TSlot : class, IControl =>
            source.Subscribe(items => Rebuild(items, bind));

        public IDisposable BindItems<T>(
            Observable<IReadOnlyList<T>> source,
            Action<IControl, T> bind) =>
            BindItems<T, IControl>(source, bind);

        private void Rebuild<T, TSlot>(IReadOnlyList<T> items, Action<TSlot, T> bind)
            where TSlot : class, IControl
        {
            if (_factory == null)
                throw new InvalidOperationException(
                    "ScrollList.itemTemplate must be set before BindItems is called");

            ClearSlots();
            for (int i = 0; i < items.Count; i++)
            {
                var slot = _factory(_content);
                _slots.Add(slot);
                if (slot is TSlot typed) bind(typed, items[i]);
                else throw new InvalidCastException(
                    $"itemTemplate='{_itemTemplate}' instantiated {slot.GetType().Name}, " +
                    $"but BindItems expected {typeof(TSlot).Name}");
            }
        }

        private void ClearSlots()
        {
            foreach (var s in _slots)
            {
                s.Dispose();
            }
            _slots.Clear();
        }

        private void EnsureVerticalScrollbar()
        {
            if (_vertScrollbar != null) { _vertScrollbar.gameObject.SetActive(true); return; }
            var rt = ProceduralBuilders.AddChild(RectTransform, "Scrollbar Vertical");
            // 注意 anchorMax.y=1：默认 Scroll View prefab 是 (1,0)/(1,0) point 锚，靠 ScrollRect
            // 在 m_HSliderExpand 为 true 时驱动撑开。但 m_HSliderExpand 要求同时存在 horizontal
            // scrollbar；ScrollList 单轴模式下不存在 → 必须自己 anchor 全 Y stretch。
            rt.anchorMin = new Vector2(1f, 0f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(1f, 1f);
            rt.sizeDelta = new Vector2(20f, 0f);
            var bg = rt.gameObject.AddComponent<UnityImage>();
            bg.color = UnityEngine.Color.white;
            ProceduralBuilders.ApplyDefaultInsetSprite(bg);
            _vertScrollbar = rt.gameObject.AddComponent<Scrollbar>();
            _vertScrollbar.direction = Scrollbar.Direction.BottomToTop;

            var sliding = ProceduralBuilders.AddChild(rt, "Sliding Area");
            sliding.sizeDelta = new Vector2(-20f, -20f);
            var handle = ProceduralBuilders.AddImage(sliding, "Handle");
            handle.color = UnityEngine.Color.white;
            ProceduralBuilders.ApplyDefaultSlicedSprite(handle);
            // Vertical Handle: 默认 prefab anchorMax=(1, 0.2) — X 全 stretch (跨 Sliding Area 宽度，
            // 配合 sliding.sizeDelta.x=-20 + handle.sizeDelta.x=20 还原 scrollbar 全宽)；
            // Y 占 sliding 高度的 0%-20% (初始 size=0.2 范围)。
            handle.rectTransform.anchorMin = Vector2.zero;
            handle.rectTransform.anchorMax = new Vector2(1f, 0.2f);
            handle.rectTransform.sizeDelta = new Vector2(20f, 20f);
            _vertScrollbar.targetGraphic = handle;
            _vertScrollbar.handleRect = handle.rectTransform;

            _scroll.verticalScrollbar = _vertScrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            _scroll.verticalScrollbarSpacing = -3f;
            ApplyScrollbarSkin();
        }

        private void EnsureHorizontalScrollbar()
        {
            if (_horizScrollbar != null) { _horizScrollbar.gameObject.SetActive(true); return; }
            var rt = ProceduralBuilders.AddChild(RectTransform, "Scrollbar Horizontal");
            // anchorMax.x=1：单轴 ScrollList 没有 vertical scrollbar，ScrollRect 不会驱动撑开 → 自己 X stretch (镜像 vertical)。
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0f, 0f);
            rt.sizeDelta = new Vector2(0f, 20f);
            var bg = rt.gameObject.AddComponent<UnityImage>();
            bg.color = UnityEngine.Color.white;
            ProceduralBuilders.ApplyDefaultInsetSprite(bg);
            _horizScrollbar = rt.gameObject.AddComponent<Scrollbar>();
            _horizScrollbar.direction = Scrollbar.Direction.LeftToRight;

            var sliding = ProceduralBuilders.AddChild(rt, "Sliding Area");
            sliding.sizeDelta = new Vector2(-20f, -20f);
            var handle = ProceduralBuilders.AddImage(sliding, "Handle");
            handle.color = UnityEngine.Color.white;
            ProceduralBuilders.ApplyDefaultSlicedSprite(handle);
            // Horizontal Handle: 镜像 vertical — anchorMax=(0.2, 1)，Y 全 stretch + X 占 0%-20%。
            handle.rectTransform.anchorMin = Vector2.zero;
            handle.rectTransform.anchorMax = new Vector2(0.2f, 1f);
            handle.rectTransform.sizeDelta = new Vector2(20f, 20f);
            _horizScrollbar.targetGraphic = handle;
            _horizScrollbar.handleRect = handle.rectTransform;

            _scroll.horizontalScrollbar = _horizScrollbar;
            _scroll.horizontalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            _scroll.horizontalScrollbarSpacing = -3f;
            ApplyScrollbarSkin();
        }

        // 内部图层：与 <Progress> 同一套命名规约 —— 每层一对 `<layer>` (sprite) + `<layer>Color`。
        // 两根滚动条共用同一份皮肤：作者关心的是"滚动条长什么样"，不是横竖各一套。

        /// <summary>
        /// 滚动条轨道的 sprite。<c>""</c> = 无图（纯色）。
        /// 属性名不能叫 <c>Scrollbar</c> —— 会在本类内遮蔽 <see cref="UnityEngine.UI.Scrollbar"/>
        /// 类型名，字段和方法签名全部编译不过；XML 属性名由 <c>[UIAttr("scrollbar")]</c> 显式给。
        /// </summary>
        [UIAttr("scrollbar", IsSprite = true), Preserve]
        public string ScrollbarSprite
        {
            set { _scrollbarSprite = value; ApplyScrollbarSkin(); }
        }

        /// <summary>滚动条轨道的颜色；支持 token / <c>/alpha</c> / 渐变。</summary>
        [UIAttr(IsColor = true), Preserve]
        public string ScrollbarColor
        {
            set { _scrollbarColor = value; ApplyScrollbarSkin(); }
        }

        /// <summary>滚动条滑块的 sprite。</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string ScrollbarHandle
        {
            set { _scrollbarHandleSprite = value; ApplyScrollbarSkin(); }
        }

        /// <summary>滚动条滑块的颜色。</summary>
        [UIAttr(IsColor = true), Preserve]
        public string ScrollbarHandleColor
        {
            set { _scrollbarHandleColor = value; ApplyScrollbarSkin(); }
        }

        private void ApplyScrollbarSkin()
        {
            ApplyScrollbarSkin(_vertScrollbar);
            ApplyScrollbarSkin(_horizScrollbar);
        }

        private void ApplyScrollbarSkin(Scrollbar bar)
        {
            if (bar == null) return;

            var track = bar.GetComponent<UnityImage>();
            if (track != null)
            {
                if (_scrollbarSprite != null) track.sprite = UI.ResolveSprite(_scrollbarSprite);
                if (_scrollbarColor != null)
                    Internal.ColorApplier.Apply(track, UI.Theme.ResolveSpec(_scrollbarColor));
            }

            var handle = bar.handleRect != null
                ? bar.handleRect.GetComponent<UnityImage>() : null;
            if (handle != null)
            {
                if (_scrollbarHandleSprite != null) handle.sprite = UI.ResolveSprite(_scrollbarHandleSprite);
                if (_scrollbarHandleColor != null)
                    Internal.ColorApplier.Apply(handle, UI.Theme.ResolveSpec(_scrollbarHandleColor));
            }
        }

        public override void Dispose()
        {
            ClearSlots();
            base.Dispose();
        }
    }
}
