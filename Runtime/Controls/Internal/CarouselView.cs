using System;
using System.Collections.Generic;
using LitMotion;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// 轮播运行机制：连续 _scroll 翻页模型 + LitMotion 吸附 + Update 自动播放 +
    /// IDragHandler 拖动 + OnRectTransformDimensionsChange resize 重排 + 指示点驱动。
    /// 挂在 Carousel root 上（拖动事件从卡片冒泡到这里，同 ScrollRect）。
    /// 真实运行状态（_current / _scroll / 计时器）都在这里，跨 Screen.ReSolve 存活。
    /// </summary>
    internal sealed class CarouselView : UIBehaviour,
        IInitializePotentialDragHandler, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform _root;
        private RectTransform _viewport;
        private RectTransform _strip;
        private RectTransform _indicator;

        private readonly List<IControl> _cards = new();
        private readonly List<UnityImage> _dotImages = new();
        private readonly List<StateTintReactor> _dotReactors = new();

        // 翻页状态
        private float _scroll;       // 连续页位置；静止时 == _current
        private int _current;        // 已提交页（PeekRuntimeState 读它）
        private int _lastDotCurrent = -2;
        private float _pageWidth = 1f;
        private float _pageHeight = 1f;

        // 行为参数
        private float _interval = 5f;
        private bool _loop = true;
        private float _transition = 0.3f;
        private bool _playing = true;

        // 运行标志
        private float _elapsed;
        private bool _dragging;
        private bool _animating;
        private MotionHandle _handle;

        // 拖动翻页状态
        private float _dragStartScroll;
        private float _dragAccumX;
        private const float SnapThreshold = 0.2f;   // 翻页所需位移占页宽比例

        // 指示点样式
        private string _dotsAnchor;
        private Vector2 _dotSize = new(8f, 8f);
        private float _dotSpacing = 6f;
        private string _dotMargin;
        private Sprite _dotSprite;
        private Sprite _dotSelectedSprite;
        private string _dotTint;
        private Color _dotBaseColor = Color.white;
        private string _dotHoverColor;
        private string _dotPressedColor;
        private Color? _dotSelectedColor;
        private bool _dotTriSlice;
        private Sprite[] _slices;       // [left, mid, right] sub-sprites of _dotSprite when tri-sliced
        private Sprite _sliceSource;    // the sprite _slices was cut from (rebuild only when this changes)

        private bool _staticCollected;
        private bool _bound;

        public Action<int> OnCurrent { get; set; }
        public RectTransform StripRect => _strip;
        internal float ScrollForTests => _scroll;   // test seam: observe the live (mid-drag) scroll position
        public int CardCount => _cards.Count;
        public int CurrentIndex => _current;

        public bool Playing
        {
            get => _playing;
            set { _playing = value; _elapsed = 0f; }
        }

        public void Init(RectTransform root, RectTransform viewport, RectTransform strip, RectTransform indicator)
        {
            _root = root;
            _viewport = viewport;
            _strip = strip;
            _indicator = indicator;
        }

        // —— 行为参数 setter（Carousel 转发）——
        public void SetInterval(float v) => _interval = v;
        public void SetLoop(bool v) => _loop = v;
        public void SetTransition(float v) => _transition = Mathf.Max(0f, v);

        // —— 卡片管理 ——
        // 首次 OnAfterApply 把已建好的静态子卡（在 Strip 下）收进 _cards；只跑一次，
        // 且 BindItems 调过之后（_bound）不再收（避免 ReSolve 时把已 Dispose 的旧引用收回）。
        public void SetStaticCards(IReadOnlyList<IControl> children)
        {
            if (_staticCollected || _bound) return;
            _staticCollected = true;
            foreach (var c in children) _cards.Add(c);
        }
        public void AddCard(IControl card) { _cards.Add(card); }

        public void ClearCards()
        {
            _bound = true;   // 标记已动态绑定：之后 ReSolve 的 SetStaticCards 不再收旧引用
            foreach (var c in _cards) c.Dispose();
            _cards.Clear();
        }

        // BindItems 重建完：钳当前页进新范围，重建指示点，重排，重启自动播放计时。
        public void OnItemsRebuilt()
        {
            int prev = _current;
            if (_cards.Count == 0) { _current = -1; _scroll = 0f; }
            else _current = Mathf.Clamp(_current < 0 ? 0 : _current, 0, _cards.Count - 1);
            RebuildIndicator();
            RelayoutNow();
            StartAutoplayIfNeeded();
            // Emit when the committed page changed (empty -> -1, or clamped into a smaller deck),
            // mirroring GoTo's change-guarded emission so OnCurrentChanged stays in sync after a rebuild.
            if (_current != prev) OnCurrent?.Invoke(_current);
        }

        // —— 指示点 ——
        public void ConfigureDots(string anchor, Vector2 size, float spacing, string margin,
                                  Sprite sprite, Sprite selectedSprite, string tint,
                                  Color baseColor, string hover, string pressed, Color? selected,
                                  bool triSlice)
        {
            _dotsAnchor = anchor;
            _dotSize = size;
            _dotSpacing = spacing;
            _dotMargin = margin;
            _dotSprite = sprite;
            _dotSelectedSprite = selectedSprite;
            _dotTint = tint;
            _dotBaseColor = baseColor;
            _dotHoverColor = hover;
            _dotPressedColor = pressed;
            _dotSelectedColor = selected;
            _dotTriSlice = triSlice;
            RebuildSlices();
            if (triSlice && selectedSprite != null)
                Debug.LogWarning("<Carousel>: dotSelectedSprite is ignored when dotTriSlice is on " +
                    "(tri-slice selection is colour-only via dotSelectedColor).");
        }

        // Cut dotSprite into 3 equal horizontal sub-sprites [left cap | tileable middle | right cap].
        // Built once and reused; only re-cut when triSlice toggles or the source sprite changes, so a
        // ReSolve doesn't churn Sprite objects. Sub-sprites are owned here and destroyed on teardown.
        private void RebuildSlices()
        {
            if (!_dotTriSlice || _dotSprite == null || _dotSprite != _sliceSource) DestroySlices();
            if (!_dotTriSlice || _dotSprite == null || _slices != null) return;

            _slices = new Sprite[3];
            var tr = _dotSprite.textureRect;
            var b = _dotSprite.border;          // (left, bottom, right, top) px — the source 9-slice border
            float third = tr.width / 3f;
            var pivot = new Vector2(0.5f, 0.5f);
            for (int s = 0; s < 3; s++)
            {
                var rect = new Rect(tr.x + third * s, tr.y, third, tr.height);
                // Carry the source 9-slice border so segments don't stretch: the left cap keeps the
                // left border, the right cap keeps the right border, internal cut edges get 0 (the
                // tileable part stretches), and top/bottom carry to every segment.
                float left = s == 0 ? Mathf.Min(b.x, third) : 0f;
                float right = s == 2 ? Mathf.Min(b.z, third) : 0f;
                var border = new Vector4(left, b.y, right, b.w);
                _slices[s] = Sprite.Create(_dotSprite.texture, rect, pivot,
                    _dotSprite.pixelsPerUnit, 0, SpriteMeshType.FullRect, border);
                _slices[s].name = _dotSprite.name + "_tri" + s;
            }
            _sliceSource = _dotSprite;
        }

        private void DestroySlices()
        {
            if (_slices != null)
                foreach (var s in _slices)
                    if (s != null) { if (UnityEngine.Application.isPlaying) Destroy(s); else DestroyImmediate(s); }
            _slices = null;
            _sliceSource = null;
        }

        // Which slice a dot shows: first = left cap (0), last = right cap (2), the rest = middle (1).
        private static int SegmentIndex(int dotIndex, int dotCount)
            => dotIndex == 0 ? 0 : (dotIndex == dotCount - 1 ? 2 : 1);

        // 按卡数建/拆指示点。dots= 空 或 卡数<=1 → 隐藏整排。每个 dot = Image + PuiButton
        // (点击跳转 + 提供 hover/pressed 的 IStateSource) + StateTintReactor (状态着色)。
        public void RebuildIndicator()
        {
            // 清旧
            for (int i = _indicator.childCount - 1; i >= 0; i--)
            {
                var go = _indicator.GetChild(i).gameObject;
                if (UnityEngine.Application.isPlaying) Destroy(go); else DestroyImmediate(go);
            }
            _dotImages.Clear();
            _dotReactors.Clear();
            _lastDotCurrent = -2;

            bool show = !string.IsNullOrEmpty(_dotsAnchor) && _dotsAnchor != "none" && _cards.Count > 1;
            _indicator.gameObject.SetActive(show);
            if (!show) return;

            // 整排锚点 / 尺寸 / 边距。非法锚点回退 bottom-center（与 lint PUI-CAROUSEL-DOTS-ANCHOR 的承诺一致）。
            AnchorPreset preset;
            try { preset = AnchorPreset.Parse(_dotsAnchor); }
            catch { preset = AnchorPreset.Parse("bottom-center"); }
            AnchorResolver.Resolve(preset, out var aMin, out var aMax, out var pivot);
            _indicator.anchorMin = aMin; _indicator.anchorMax = aMax; _indicator.pivot = pivot;
            float rowW = _cards.Count * _dotSize.x + (_cards.Count - 1) * _dotSpacing;
            _indicator.sizeDelta = new Vector2(rowW, _dotSize.y);
            _indicator.anchoredPosition = MarginOffset(_dotMargin, pivot);

            var layout = _indicator.gameObject.GetComponent<HorizontalLayoutGroup>()
                         ?? _indicator.gameObject.AddComponent<HorizontalLayoutGroup>();
            layout.spacing = _dotSpacing;
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.childControlWidth = false;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            var abs = StateColorSet.Resolve(_dotHoverColor, _dotPressedColor, null, null);
            var emptyChildren = System.Array.Empty<IControl>();
            for (int i = 0; i < _cards.Count; i++)
            {
                int captured = i;
                var dotRt = ProceduralBuilders.AddChild(_indicator, "Dot");
                dotRt.sizeDelta = _dotSize;
                var le = dotRt.gameObject.AddComponent<LayoutElement>();
                le.preferredWidth = _dotSize.x; le.preferredHeight = _dotSize.y;

                var img = dotRt.gameObject.AddComponent<UnityImage>();
                img.color = _dotBaseColor;
                img.raycastTarget = true;
                // Tri-slice: this dot shows its left/middle/right segment; else the whole sprite.
                var shown = (_dotTriSlice && _slices != null) ? _slices[SegmentIndex(i, _cards.Count)] : _dotSprite;
                if (shown != null)
                {
                    img.sprite = shown;
                    img.type = shown.border != Vector4.zero ? UnityImage.Type.Sliced : UnityImage.Type.Simple;
                }
                ImageTint.Apply(img, _dotTint);

                var btn = dotRt.gameObject.AddComponent<PuiButton>();
                btn.targetGraphic = img;
                btn.onClick.AddListener(() => GoTo(captured, animated: true));

                var reactor = StateTintInstaller.Install(dotRt.gameObject, btn, emptyChildren,
                    abs, default, _dotSelectedColor, selected: captured == _current);

                _dotImages.Add(img);
                _dotReactors.Add(reactor);
            }
            RefreshDotSelection();
        }

        // 把 dots= 的 margin（T,R,B,L，支持 '_'）转成相对锚点的偏移。只取与 pivot 同侧的两个分量。
        private static Vector2 MarginOffset(string margin, Vector2 pivot)
        {
            if (string.IsNullOrEmpty(margin)) return Vector2.zero;
            var p = margin.Split(',');
            float T = ParseSlot(p, 0), R = ParseSlot(p, 1), B = ParseSlot(p, 2), L = ParseSlot(p, 3);
            // pivot.y: 0=底(用 B 上推), 1=顶(用 T 下推); pivot.x: 0=左(用 L 右推), 1=右(用 R 左推)
            float x = pivot.x <= 0f ? L : (pivot.x >= 1f ? -R : 0f);
            float y = pivot.y <= 0f ? B : (pivot.y >= 1f ? -T : 0f);
            return new Vector2(x, y);
        }

        private static float ParseSlot(string[] parts, int i)
        {
            if (i >= parts.Length) return 0f;
            var s = parts[i].Trim();
            if (s == "_" || s.Length == 0) return 0f;
            return float.TryParse(s, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : 0f;
        }

        // 当前页变化时刷新各 dot 的选中态：reactor 选中基色 + 可选 overrideSprite 换图。
        private void RefreshDotSelection()
        {
            if (_dotImages.Count == 0 || _current == _lastDotCurrent) return;
            _lastDotCurrent = _current;
            for (int i = 0; i < _dotImages.Count; i++)
            {
                _dotReactors[i]?.SetSelected(i == _current);
                if (_dotImages[i] != null)
                    // selectedSprite swap is suppressed under tri-slice (it would replace the dot's
                    // 1/3 slice with a full-width sprite and break the connected bar) — selection is colour-only.
                    _dotImages[i].overrideSprite =
                        (!_dotTriSlice && i == _current && _dotSelectedSprite != null) ? _dotSelectedSprite : null;
            }
        }

        // —— 翻页 ——
        // 翻到 index：loop 取最短环向，非 loop 钳位。animated=true 用 LitMotion 把 _scroll
        // 平滑滑到目标整数；false 立即定位。仅当提交页真正变化时 fire OnCurrent。
        public void GoTo(int index, bool animated)
        {
            int n = _cards.Count;
            if (n == 0) { _current = Mathf.Max(0, index); _scroll = _current; return; }

            int target = _loop ? ((index % n) + n) % n : Mathf.Clamp(index, 0, n - 1);
            bool changed = target != _current;
            _elapsed = 0f;

            if (_handle.IsActive()) _handle.TryCancel();

            if (!animated || _transition <= 0f || !UnityEngine.Application.isPlaying)
            {
                // Clear any flag leaked from the tween just cancelled above (TryCancel
                // suppresses OnComplete, which is where _animating would otherwise reset).
                _animating = false;
                _current = target;
                _scroll = target;
                Reposition();
            }
            else
            {
                // 选最短环向目标 scroll（可能为负或 >n，补间结束再归一化）。
                float delta = target - _scroll;
                if (_loop)
                {
                    delta = Mathf.Repeat(delta + n * 0.5f, n) - n * 0.5f;
                }
                float targetScroll = _scroll + delta;
                _current = target;
                _animating = true;
                _handle = LMotion.Create(_scroll, targetScroll, _transition)
                    .WithEase(Ease.OutCubic)
                    .WithOnComplete(() => { _scroll = target; _animating = false; Reposition(); })
                    .Bind(this, static (v, self) => { self._scroll = v; self.Reposition(); });
            }

            if (changed) OnCurrent?.Invoke(_current);
        }

        public void Next(bool animated) => GoTo(_current + 1, animated);
        public void Previous(bool animated) => GoTo(_current - 1, animated);
        // 按连续位置 _scroll 把每张卡放到正确 x。无限循环用 Mathf.Repeat 把偏移
        // 折进 [-N/2, N/2)，越界的卡瞬移到另一侧（N>=3 时在屏外发生，不可见）。
        private void Reposition()
        {
            int n = _cards.Count;
            if (n == 0) return;
            for (int i = 0; i < n; i++)
            {
                var card = _cards[i] as Control;
                if (card?.GameObject == null) continue;
                float off = i - _scroll;
                if (_loop) off = Mathf.Repeat(off + n * 0.5f, n) - n * 0.5f;
                var rt = card.RectTransform;
                rt.anchorMin = new Vector2(0.5f, 0.5f);
                rt.anchorMax = new Vector2(0.5f, 0.5f);
                rt.pivot = new Vector2(0.5f, 0.5f);
                rt.sizeDelta = new Vector2(_pageWidth, _pageHeight);
                rt.anchoredPosition = new Vector2(off * _pageWidth, 0f);
            }
            RefreshDotSelection();
        }

        // 重算页宽高 + 重排卡片。OnAfterApply（初始 / ReSolve）与 resize 都调它。
        public void RelayoutNow()
        {
            if (_handle.IsActive()) _handle.TryCancel();
            _animating = false;
            // Viewport stretch-fills root with zero offsets (see OnAttached), so root.rect == viewport.rect.
            // If the Viewport is ever inset from root, switch this to the viewport's rect.
            var r = _root.rect;
            _pageWidth = r.width > 0f ? r.width : 1f;
            _pageHeight = r.height > 0f ? r.height : 1f;
            if (_cards.Count > 0)
                _current = _loop ? ((_current % _cards.Count) + _cards.Count) % _cards.Count
                                 : Mathf.Clamp(_current, 0, _cards.Count - 1);
            _scroll = _current;
            Reposition();
        }
        public void StartAutoplayIfNeeded() => _elapsed = 0f;

        // —— Unity 生命周期 ——
        private void Update()
        {
            if (!_playing || _interval <= 0f || _cards.Count <= 1 || _dragging || _animating) return;
            _elapsed += UnityEngine.Time.unscaledDeltaTime;
            if (_elapsed >= _interval)
            {
                _elapsed = 0f;
                GoTo(_current + 1, animated: true);
            }
        }
        // RectTransform 几何变更（窗口 resize / 锚点重算）→ 重算页宽高并以保住的 _current 瞬移重排。
        // UIBehaviour 可靠收到该回调（plain MonoBehaviour 不一定），所以 CarouselView : UIBehaviour。
        protected override void OnRectTransformDimensionsChange()
        {
            base.OnRectTransformDimensionsChange();
            if (_root == null || !isActiveAndEnabled) return;
            // Don't reflow mid-drag: RelayoutNow snaps _scroll to _current, which would jump the strip
            // out from under the finger. Stale page width self-corrects on the next layout / ReSolve.
            if (_dragging) return;
            if (_root.rect.width <= 0f) return;
            RelayoutNow();
        }

        // EditMode 测试 seam：Unity 的 OnRectTransformDimensionsChange 在脱离 Canvas 的
        // 孤立 GameObject 上不会自动触发，与 RectDimensionsRelay.InvokeRectChangedForTests 同构。
        internal void InvokeRectChangedForTests() => OnRectTransformDimensionsChange();

        void IInitializePotentialDragHandler.OnInitializePotentialDrag(PointerEventData e)
            => ForwardToParent(e, ExecuteEvents.initializePotentialDrag);

        void IBeginDragHandler.OnBeginDrag(PointerEventData e)
        {
            // No axis lock: the carousel always acts on the horizontal (X) component of ANY drag, so a
            // gesture that starts vertical and then goes horizontal still scrolls — no dead gestures from
            // a brittle first-frame direction guess. The whole drag is forwarded up too, so an outer
            // (e.g. vertical) ScrollList scrolls its own axis from the same gesture (it ignores our X;
            // we ignore its Y).
            if (_handle.IsActive()) _handle.TryCancel();
            _animating = false;
            _dragging = true;
            _dragStartScroll = _scroll;
            _dragAccumX = 0f;
            _elapsed = 0f;
            ForwardToParent(e, ExecuteEvents.beginDragHandler);
        }

        void IDragHandler.OnDrag(PointerEventData e)
        {
            ForwardToParent(e, ExecuteEvents.dragHandler);   // outer ScrollRect scrolls its (orthogonal) axis
            if (_cards.Count == 0) return;
            _dragAccumX += e.delta.x;                         // only the X component drives the carousel
            // Clamp the drag to ±1 page: you can reveal at most the neighbour, never slide to a far
            // page and then snap back to the adjacent one. Clamping the accumulator (not just _scroll)
            // keeps reverse motion responsive — dragging back immediately moves off the boundary.
            _dragAccumX = Mathf.Clamp(_dragAccumX, -_pageWidth, _pageWidth);
            _scroll = _dragStartScroll - _dragAccumX / _pageWidth;   // 右拖(dx>0)显示上一张 → _scroll 减小
            Reposition();
        }

        void IEndDragHandler.OnEndDrag(PointerEventData e)
        {
            ForwardToParent(e, ExecuteEvents.endDragHandler);
            _dragging = false;
            int target = _current;
            if (_dragAccumX <= -_pageWidth * SnapThreshold) target = _current + 1;
            else if (_dragAccumX >= _pageWidth * SnapThreshold) target = _current - 1;
            GoTo(target, animated: true);
        }

        // 竖向为主的拖动不属于轮播 — 转发给父级让外层 ScrollRect/ScrollList 滚动
        // （Unity 把整条拖动序列交给最深的 IBeginDragHandler，空 return 会吞掉事件）。
        private void ForwardToParent<T>(PointerEventData e, ExecuteEvents.EventFunction<T> fn)
            where T : IEventSystemHandler
        {
            var parent = transform.parent;
            if (parent != null) ExecuteEvents.ExecuteHierarchy(parent.gameObject, e, fn);
        }

        protected override void OnDestroy()
        {
            if (_handle.IsActive()) _handle.TryCancel();
            DestroySlices();
            base.OnDestroy();
        }
    }
}
