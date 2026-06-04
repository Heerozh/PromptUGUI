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
        public void SetStaticCards(IReadOnlyList<IControl> children) { /* Task 2 */ }
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
        private void Reposition() { /* Task 3 */ }
        public void RelayoutNow() { /* Task 2/3 */ }
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
