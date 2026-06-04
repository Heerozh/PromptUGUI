using System;
using System.Collections.Generic;
using LitMotion;
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
        IBeginDragHandler, IDragHandler, IEndDragHandler
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

        private bool _staticCollected;
        private bool _bound;

        public Action<int> OnCurrent { get; set; }
        public RectTransform StripRect => _strip;
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
        public void ClearCards() { /* Task 5 */ }

        // —— 指示点 ——
        public void ConfigureDots(string anchor, Vector2 size, float spacing, string margin,
                                  Sprite sprite, Sprite selectedSprite, string tint,
                                  Color baseColor, string hover, string pressed, Color? selected)
        { /* Task 6 */ }
        public void RebuildIndicator() { /* Task 6 */ }
        private void RefreshDotSelection() { /* Task 6 */ }

        // —— 翻页 ——
        public void GoTo(int index, bool animated) { /* Task 3 */ }
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

        // —— Unity 生命周期（体在后续 Task）——
        private void Update() { /* Task 7 */ }
        protected override void OnRectTransformDimensionsChange() { /* Task 9 */ }
        void IBeginDragHandler.OnBeginDrag(PointerEventData e) { /* Task 8 */ }
        void IDragHandler.OnDrag(PointerEventData e) { /* Task 8 */ }
        void IEndDragHandler.OnEndDrag(PointerEventData e) { /* Task 8 */ }

        protected override void OnDestroy()
        {
            if (_handle.IsActive()) _handle.TryCancel();
            base.OnDestroy();
        }
    }
}
