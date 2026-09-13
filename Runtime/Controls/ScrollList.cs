using System;
using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class ScrollList : ProceduralControl, IHugContent, IScrollbarHost
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
        // The one bar (spec 2026-09-12 §5.4): an authored <Scrollbar> child adopted at instantiation,
        // else a default one this control builds in its first OnAfterApply. Re-oriented in place
        // when direction flips — there is no second, lazily built bar any more.
        private Scrollbar _bar;
        private bool _ownsBar;
        private string _itemTemplate;
        // spacing 两轴分开存：单列只用得上 V、单行只用得上 H，网格两个都用。padding 同理存解析后的
        // 四段而不是原串 —— 换组之后新组件的 padding 是全零，必须能原样重放。
        private float _spacingV;
        private float _spacingH;
        private int _padT, _padR, _padB, _padL;
        private int _columns;              // 0 = 不用网格，走 direction 的单列 / 单行
        private Vector2? _cellSize;
        private Func<RectTransform, IControl> _factory;
        private readonly List<IControl> _slots = new();
        private bool _staticCollected;
        private bool _bound;

        // DSS-D4: ScrollList 视口默认值（避免 0x0 不可见）；实际项目几乎都会显式写 size。
        private const float DefaultMainAxisLength = 200f;
        private const float DefaultCrossAxisLength = 160f;

        public override Vector2? GetNativeSize()
        {
            // Grid mode: the cells are authoritative across, so the default viewport is exactly wide
            // enough to hold the columns it was asked for — a flat 160 would clip the 4th of four
            // 66-wide columns before the author ever saw the list. The main axis stays the plain
            // default: how many ROWS are visible is a viewport choice, not a content one.
            // Note the scrollbar is not counted in: unless <Scrollbar overlay="true"> it still takes
            // (thickness + spacing) out of the viewport once the content overflows.
            if (IsGrid && _cellSize.HasValue)
            {
                var w = _padL + _padR
                        + _columns * _cellSize.Value.x
                        + Mathf.Max(0, _columns - 1) * _spacingH;
                return new Vector2(w, DefaultMainAxisLength);
            }
            return _direction == "horizontal"
                ? new Vector2(DefaultMainAxisLength, DefaultCrossAxisLength)
                : new Vector2(DefaultCrossAxisLength, DefaultMainAxisLength);
        }

        public int SlotCount => _slots.Count;

        // 静态 XML 子卡与 BindItems 建的卡都进 Content（同 Carousel 的 _strip）：挂在 ScrollList
        // 根上的子节点落在 Viewport 之外 —— 既不被裁剪、也不滚动、也不计入 Content 尺寸。
        protected internal override Transform ChildHostTransform => _content;

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

            ApplyLayoutMode();
        }

        /// <summary>
        /// True when this list lays its items out as a grid: <c>columns</c> ≥ 1 and the direction is
        /// not horizontal. A row-major grid (<c>rows=</c>) is not in v1, so <c>direction="horizontal"</c>
        /// wins over <c>columns</c> here — the combination is a lint error
        /// (<c>PUI-SCROLL-COLUMNS-DIRECTION</c>) rather than a second layout to invent.
        /// </summary>
        internal bool IsGrid => _columns >= 1 && _direction != "horizontal";

        /// <summary>
        /// Configure the Content layout group from this node's own declaration BEFORE its children
        /// are instantiated into it. The apply pass is DFS post-order, so a child resolves its
        /// geometry against whatever group Content carries at instantiation time: without this, a
        /// grid list's children would measure against the boot <c>VerticalLayoutGroup</c> on the
        /// first pass and against the <c>GridLayoutGroup</c> on every <c>ReSolve</c> after, and a
        /// <c>&lt;Text scale=&gt;</c> cell would get the scale-host wrapper that <c>&lt;Grid&gt;</c>
        /// is excluded from. Idempotent — the ordinary setters still run in the apply pass.
        /// </summary>
        internal void PreConfigureContent(string direction, string columns)
        {
            _direction = string.IsNullOrEmpty(direction) ? "vertical" : direction;
            if (int.TryParse(columns, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                _columns = Math.Max(0, n);
            ApplyLayoutMode();
        }

        private void ApplyLayoutMode()
        {
            var wantGrid = IsGrid;
            var wantHorizontal = !wantGrid && _direction == "horizontal";
            var wantType = wantGrid ? typeof(GridLayoutGroup)
                         : wantHorizontal ? typeof(HorizontalLayoutGroup)
                         : typeof(VerticalLayoutGroup);

            // Ensure Content carries the right LayoutGroup type, reusing it when unchanged. Object.Destroy is
            // deferred to end-of-frame in play mode, so a destroy-then-AddComponent in one frame collides with
            // the not-yet-removed group (LayoutGroup is [DisallowMultipleComponent]) — the add fails and the
            // deferred destroy then strands Content with no layout group (items collapse). Only swap on a real
            // type change, and DestroyImmediate so the slot is free before AddComponent (safe here: off the
            // app/ReSolve call stack, never a physics/animation/OnValidate callback). Mirrors TabBar.ApplyDirection.
            if (_layoutGroup == null || _layoutGroup.GetType() != wantType)
            {
                if (_layoutGroup != null)
                {
                    UnityEngine.Object.DestroyImmediate(_layoutGroup);
                    _layoutGroup = null;
                }
                _layoutGroup = wantGrid
                    ? (LayoutGroup)_content.gameObject.AddComponent<GridLayoutGroup>()
                    : wantHorizontal
                        ? _content.gameObject.AddComponent<HorizontalLayoutGroup>()
                        : _content.gameObject.AddComponent<VerticalLayoutGroup>();
            }

            if (_layoutGroup is GridLayoutGroup grid)
            {
                // Column-major wrap from the top-left: rows grow downwards, which is the only
                // direction this mode scrolls.
                grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
                grid.constraintCount = _columns;
                grid.startCorner = GridLayoutGroup.Corner.UpperLeft;
                grid.startAxis = GridLayoutGroup.Axis.Horizontal;
                grid.childAlignment = TextAnchor.UpperLeft;
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
                // 顶部锚点：水平方向铺满 viewport，竖向由 ContentSizeFitter 撑开（网格模式共用这一支）
                _content.anchorMin = new Vector2(0f, 1f);
                _content.anchorMax = new Vector2(1f, 1f);
                _content.pivot = new Vector2(0.5f, 1f);
                _content.sizeDelta = Vector2.zero;
                _content.anchoredPosition = Vector2.zero;
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            ApplyGroupMetrics();

            // The bar follows the axis: not built here (an authored one may still be on its way —
            // children instantiate after PreConfigureContent), just pointed the right way if present.
            WireScrollbar();
        }

        // ───── the scrollbar (IScrollbarHost) ─────

        RectTransform IScrollbarHost.ScrollbarHost => RectTransform;

        void IScrollbarHost.AdoptScrollbar(Scrollbar bar)
        {
            if (_bar != null && _bar != bar)
            {
                Debug.LogWarning(
                    $"[{PromptUGUI.Lint.ScrollbarRules.DuplicateCode}] <ScrollList id='{Id}'>: a second " +
                    $"<Scrollbar> (id='{bar.Id}') — a host takes one bar; the first in document order is " +
                    "used and this one is parked inactive.");
                bar.GameObject.SetActive(false);
                return;
            }
            _bar = bar;
            _ownsBar = false;
            bar.AttachHost(this);
            WireScrollbar();
        }

        void IScrollbarHost.OnScrollbarChanged(Scrollbar bar)
        {
            if (bar == _bar) ApplyScrollbarPolicy();
        }

        /// <summary>No <c>&lt;Scrollbar&gt;</c> was authored: build the stock one. Once.</summary>
        private void EnsureDefaultScrollbar()
        {
            if (_bar != null) return;
            var go = new GameObject(Scrollbar.NodeName, typeof(RectTransform));
            go.transform.SetParent(RectTransform, worldPositionStays: false);
            var bar = new Scrollbar();
            bar.AttachTo(go);
            _bar = bar;
            _ownsBar = true;
            bar.AttachHost(this);
            WireScrollbar();
        }

        /// <summary>
        /// Points the bar along the scrolling axis and hands it to the ScrollRect on that axis (the
        /// other axis is cleared — one bar, one axis). Idempotent; runs on every layout-mode change.
        /// </summary>
        private void WireScrollbar()
        {
            if (_bar == null || _scroll == null) return;
            var vertical = _direction != "horizontal";
            _bar.Orient(vertical);
            var bar = _bar.Bar;
            if (vertical)
            {
                if (_scroll.horizontalScrollbar == bar) _scroll.horizontalScrollbar = null;
                if (_scroll.verticalScrollbar != bar) _scroll.verticalScrollbar = bar;
            }
            else
            {
                if (_scroll.verticalScrollbar == bar) _scroll.verticalScrollbar = null;
                if (_scroll.horizontalScrollbar != bar) _scroll.horizontalScrollbar = bar;
            }
            ApplyScrollbarPolicy();
        }

        /// <summary>What lives on the ScrollRect side of the bar: overlay → visibility, spacing.</summary>
        private void ApplyScrollbarPolicy()
        {
            if (_bar == null || _scroll == null) return;
            var visibility = _bar.IsOverlay
                ? ScrollRect.ScrollbarVisibility.AutoHide
                : ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            _scroll.verticalScrollbarVisibility = visibility;
            _scroll.horizontalScrollbarVisibility = visibility;
            _scroll.verticalScrollbarSpacing = _bar.ResolvedSpacing;
            _scroll.horizontalScrollbarSpacing = _bar.ResolvedSpacing;
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
                case GridLayoutGroup g:
                    g.spacing = new Vector2(_spacingH, _spacingV);
                    if (_cellSize.HasValue) g.cellSize = _cellSize.Value;
                    break;
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
            set { _direction = string.IsNullOrEmpty(value) ? "vertical" : value; ApplyLayoutMode(); }
        }

        /// <summary>
        /// Column count. <c>0</c> (the default) lays items out as the single column / row that
        /// <c>direction</c> describes; anything ≥ 1 switches Content to a <c>GridLayoutGroup</c> and
        /// makes <c>cellSize</c> required.
        /// <para>Spell <c>columns="0"</c> out when a variant has to LEAVE the grid — a variant that
        /// resolves to null is skipped rather than reverted (<c>ControlAttributeApplier</c> does
        /// <c>if (v == null) continue;</c>), so simply omitting the override would keep the grid.</para>
        /// </summary>
        [UIAttr, Preserve]
        public int Columns
        {
            set { _columns = Mathf.Max(0, value); ApplyLayoutMode(); }
        }

        /// <summary>
        /// Uniform cell size <c>"WxH"</c> for grid mode; the children's own size is ignored there
        /// (<c>PUI-GRID-CHILD-SIZE</c>). Required whenever <c>columns</c> is set
        /// (<c>PUI-SCROLL-COLUMNS-CELLSIZE</c>) — without it every cell falls back to uGUI's 100×100.
        /// </summary>
        [UIAttr, Preserve]
        public string CellSize
        {
            set
            {
                var parsed = ParseCellSize(value);
                if (parsed.HasValue) _cellSize = parsed;
                ApplyGroupMetrics();
            }
        }

        // 格式错时告警并保留旧值（同 Carousel.DotSize）：一个笔误不该把整个 Screen 打不开。
        private static Vector2? ParseCellSize(string value)
        {
            var x = value == null ? -1 : value.IndexOf('x');
            if (x > 0
                && float.TryParse(value.Substring(0, x), NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                && float.TryParse(value.Substring(x + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                return new Vector2(w, h);
            if (!string.IsNullOrEmpty(value))
                Debug.LogWarning($"<ScrollList cellSize='{value}'> is not 'WxH'; keeping the previous cell size.");
            return null;
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
            // 首次 apply 把静态 XML 子卡收进 _slots —— apply 是 DFS 后序，到这里子节点已全部建好。
            // 只跑一次（_staticCollected），且 BindItems 调过之后（_bound）不再收：否则 ReSolve 会
            // 把已 Dispose 的旧引用收回来。同 CarouselView.SetStaticCards。
            if (!_staticCollected && !_bound)
            {
                _staticCollected = true;
                // The bar is chrome, not a slot — it lives beside the Viewport, never in Content.
                foreach (var c in Children)
                    if (c is not Scrollbar) _slots.Add(c);
            }
            // mask 未显式写时跟随 bg sprite：有图→圆角 stencil，sprite=""→直角 RectMask2D
            // （对齐 InputField 的 mask-tracks-border 先例；显式 mask= 一旦写过即 latch，跳过这里）。
            if (!_maskExplicit)
                ProceduralBuilders.ApplyViewportMask(
                    _viewport, _bg != null && _bg.sprite != null ? null : "",
                    ProceduralBuilders.SpriteMaskRoundedRect);
            // No authored <Scrollbar> arrived with the children — build the stock one now (children
            // instantiate before this apply, so by here the answer is final).
            EnsureDefaultScrollbar();
            // The default bar may have just been appended after the frame —— 每轮 apply 后把 frame 钉回最顶。
            if (_frame != null) _frame.transform.SetAsLastSibling();
        }

        private Func<RectTransform, IControl> ResolveFactory(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            // Template first, then a registered Control — one resolver shared with Carousel / TabBar /
            // Screen.Instantiate; only the not-found exception is ours (it is an attribute value error).
            var owner = PromptUGUI.Application.UI.OwnerScreenOf(this);
            if (PromptUGUI.Application.TemplateFactoryResolver.TryResolve(
                    owner, tag, $"itemTemplate='{tag}'", out var factory))
                return factory;
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
            // 标记已动态绑定：之后 ReSolve 的静态收集不再执行。静态卡在这里被 Dispose，
            // Screen.ReSolve 靠 `control.GameObject == null` 跳过它们的 ElementNode。
            _bound = true;
            foreach (var s in _slots)
            {
                s.Dispose();
            }
            _slots.Clear();
        }

        public override void Dispose()
        {
            ClearSlots();
            // An adopted bar is a Screen-owned node and is disposed with the rest of _nodeMap; the
            // default one is ours.
            if (_ownsBar) _bar?.Dispose();
            base.Dispose();
        }
    }
}
