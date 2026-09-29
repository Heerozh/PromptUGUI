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
    public sealed class ScrollList : ProceduralControl, IHugContent, IScrollbarHost, IScrollTickHost
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
        private CrossAxisMaskImage _maskImage;
        private bool _maskExplicit;
        private PuiScrollRect _scroll;
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
        // 建出当前 _slots 的 itemTemplate 名：Rebuild 拿它判断模板换没换。比名字不比 _factory 委托——
        // ItemTemplate 是 [UIAttr]，每次 ReSolve 重放都会 ResolveFactory 出一个新委托，按委托比会次次误判。
        private string _slotsTemplate;
        private bool _staticCollected;
        private bool _bound;

        // BindItems (spec 2026-09-29-scrolllist-virtualization §5.5): the binding that produced the current
        // rows, how many items its last push had, and a reusable newToOld buffer.
        private IItemBinding _binding;
        private int _itemCount;
        private int[] _remap = new int[16];
        private readonly List<IControl> _prevSlots = new();
        // Retired rows wait here until their deferred Destroy. Inactive, and it carries a DISABLED
        // VerticalLayoutGroup: Control.ApplyCommon asks the parent for a LayoutGroup (a disabled one
        // counts), so a ReSolve that replays a row sitting here takes the layout-group branch — on the
        // free-positioning branch a width="stretch" row root would throw.
        private RectTransform _pool;
        private bool _disposed;

        // virtualize= (spec 2026-09-29 §4.1): asked for, and fixed, when the list is built
        // (PreConfigureContent); granted unless the layout cannot be a single vertical column.
        private bool _virtualAsked;
        private bool _virtual;
        private bool _reuseItems = true;
        private HashSet<string> _warnedCodes;

        // Virtual mode (§5). _slots[j] shows item _slotIndex[j]: contiguous after a sync, possibly not after
        // a push to an inactive list until it shows again.
        private readonly VirtualLayoutModel _model = new();
        private readonly List<int> _slotIndex = new();
        private readonly List<IControl> _parked = new();
        // Scratch for one sync / push, kept to avoid allocating per frame.
        private readonly List<IControl> _leaving = new();
        private readonly List<IControl> _nextSlots = new();
        private readonly List<int> _nextIndex = new();
        private readonly List<int> _toBind = new();
        private readonly Dictionary<int, IControl> _keep = new();
        // A sync (or a non-virtual rebuild) is running; a push arriving meanwhile waits in _queuedPush (VIR-P7).
        private bool _inSync;
        private Action _queuedPush;
        // A sync is owed: an inactive list got a push, the heights went stale, the list was enabled.
        private bool _pending;
        private bool _pendingBindAll;
        private Anchor? _pendingAnchor;
        private Vector2 _lastViewport;

        // Sticky edges (spec §5.4, VIR-P5): remembered, not re-derived from the geometry at every sync — a row
        // growing under the viewport, or the scrollbar appearing and the rows rewrapping, moves the geometry
        // without the user having scrolled anywhere. Only the user's own motion re-reads them.
        private bool _stickToEnd;
        private bool _stuckToStart = true;
        private bool _stuckToEnd;
        private float _lastEndScroll;

        // The realized rows' heights may be stale (width change, ReSolve, a row changed from outside): the next
        // sync re-measures even if the window did not move (§5.11).
        private bool _remeasure;
        // The first real sync forces the owning screen's layout once when the list was built this frame — a
        // list in a stack has no size before the canvas lays it out (§5.10).
        private bool _settled;
        private int _bornFrame;
        // A bind that threw during a push: rethrown once the sync is consistent again (§5.6).
        private System.Runtime.ExceptionServices.ExceptionDispatchInfo _bindError;
        private bool _syncIsPush;

        /// <summary>Test hook: how many window syncs have run.</summary>
        internal int SyncCount { get; private set; }

        // Drag-to-reorder (spec 2026-09-16-scrolllist-drag-reorder). The driver and the catcher are
        // built lazily the first time reorder= turns on and stay; the flag alone decides whether a
        // press starts a session. Parameters are plain fields the driver reads at press time —
        // attribute arrival order is not something a setter can depend on.
        private ReorderDriver _reorder;
        private HitCatcher _catcher;
        private bool _reorderOn;
        private float _reorderHold = -1f;          // < 0 = auto (mouse 0 / touch 0.4s)
        private string _reorderHandle;
        private float _reorderDuration = 0.15f;
        private readonly Subject<(int From, int To)> _reordered = new();

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

        /// <summary>Items in the last <see cref="BindItems{T,TSlot}"/> push; 0 before the first one.</summary>
        public int ItemCount => _itemCount;

        /// <summary>The rows in sibling order — static placeholders or <see cref="BindItems{T,TSlot}"/> rows.</summary>
        internal IReadOnlyList<IControl> Slots => _slots;

        internal bool IsHorizontal => !IsGrid && _direction == "horizontal";

        /// <summary>True when this list only realizes the rows near its viewport (<c>virtualize="true"</c> that was granted).</summary>
        internal bool IsVirtual => _virtual;

        // A virtual list's runtime counterpart of the CLI's PUI-SCROLL-VIRTUAL-* errors: said once per code, by
        // the control that had to ignore something (VIR-P1).
        private void WarnOnce(string code, string message)
        {
            if ((_warnedCodes ??= new HashSet<string>()).Add(code))
                UILog.Warn(this, $"[{code}] <ScrollList id='{Id}'>: {message}");
        }

        // ───── drag-to-reorder (spec 2026-09-16) ─────

        /// <summary>
        /// A row was dragged to a new place: <c>(From, To)</c> are slot indices, <c>To</c> being the
        /// insert index AFTER removal (<c>list.RemoveAt(From); list.Insert(To, x)</c>). The rows and
        /// <see cref="Slots"/> are already in the new order when this fires, so a host that applies
        /// the same move to its data and pushes synchronously rebinds with zero visual change; a host
        /// that pushes the unchanged list pulls the rows back — the model is the truth.
        /// </summary>
        public Observable<(int From, int To)> OnReordered => _reordered;

        /// <summary>True from lift to release (the settle tween afterwards does not count).</summary>
        public bool IsReordering => _reorder != null && _reorder.IsSessionActive;

        internal bool ReorderEnabled => _reorderOn;
        internal float ReorderHold => _reorderHold;
        internal string ReorderHandle => _reorderHandle;
        internal float ReorderDuration => _reorderDuration;

        [UIAttr, Preserve]
        public bool Reorder
        {
            set
            {
                if (value && _virtual)
                {
                    WarnOnce(PromptUGUI.Lint.ScrollListRules.VirtualReorderCode,
                        "reorder is not available on a virtual list — its rows only exist near the viewport. " +
                        "Drag-to-reorder stays off.");
                    value = false;
                }
                _reorderOn = value;
                if (value && _reorder == null)
                {
                    _reorder = _content.gameObject.AddComponent<ReorderDriver>();
                    _reorder.Init(this, _content, _viewport);
                    _catcher = _content.gameObject.AddComponent<HitCatcher>();
                }
                if (_catcher != null) _catcher.enabled = value;
                if (!value) _reorder?.Cancel();
            }
        }

        /// <summary><c>auto</c> (mouse 0 / touch 0.4s, both 0 with a handle) or a duration; see the spec §4.1.</summary>
        [UIAttr("reorderHold"), Preserve]
        public string ReorderHoldAttr
        {
            set
            {
                if (string.IsNullOrEmpty(value) || value.Trim() == "auto") { _reorderHold = -1f; return; }
                if (TryParseSeconds(value, out var s)) _reorderHold = Mathf.Max(0f, s);
                else UILog.Warn(this, $"<ScrollList reorderHold='{value}'> is not 'auto' or a duration (0.4s / 400ms / 0.4); keeping the previous value.");
            }
        }

        /// <summary>Id of the node inside each row a drag has to start on; unset = the whole row.</summary>
        [UIAttr("reorderHandle"), Preserve]
        public string ReorderHandleAttr
        {
            set => _reorderHandle = string.IsNullOrEmpty(value) ? null : value;
        }

        /// <summary>Squeeze / settle tween length; <c>0</c> = instant.</summary>
        [UIAttr("reorderDuration"), Preserve]
        public string ReorderDurationAttr
        {
            set
            {
                if (TryParseSeconds(value, out var s)) _reorderDuration = Mathf.Max(0f, s);
                else UILog.Warn(this, $"<ScrollList reorderDuration='{value}'> is not a duration (0.15s / 150ms / 0.15); keeping the previous value.");
            }
        }

        private static bool TryParseSeconds(string value, out float seconds)
        {
            try { seconds = AnimationSpec.ParseSeconds(value); return true; }
            catch (FormatException) { seconds = 0f; return false; }
            catch (ArgumentException) { seconds = 0f; return false; }
        }

        /// <summary>The driver committed a drop: keep <see cref="Slots"/> in step with the sibling order.</summary>
        internal void PermuteSlots(int from, int to)
        {
            var row = _slots[from];
            _slots.RemoveAt(from);
            _slots.Insert(to, row);
        }

        internal void RaiseReordered(int from, int to) => _reordered.OnNext((from, to));

        /// <summary>
        /// The driver lifted <paramref name="row"/>: fire its <c>lift</c> hooks. Returns whether the
        /// driver should apply its own default lift look — it does unless an authored <c>on="lift"</c>
        /// hook exists on the row, in which case the look is the author's alone (spec §4.3).
        /// </summary>
        internal bool NotifyLifted(IControl row)
        {
            var marker = MarkerOf(row);
            marker?.Lift();
            return marker == null || !marker.HasLiftHook;
        }

        internal void NotifyDropped(IControl row) => MarkerOf(row)?.Drop();

        // The marker sits on the row's layout host (Content's direct child) — where the upward walk
        // in TriggerSourceResolver.FindReorderRow puts it. Null for a row nobody hooked.
        private static ReorderRowMarker MarkerOf(IControl row)
        {
            var host = row is Control c ? c.LayoutHost : row?.RectTransform;
            return host != null ? host.GetComponent<ReorderRowMarker>() : null;
        }

        // 静态 XML 子卡与 BindItems 建的卡都进 Content（同 Carousel 的 _strip）：挂在 ScrollList
        // 根上的子节点落在 Viewport 之外 —— 既不被裁剪、也不滚动、也不计入 Content 尺寸。
        protected internal override Transform ChildHostTransform => _content;

        public override void OnAttached()
        {
            _bornFrame = BornFrame.Capture();
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultContainerColor;
            ProceduralBuilders.ApplyDefaultInsetSprite(_bg);
            // The subclass adds the hooks a virtual list needs (spec 2026-09-29 §5.8) and behaves exactly like
            // ScrollRect otherwise.
            _scroll = GameObject.GetComponent<PuiScrollRect>() ?? GameObject.AddComponent<PuiScrollRect>();
            _scroll.Host = this;

            _viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
            _viewport.pivot = new Vector2(0f, 1f);
            // The mask graphic goes on first, as our subclass: ApplyViewportMask reuses whatever Image
            // the node already carries, and this one also opens the cross-axis sides (§ApplyCrossAxisClip).
            _maskImage = _viewport.gameObject.AddComponent<CrossAxisMaskImage>();
            // Viewport mask 三态 + 默认 pugui_9slice_mask 圆角，见 ApplyViewportMask 注释 / spec §2.3。
            ProceduralBuilders.ApplyViewportMask(_viewport, null, ProceduralBuilders.SpriteMaskRoundedRect);
            _scroll.viewport = _viewport;

            _content = ProceduralBuilders.AddChild(_viewport, "Content");
            _scroll.content = _content;
            // Lets a lift / drop hook nested anywhere in a row find its row by walking up — the row is
            // the child of this node on the way. Always present: hooks bind while the row is still
            // being instantiated, before this list has even collected it (spec 2026-09-16 §4.2).
            _content.gameObject.AddComponent<ScrollListContentMarker>().Owner = this;

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
        internal void PreConfigureContent(string direction, string columns, string virtualize = null)
        {
            _direction = string.IsNullOrEmpty(direction) ? "vertical" : direction;
            if (int.TryParse(columns, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
                _columns = Math.Max(0, n);

            // virtualize is decided here, once: it chooses Content's layout group before any child is built
            // into it, and switching mode later would mean rebuilding the group and the window (VIR-D10).
            _virtualAsked = bool.TryParse(virtualize, out var v) && v;
            _virtual = _virtualAsked && _columns < 1 && _direction != "horizontal";
            if (_virtualAsked && !_virtual)
                WarnOnce(PromptUGUI.Lint.ScrollListRules.VirtualLayoutCode,
                    "virtualize needs a single vertical column; with columns / direction=\"horizontal\" the list is " +
                    "not virtualized and lays out every row.");
            ApplyLayoutMode();
        }

        private void ApplyLayoutMode()
        {
            var wantGrid = IsGrid;
            var wantHorizontal = !wantGrid && _direction == "horizontal";
            var wantType = _virtual ? typeof(WindowedVerticalLayoutGroup)
                         : wantGrid ? typeof(GridLayoutGroup)
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
                if (_virtual) _layoutGroup = _content.gameObject.AddComponent<WindowedVerticalLayoutGroup>();
                else if (wantGrid) _layoutGroup = _content.gameObject.AddComponent<GridLayoutGroup>();
                else if (wantHorizontal) _layoutGroup = _content.gameObject.AddComponent<HorizontalLayoutGroup>();
                else _layoutGroup = _content.gameObject.AddComponent<VerticalLayoutGroup>();
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
            else if (_layoutGroup is HorizontalOrVerticalLayoutGroup hv)
            {
                // Play mode's defaults, spelled out. AddComponent in edit mode (UIPreview, EditMode
                // tests) leaves childControl* OFF, and the group then lays rows out by their default
                // 100×100 rect instead of the LayoutElement ApplyCommon wrote — a <Frame height="30">
                // row previewed 100 tall and never stretched (spec 2026-09-16 §14.2). Same four values
                // Play mode has always had, so the runtime does not change; the editor now matches it.
                hv.childControlWidth = true;
                hv.childControlHeight = true;
                hv.childForceExpandWidth = true;
                hv.childForceExpandHeight = true;
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
            ApplyCrossAxisClip();

            // The bar follows the axis: not built here (an authored one may still be on its way —
            // children instantiate after PreConfigureContent), just pointed the right way if present.
            WireScrollbar();
        }

        /// <summary>
        /// A list scrolls along one axis, so the viewport clips along that axis only: a vertical list
        /// (and a grid) leaves its left / right open, a horizontal list its top / bottom. What that
        /// frees is a row's glow, its shadow, and the lift scale of a drag-to-reorder session — all of
        /// which the viewport used to cut at its edge. Rounded corners still clip (the stencil band
        /// stops at the sprite's 9-slice borders); a border-less custom mask keeps its whole shape.
        /// <para>Re-applied whenever the mask mode or the direction changes — both hands are idempotent.</para>
        /// </summary>
        private void ApplyCrossAxisClip()
        {
            if (_viewport == null) return;
            var open = IsHorizontal ? 1 : 0;   // the axis the band runs along = the one that stays open
            var rectMask = _viewport.GetComponent<RectMask2D>();
            if (rectMask != null)
            {
                // Clipping.FindCullAndClipWorldRect does xMin + padding.x / xMax - padding.z: a
                // negative padding pushes the clip rect out. Culling uses the same rect, so rows
                // scrolled out along the main axis are still culled.
                rectMask.padding = open == 0
                    ? new Vector4(-CrossAxisMaskImage.Reach, 0f, -CrossAxisMaskImage.Reach, 0f)
                    : new Vector4(0f, -CrossAxisMaskImage.Reach, 0f, -CrossAxisMaskImage.Reach);
            }
            if (_maskImage != null) _maskImage.SetBandAxis(open);
        }

        // ───── the ScrollRect's tick (IScrollTickHost, spec 2026-09-29 §5.8) ─────

        void IScrollTickHost.OnScrollLateUpdate()
        {
            if (!_virtual || _binding == null || _inSync) return;
            var userMoved = _scroll.ConsumeUserMotion();
            if (userMoved) RecomputeStuck();
            var size = _viewport.rect.size;
            if (size.x != _lastViewport.x)
            {
                // Narrower or wider: every text rewraps.
                _model.InvalidateAll();
                _remeasure = true;
            }
            var resized = size != _lastViewport;
            if (!_remeasure && !_pending && RealizedRowsChanged()) _remeasure = true;
            if (_pending || _remeasure || userMoved || resized) Sync(_pendingBindAll, _pendingAnchor ?? CaptureAnchor());
        }

        /// <summary>
        /// A realized row no longer matches the model — its text was changed from outside, it animated, it was
        /// hidden. Compared row by row over the window, not by the total, which float error and collapsed rows fool.
        /// </summary>
        private bool RealizedRowsChanged()
        {
            for (var j = 0; j < _slots.Count; j++)
            {
                var host = HostOf(_slots[j]);
                if (host == null) return true;
                var collapsed = IsCollapsedRow(host);
                if (collapsed != _model.IsCollapsed(_slotIndex[j])) return true;
                if (!collapsed && Mathf.Abs(host.rect.height - _model.HeightOf(_slotIndex[j])) > 0.01f) return true;
            }
            return false;
        }

        private static bool IsCollapsedRow(RectTransform host)
        {
            if (!host.gameObject.activeSelf) return true;
            var element = host.GetComponent<LayoutElement>();
            return element != null && element.ignoreLayout;
        }

        void IScrollTickHost.OnScrollEnabled()
        {
            if (_virtual && _binding != null) _pending = true;
        }

        // ───── the scrollbar (IScrollbarHost) ─────

        RectTransform IScrollbarHost.ScrollbarHost => RectTransform;

        void IScrollbarHost.AdoptScrollbar(Scrollbar bar)
        {
            if (_bar != null && _bar != bar)
            {
                UILog.Warn(bar,
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

        /// <summary>
        /// <c>false</c> = 每次推送整表销毁重建（2026-09-14 之前的行为）。给"宿主自己重排过行的兄弟序"、
        /// "行是自定义 Control、内部状态不经属性重置"这类场合。默认 <c>true</c>：行按位置复用（见 Rebuild）。
        /// </summary>
        [UIAttr, Preserve]
        public bool ReuseItems
        {
            get => _reuseItems;
            set
            {
                _reuseItems = value;
                if (!value && _virtual)
                    WarnOnce(PromptUGUI.Lint.ScrollListRules.VirtualReuseCode,
                        "reuseItems=\"false\" is ignored on a virtual list — it always recycles its rows.");
            }
        }

        /// <summary>
        /// Only realize the rows near the viewport (spec 2026-09-29-scrolllist-virtualization). Fixed when the list
        /// is built — it chooses Content's layout group — so a later change (a variant, a theme) is ignored with a
        /// warning. A single vertical column only: with <c>columns</c> / <c>direction="horizontal"</c> it is not granted.
        /// </summary>
        [UIAttr, Preserve]
        public bool Virtualize
        {
            set
            {
                if (value != _virtualAsked)
                    WarnOnce(PromptUGUI.Lint.ScrollListRules.VirtualVariantCode,
                        $"virtualize is fixed when the list is built; it stays {(_virtualAsked ? "on" : "off")} — " +
                        "a variant or theme cannot switch it.");
            }
        }

        /// <summary>
        /// While the viewport is at the end, pushes and size changes keep it there — a chat log. "At the end" is
        /// remembered, not re-measured: only the user scrolling away (or back) changes it, never a row growing under
        /// the viewport. With it on, the start is not sticky (loading older items at the top keeps the view on the
        /// item the user was reading).
        /// </summary>
        [UIAttr, Preserve]
        public bool StickToEnd
        {
            get => _stickToEnd;
            set
            {
                _stickToEnd = value;
                if (!value) _stuckToEnd = false;
            }
        }

        [UIAttr, Preserve]
        public string Direction
        {
            set
            {
                var direction = string.IsNullOrEmpty(value) ? "vertical" : value;
                // A virtual list is one vertical column for good; the field stays as is, since GetNativeSize,
                // the cross-axis clip and the scrollbar wiring all read it.
                if (_virtual && direction == "horizontal")
                {
                    WarnOnce(PromptUGUI.Lint.ScrollListRules.VirtualLayoutCode,
                        "direction=\"horizontal\" is ignored on a virtual list — it stays one vertical column.");
                    return;
                }
                _direction = direction;
                ApplyLayoutMode();
            }
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
            set
            {
                var columns = Mathf.Max(0, value);
                if (_virtual && columns >= 1)
                {
                    WarnOnce(PromptUGUI.Lint.ScrollListRules.VirtualLayoutCode,
                        "columns is ignored on a virtual list — it stays one vertical column.");
                    return;
                }
                _columns = columns;
                ApplyLayoutMode();
            }
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
        private Vector2? ParseCellSize(string value)
        {
            var x = value == null ? -1 : value.IndexOf('x');
            if (x > 0
                && float.TryParse(value.Substring(0, x), NumberStyles.Float, CultureInfo.InvariantCulture, out var w)
                && float.TryParse(value.Substring(x + 1), NumberStyles.Float, CultureInfo.InvariantCulture, out var h))
                return new Vector2(w, h);
            if (!string.IsNullOrEmpty(value))
                UILog.Warn(this, $"<ScrollList cellSize='{value}'> is not 'WxH'; keeping the previous cell size.");
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
                ApplyCrossAxisClip();   // mask="" may have just added the RectMask2D
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

        internal override void OnBeforeApply()
        {
            base.OnBeforeApply();
            // An apply pass rewrites the rows' LayoutElement (flow= resets ignoreLayout) — a lifted
            // row would be pulled back into the layout under the finger. Structure changes and
            // gestures do not overlap: the session ends first, back at its origin (spec §5.7).
            _reorder?.Cancel();
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
            {
                ProceduralBuilders.ApplyViewportMask(
                    _viewport, _bg != null && _bg.sprite != null ? null : "",
                    ProceduralBuilders.SpriteMaskRoundedRect);
                ApplyCrossAxisClip();
            }
            // No authored <Scrollbar> arrived with the children — build the stock one now (children
            // instantiate before this apply, so by here the answer is final).
            EnsureDefaultScrollbar();
            // A ReSolve replays the static nodes (this one) BEFORE the rows, so the rows' new heights cannot be
            // measured here: mark them stale and let the next tick re-measure (§5.11).
            if (_virtual && _binding != null)
            {
                _model.InvalidateAll();
                _remeasure = _pending = true;
            }
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
            Subscribe(source, new PositionalBinding<T, TSlot>(bind));

        public IDisposable BindItems<T>(
            Observable<IReadOnlyList<T>> source,
            Action<IControl, T> bind) =>
            BindItems<T, IControl>(source, bind);

        /// <summary>
        /// Rows follow their items by <paramref name="key"/> (spec 2026-09-29-scrolllist-virtualization §5.5):
        /// an append instantiates one row, a trim retires one, a reorder only moves siblings — every other row
        /// is reused, and every row is still re-bound on every push. The previous push is matched by key when it
        /// came from any keyed <c>BindItems</c> with the same key type, so a fresh
        /// <c>BindItems(Observable.Return(list), …, key)</c> per push works too. A null or duplicate key rejects
        /// the whole push (logged through R3; the list keeps showing the previous one).
        /// </summary>
        public IDisposable BindItems<T, TSlot, TKey>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind,
            Func<T, TKey> key)
            where TSlot : class, IControl =>
            Subscribe(source, new KeyedBinding<T, TSlot, TKey>(bind, key ?? throw new ArgumentNullException(nameof(key))));

        public IDisposable BindItems<T, TKey>(
            Observable<IReadOnlyList<T>> source,
            Action<IControl, T> bind,
            Func<T, TKey> key) =>
            BindItems<T, IControl, TKey>(source, bind, key);

        private IDisposable Subscribe<T, TSlot>(Observable<IReadOnlyList<T>> source, ItemBinding<T, TSlot> binding)
            where TSlot : class, IControl =>
            source.Subscribe(items => OnPush(binding, items ?? Array.Empty<T>()));

        private void OnPush<T, TSlot>(ItemBinding<T, TSlot> binding, IReadOnlyList<T> items)
            where TSlot : class, IControl
        {
            // A host that forgot .AddTo(screen): the list is gone, the stream is not.
            if (_disposed) return;
            if (_inSync)
            {
                // A push from inside a bind callback (or anything else running during a sync): the latest one
                // wins and runs as soon as this one is done — never in the middle of it (VIR-P7).
                _queuedPush = () => OnPush(binding, items);
                return;
            }
            if (_factory == null)
                throw new InvalidOperationException(
                    "ScrollList.itemTemplate must be set before BindItems is called");

            // Validation (null / duplicate key) throws here, before anything about the list has changed.
            binding.Accept(items, _binding, _itemCount, ref _remap);
            var sameBinding = ReferenceEquals(binding, _binding);
            _binding = binding;
            _itemCount = items.Count;
            if (_virtual)
            {
                VirtualPush(items.Count, sameBinding);
                return;
            }

            _inSync = true;
            try { Rebuild(binding, items.Count); }
            catch
            {
                _queuedPush = null;
                throw;
            }
            finally { _inSync = false; }
            RunQueued();
        }

        private void RunQueued()
        {
            var push = _queuedPush;
            _queuedPush = null;
            push?.Invoke();
        }

        /// <summary>
        /// 复用（2026-09-14 scrolllist-row-reuse spec；2026-09-29 virtualization spec §5.5）：新第 j 项绑到
        /// <c>_remap[j]</c> 指的那一行 —— 无 key 时就是第 j 行（位置复用），有 key 时是那个 key 上一次所在的行。
        /// 复用的行不动 GO，只在再次 bind 之前释放上一次 bind 挂上的 <c>.AddTo(slot)</c> 订阅袋——把每个属性无条件
        /// 写一遍是 bind 回调的契约（SKILL）。整表重建只剩：静态占位卡在场的首次绑定（2026-09-10 spec 的规则）、
        /// <c>itemTemplate</c> 名变了、<c>reuseItems="false"</c>；被外部销毁的行只重建那一行。
        /// <para>先定结构、再 bind：没人要的旧行先退役（移出 Content 再销毁），兄弟序钉成数据序，然后才逐行 bind ——
        /// bind 回调看到的已是最终的列表。</para>
        /// </summary>
        private void Rebuild(IItemBinding binding, int count)
        {
            // A push mid-drag ends the session first (§5.7): Rebuild assumes row i is sibling i, which a
            // placeholder in Content would break, and the rows are about to be re-bound.
            _reorder?.Cancel();

            var full = !_bound || !ReuseItems || _slotsTemplate != _itemTemplate;
            if (full) ClearSlots();
            _slotsTemplate = _itemTemplate;

            _prevSlots.Clear();
            _prevSlots.AddRange(_slots);
            _slots.Clear();
            for (var j = 0; j < count; j++)
            {
                var from = full ? -1 : _remap[j];
                IControl row = null;
                if (from >= 0 && from < _prevSlots.Count && _prevSlots[from] != null
                    && _prevSlots[from].GameObject != null)
                {
                    row = _prevSlots[from];
                    _prevSlots[from] = null;
                    if (row is Control c) c.ReleaseSubscriptions();
                }
                // 新行（含被外部销毁的行重建）追加在 Content 末尾，下面按数据序钉回兄弟位。
                _slots.Add(row ?? _factory(_content));
            }
            foreach (var old in _prevSlots)
                if (old != null) Retire(old);
            _prevSlots.Clear();

            // Retired rows are out of Content, so its children are exactly _slots: pin data order.
            for (var j = 0; j < _slots.Count; j++)
            {
                var host = HostOf(_slots[j]);
                if (host.GetSiblingIndex() != j) host.SetSiblingIndex(j);
            }

            for (var j = 0; j < count; j++)
                if (!binding.TryBind(_slots[j], j))
                    throw new InvalidCastException(
                        $"itemTemplate='{_itemTemplate}' instantiated {_slots[j].GetType().Name}, " +
                        $"but BindItems expected {binding.SlotType.Name}");
        }

        // ───── virtual mode (spec 2026-09-29-scrolllist-virtualization §5) ─────

        private enum AnchorKind : byte
        {
            Keep,    // leave the scroll position alone
            Start,
            End,
            Item,    // keep an item's top edge where it is in the viewport
        }

        /// <summary>What has to stay put across a sync (§5.4).</summary>
        private struct Anchor
        {
            public AnchorKind Kind;
            public int Index;     // Item: the item
            public float Delta;   // Item: its top edge minus the scroll position
            public bool Snap;     // first push: go straight to the target

            public static Anchor Keep => new Anchor { Kind = AnchorKind.Keep };
        }

        private WindowedVerticalLayoutGroup Windowed => _layoutGroup as WindowedVerticalLayoutGroup;

        /// <summary>Distance from Content's top edge to the viewport's top edge.</summary>
        private float ScrollY => _content.anchoredPosition.y;

        /// <summary>What the ScrollRect's tick does once the user has moved the content — for EditMode tests.</summary>
        internal void RefreshWindow()
        {
            if (!_virtual || _binding == null || _inSync) return;
            if (_scroll.ConsumeUserMotion()) RecomputeStuck();
            Sync(bindAll: false, _pendingAnchor ?? CaptureAnchor());
        }

        /// <summary>The user moved the content: re-read the sticky edges from where it is now (VIR-P5).</summary>
        private void RecomputeStuck()
        {
            var s = ScrollY;
            var end = Mathf.Max(0f, _content.rect.height - _viewport.rect.height);
            _stuckToStart = s <= 1f;
            _stuckToEnd = _stickToEnd && s >= end - 1f;
        }

        private void VirtualPush(int count, bool sameBinding)
        {
            Anchor anchor;
            if (!_bound || _slotsTemplate != _itemTemplate)
            {
                // First push (the static placeholders go) or a new template: nothing realized survives.
                ClearSlots();
                _model.Reset(count);
                anchor = FirstPushAnchor();
            }
            else
            {
                // A push from another BindItems call is a new data source: it is anchored like a first push
                // (VIR-P4) — equal keys of a different kind of item must not drag the view to an unrelated row.
                // The rows are still reused by key.
                anchor = sameBinding ? _pendingAnchor ?? CaptureAnchor() : FirstPushAnchor();
                _model.Remap(_remap, count);
                RemapRealized(count);
                anchor = RemapAnchor(anchor, count);
            }
            _slotsTemplate = _itemTemplate;
            _pendingAnchor = null;

            if (!GameObject.activeInHierarchy)
            {
                // A hidden list only keeps the data; it realizes and binds when it shows (§5.9).
                _pendingAnchor = anchor;
                _pending = _pendingBindAll = true;
                return;
            }
            Sync(bindAll: true, anchor);
        }

        private static Anchor SnapTo(AnchorKind kind) => new Anchor { Kind = kind, Snap = true };

        /// <summary>A first push (or a new data source) opens at the start — at the end with stickToEnd.</summary>
        private Anchor FirstPushAnchor()
        {
            _stuckToStart = true;
            _stuckToEnd = _stickToEnd;
            return SnapTo(_stickToEnd ? AnchorKind.End : AnchorKind.Start);
        }

        /// <summary>
        /// What must stay put across the next sync (§5.4): the sticky edge the list is on; otherwise the first
        /// realized row the viewport shows, at its current place on screen; with no realized row in view (a jump
        /// past the window) the item the model puts at the viewport's top.
        /// </summary>
        private Anchor CaptureAnchor()
        {
            if (_stickToEnd && _stuckToEnd)
                return new Anchor { Kind = AnchorKind.End, Delta = ScrollY - _lastEndScroll };
            if (!_stickToEnd && _stuckToStart) return Anchor.Keep;   // at the start, S simply stays

            var s = ScrollY;
            var view = _viewport.rect.height;
            for (var j = 0; j < _slots.Count; j++)
            {
                var host = HostOf(_slots[j]);
                if (host == null || !host.gameObject.activeSelf) continue;
                var top = TopOf(host);
                if (top + host.rect.height > s && top < s + view)
                    return new Anchor { Kind = AnchorKind.Item, Index = _slotIndex[j], Delta = top - s };
            }
            if (_model.Count == 0) return Anchor.Keep;
            var k = _model.IndexAt(s);
            return new Anchor { Kind = AnchorKind.Item, Index = k, Delta = _model.OffsetOf(k) - s };
        }

        // Distance from Content's top edge to a row's top edge, for a row the Content group has laid out.
        private static float TopOf(RectTransform host) =>
            -host.anchoredPosition.y - host.rect.height * (1f - host.pivot.y);

        /// <summary>
        /// A push moved the items: each realized row follows its item to the item's new index; a row whose item is
        /// gone is parked straight away — it is not bound again.
        /// </summary>
        private void RemapRealized(int newCount)
        {
            if (_slots.Count == 0) return;
            _keep.Clear();
            for (var k = 0; k < _slots.Count; k++) _keep[_slotIndex[k]] = _slots[k];
            _nextSlots.Clear();
            _nextIndex.Clear();
            for (var j = 0; j < newCount && _keep.Count > 0; j++)
            {
                var from = _remap[j];
                if (from < 0 || !_keep.TryGetValue(from, out var row)) continue;
                _keep.Remove(from);
                _nextSlots.Add(row);
                _nextIndex.Add(j);
            }
            foreach (var gone in _keep.Values) Park(gone);
            _keep.Clear();
            _slots.Clear();
            _slots.AddRange(_nextSlots);
            _slotIndex.Clear();
            _slotIndex.AddRange(_nextIndex);
        }

        private Anchor RemapAnchor(Anchor anchor, int newCount)
        {
            if (anchor.Kind != AnchorKind.Item) return anchor;
            int after = -1, before = -1, afterFrom = int.MaxValue, beforeFrom = int.MinValue;
            for (var j = 0; j < newCount; j++)
            {
                var from = _remap[j];
                if (from < 0) continue;
                if (from == anchor.Index)
                {
                    anchor.Index = j;
                    return anchor;
                }
                if (from > anchor.Index && from < afterFrom) { afterFrom = from; after = j; }
                if (from < anchor.Index && from > beforeFrom) { beforeFrom = from; before = j; }
            }
            // The anchor item is gone: the next item that survives takes its place, else the one before it.
            if (after >= 0) { anchor.Index = after; return anchor; }
            if (before >= 0) { anchor.Index = before; return anchor; }
            return SnapTo(_stickToEnd ? AnchorKind.End : AnchorKind.Start);
        }

        /// <summary>
        /// Realizes the rows for the items near the viewport, measures them and positions the window (§5.6). A
        /// push rebinds every realized row (<paramref name="bindAll"/>); a scroll binds only the rows that enter.
        /// </summary>
        private void Sync(bool bindAll, Anchor anchor)
        {
            var group = Windowed;
            if (group == null || _inSync) return;
            var isPush = bindAll;
            var remeasure = _remeasure;
            _remeasure = false;
            _inSync = true;
            _syncIsPush = isPush;
            SyncCount++;
            try
            {
                EnsureSettled();
                if (_slotsTemplate != _itemTemplate)
                {
                    // The template changed without a push (a variant): the realized rows came from the old one.
                    ClearSlots();
                    _model.InvalidateAll();
                    _slotsTemplate = _itemTemplate;
                    bindAll = true;
                }
                // The group is rewritten on every ReSolve in attribute order: read it, never cache it (VIR-P9).
                _model.SetMetrics(group.spacing, group.padding.top, group.padding.bottom);
                var view = _viewport.rect.height;
                if (_model.Count == 0)
                {
                    ParkAll();
                    group.Leading = group.Trailing = 0f;
                    LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                }
                else
                {
                    if (!_model.HasEstimate)
                    {
                        // Nothing measured yet: realize one row where the view will be; it sets the estimate.
                        var seed = SeedIndex(anchor);
                        Reconcile(seed, seed, bindAll: true);
                        group.Leading = _model.ExtentBefore(seed);
                        group.Trailing = _model.ExtentAfter(seed);
                        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                        MeasureWindow();
                    }
                    var realizedFirst = _slotIndex.Count > 0 ? _slotIndex[0] : -1;
                    var realizedLast = _slotIndex.Count > 0 ? _slotIndex[_slotIndex.Count - 1] : -1;
                    var contiguous = realizedLast - realizedFirst + 1 == _slotIndex.Count;
                    // Measuring changes the estimate, which moves the window: a few passes settle it. The rows'
                    // own heights do not depend on Leading, so after the first pass only positions change.
                    for (var pass = 0; pass < 4; pass++)
                    {
                        var s = TargetScroll(anchor, view);
                        var margin = Mathf.Max(1f, _model.Estimate);
                        _model.TryWindow(s - margin, s + view + margin, out var first, out var last);
                        var leading = _model.ExtentBefore(first);
                        var trailing = _model.ExtentAfter(last);
                        var sameWindow = contiguous && first == realizedFirst && last == realizedLast;
                        if (sameWindow && !bindAll && !remeasure
                            && leading == group.Leading && trailing == group.Trailing) break;
                        var realize = !sameWindow || bindAll;
                        if (realize) Reconcile(first, last, bindAll);
                        group.Leading = leading;
                        group.Trailing = trailing;
                        LayoutRebuilder.ForceRebuildLayoutImmediate(_content);
                        if (realize || remeasure) MeasureWindow();
                        realizedFirst = first;
                        realizedLast = last;
                        contiguous = true;
                        bindAll = false;
                        remeasure = false;
                    }
                }
                ApplyAnchor(anchor, view, isPush);
                _pending = _pendingBindAll = false;
                _pendingAnchor = null;
                _lastViewport = _viewport.rect.size;
                _lastEndScroll = Mathf.Max(0f, _model.Total - view);
            }
            catch
            {
                _queuedPush = null;
                _bindError = null;
                throw;
            }
            finally
            {
                _inSync = false;
            }
            var bindError = _bindError;
            _bindError = null;
            if (bindError != null)
            {
                _queuedPush = null;
                bindError.Throw();   // through R3's unhandled-exception handler: logged, the list is consistent
            }
            RunQueued();
        }

        /// <summary>
        /// Once, before the first real sync: a list built this frame (or with no size yet) gets the layout it sits
        /// in done first, so the window is cut and the rows are measured at the final size (§5.10). A list on a
        /// page shown later was laid out when its screen opened.
        /// <para>The rebuild starts at the top of the chain of layout groups the list belongs to — the same root
        /// <c>LayoutRebuilder.MarkLayoutForRebuild</c> would pick. Rebuilding the screen root instead does nothing:
        /// the rebuilder skips every subtree whose root has no layout controller.</para>
        /// </summary>
        private void EnsureSettled()
        {
            if (_settled) return;
            _settled = true;
            if (!BornFrame.IsCurrent(_bornFrame) && _viewport.rect.height > 0f) return;
            var root = LayoutHost;
            while (root.parent is RectTransform parent)
            {
                var group = parent.GetComponent<LayoutGroup>();
                if (group == null || !group.enabled) break;
                root = parent;
            }
            LayoutRebuilder.ForceRebuildLayoutImmediate(root);
        }

        private int SeedIndex(Anchor anchor)
        {
            var count = _model.Count;
            switch (anchor.Kind)
            {
                case AnchorKind.Item: return Mathf.Clamp(anchor.Index, 0, count - 1);
                case AnchorKind.End: return count - 1;
                case AnchorKind.Start: return 0;
                default: return Mathf.Clamp(_model.IndexAt(ScrollY), 0, count - 1);
            }
        }

        private float TargetScroll(Anchor anchor, float view)
        {
            switch (anchor.Kind)
            {
                case AnchorKind.Start: return 0f;
                // Delta keeps an elastic pull past the end as it was (0 when resting on the end).
                case AnchorKind.End: return Mathf.Max(0f, _model.Total - view) + anchor.Delta;
                case AnchorKind.Item:
                    return _model.OffsetOf(Mathf.Clamp(anchor.Index, 0, _model.Count - 1)) - anchor.Delta;
                default: return ScrollY;
            }
        }

        /// <summary>
        /// Puts the anchor back (VIR-P6). On the scroll path this only ever SHIFTS the content by what the anchor
        /// moved — an elastic pull past either end plays out instead of being snapped away every frame. A push
        /// lands on a valid position once the list is at rest (not dragging, no inertia); a first push always does.
        /// </summary>
        private void ApplyAnchor(Anchor anchor, float view, bool isPush)
        {
            var target = TargetScroll(anchor, view);
            if (anchor.Snap || (isPush && !_scroll.IsDragging && _scroll.velocity == Vector2.zero))
                target = Mathf.Clamp(target, 0f, Mathf.Max(0f, _model.Total - view));
            // Even a zero shift goes through: the bar is written from the new bounds every sync. A bar left at its
            // stale value is read BACK by the ScrollRect on its next layout pass and drags the content with it (a
            // fresh list's bar sits at 0 = the end).
            _scroll.ShiftContentY(target - ScrollY);
        }

        /// <summary>
        /// Makes <c>_slots</c> the rows for items <paramref name="first"/>..<paramref name="last"/>, in sibling order.
        /// A row leaving the window is handed to an entering item first (it stays in Content — only its sibling index
        /// and its binding change); what is left over is parked; what is still missing comes from the pool, then the
        /// factory (always into Content: a lift / drop hook finds its list by walking up during its first apply).
        /// </summary>
        private void Reconcile(int first, int last, bool bindAll)
        {
            _keep.Clear();
            _leaving.Clear();
            for (var k = 0; k < _slots.Count; k++)
            {
                var index = _slotIndex[k];
                if (index >= first && index <= last && !_keep.ContainsKey(index)) _keep[index] = _slots[k];
                else _leaving.Add(_slots[k]);
            }
            _nextSlots.Clear();
            _nextIndex.Clear();
            _toBind.Clear();
            for (var index = first; index <= last; index++)
            {
                if (_keep.TryGetValue(index, out var row))
                {
                    if (bindAll) _toBind.Add(_nextSlots.Count);
                }
                else
                {
                    row = TakeRow();
                    _toBind.Add(_nextSlots.Count);
                }
                _nextSlots.Add(row);
                _nextIndex.Add(index);
            }
            foreach (var spare in _leaving) Park(spare);
            _leaving.Clear();
            _keep.Clear();
            _slots.Clear();
            _slots.AddRange(_nextSlots);
            _slotIndex.Clear();
            _slotIndex.AddRange(_nextIndex);

            // Parked rows are out of Content, so its children are exactly _slots: pin the window order.
            for (var j = 0; j < _slots.Count; j++)
            {
                var host = HostOf(_slots[j]);
                if (host.GetSiblingIndex() != j) host.SetSiblingIndex(j);
            }
            foreach (var j in _toBind)
            {
                try
                {
                    BindRow(_slots[j], _slotIndex[j]);
                }
                catch (Exception e)
                {
                    // Keep going: the window must stay consistent. A push reports the first failure once the sync
                    // is done; a scroll has no caller to report to.
                    if (_syncIsPush) _bindError ??= System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(e);
                    else UILog.Error(this, $"<ScrollList id='{Id}'>: bind threw for item {_slotIndex[j]} while scrolling: {e}");
                }
            }
        }

        private IControl TakeRow()
        {
            if (_leaving.Count > 0)
            {
                var row = _leaving[_leaving.Count - 1];
                _leaving.RemoveAt(_leaving.Count - 1);
                return row;
            }
            while (_parked.Count > 0)
            {
                var row = _parked[_parked.Count - 1];
                _parked.RemoveAt(_parked.Count - 1);
                var host = HostOf(row);
                if (host == null) continue;   // destroyed from outside while parked
                host.SetParent(_content, worldPositionStays: false);
                return row;
            }
            return _factory(_content);
        }

        private void BindRow(IControl row, int index)
        {
            if (row is Control c) c.ReleaseSubscriptions();
            if (!_binding.TryBind(row, index))
                throw new InvalidCastException(
                    $"itemTemplate='{_itemTemplate}' instantiated {row.GetType().Name}, " +
                    $"but BindItems expected {_binding.SlotType.Name}");
        }

        /// <summary>Reads the laid-out height of every realized row back into the model.</summary>
        private void MeasureWindow()
        {
            for (var j = 0; j < _slots.Count; j++)
            {
                var host = HostOf(_slots[j]);
                if (IsCollapsedRow(host))
                {
                    // The group leaves it out — no height, no gap (VIR-P9).
                    _model.SetCollapsed(_slotIndex[j]);
                    WarnOnce("hidden-row",
                        "a row hidden by its bind callback (Hidden = true or flow=\"false\") takes no space in a " +
                        "virtual list — filter the items instead of hiding rows.");
                    continue;
                }
                _model.SetMeasured(_slotIndex[j], host.rect.height);
            }
        }

        /// <summary>Moves a row into the inactive pool; its subscriptions are released now, not at its next bind.</summary>
        private void Park(IControl row)
        {
            var host = HostOf(row);
            if (host == null) return;   // destroyed from outside
            if (row is Control c) c.ReleaseSubscriptions();
            host.SetParent(EnsurePool(), worldPositionStays: false);
            _parked.Add(row);
        }

        private void ParkAll()
        {
            foreach (var row in _slots) Park(row);
            _slots.Clear();
            _slotIndex.Clear();
        }

        private void ReleaseParked()
        {
            foreach (var row in _parked) row.Dispose();
            _parked.Clear();
        }

        private static RectTransform HostOf(IControl row) => row is Control c ? c.LayoutHost : row.RectTransform;

        private RectTransform EnsurePool()
        {
            if (_pool != null) return _pool;
            _pool = ProceduralBuilders.AddChild(RectTransform, "Pool");
            _pool.gameObject.AddComponent<VerticalLayoutGroup>().enabled = false;
            _pool.gameObject.SetActive(false);
            return _pool;
        }

        /// <summary>
        /// Takes a row out of Content, then disposes it. In Play mode <c>Destroy</c> waits for the end of the
        /// frame; until then the row would still be laid out (a trimmed first row would shift the whole list
        /// for a frame) and still hold a sibling index (ReorderDriver relies on slot i being sibling i).
        /// </summary>
        private void Retire(IControl row)
        {
            var host = HostOf(row);
            if (host != null) host.SetParent(EnsurePool(), worldPositionStays: false);
            row.Dispose();
        }

        private void ClearSlots()
        {
            // 标记已动态绑定：之后 ReSolve 的静态收集不再执行。静态卡在这里被退役，
            // Screen.ReSolve 靠 `control.GameObject == null` 跳过它们的 ElementNode。
            _reorder?.Cancel();
            _bound = true;
            foreach (var s in _slots)
            {
                Retire(s);
            }
            _slots.Clear();
            _slotIndex.Clear();
            ReleaseParked();
        }

        public override void Dispose()
        {
            _disposed = true;
            _reorder?.Cancel();
            // The whole list goes away: no need to move the rows out of Content first.
            foreach (var s in _slots)
            {
                s.Dispose();
            }
            _slots.Clear();
            _slotIndex.Clear();
            foreach (var p in _parked) p.Dispose();
            _parked.Clear();
            // An adopted bar is a Screen-owned node and is disposed with the rest of _nodeMap; the
            // default one is ours.
            if (_ownsBar) _bar?.Dispose();
            _reordered.Dispose();
            base.Dispose();
        }
    }
}
