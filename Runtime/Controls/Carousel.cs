using System;
using System.Collections.Generic;
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
    public sealed class Carousel : Control
    {
        private CarouselView _view;
        private RectTransform _strip;
        private readonly Subject<int> _currentChanged = new();

        // itemTemplate / BindItems
        private string _itemTemplate = "Frame";
        private Func<RectTransform, IControl> _factory;
        private IDisposable _itemsSub;

        // 指示点样式（attr 暂存，OnAfterApply 时下发给 _view）
        private string _dotsAnchor;
        private Vector2 _dotSize = new(8f, 8f);
        private float _dotSpacing = 6f;
        private string _dotMargin;
        private string _dotSprite;
        private string _dotSelectedSprite;
        private string _dotTint;
        private string _dotColor;
        private string _dotSelectedColor;
        private string _dotHoverColor;
        private string _dotPressedColor;

        public override Vector2? GetNativeSize() => new Vector2(200f, 120f);

        public override void OnAttached()
        {
            var viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
            // 透明 raycast catcher：保证视口任意位置都能起拖（拖动事件冒泡到 root 的 CarouselView）。
            var catcher = viewport.gameObject.AddComponent<UnityImage>();
            catcher.color = new Color(0f, 0f, 0f, 0f);
            catcher.raycastTarget = true;
            viewport.gameObject.AddComponent<RectMask2D>();

            _strip = ProceduralBuilders.AddChild(viewport, "Strip");
            var indicator = ProceduralBuilders.AddChild(RectTransform, "Indicator");

            _view = GameObject.AddComponent<CarouselView>();
            _view.Init(RectTransform, viewport, _strip, indicator);
            _view.OnCurrent = i => _currentChanged.OnNext(i);
        }

        // 静态 XML 子卡 + BindItems 建的卡都进 Strip。
        protected internal override Transform ChildHostTransform => _strip;

        [UIAttr, Preserve]
        public string ItemTemplate
        {
            set { _itemTemplate = string.IsNullOrEmpty(value) ? "Frame" : value; _factory = null; }
        }

        [UIAttr, Preserve]
        public bool Loop { set => _view.SetLoop(value); }

        [UIAttr, Preserve]
        public float Transition { set => _view.SetTransition(value); }

        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _view.SetStaticCards(Children);
            _view.RelayoutNow();
        }

        public int Count => _view.CardCount;

        [UIAttr, Preserve]
        public int Current
        {
            get => _view.CurrentIndex;
            set => _view.GoTo(value, animated: false);
        }

        internal override string PeekRuntimeState()
            => _view.CurrentIndex.ToString(System.Globalization.CultureInfo.InvariantCulture);

        public void GoTo(int index, bool animated = true) => _view.GoTo(index, animated);
        public void Next(bool animated = true) => _view.Next(animated);
        public void Previous(bool animated = true) => _view.Previous(animated);
        public Observable<int> OnCurrentChanged => _currentChanged;

        public override void Dispose()
        {
            _itemsSub?.Dispose();
            _currentChanged.Dispose();
            base.Dispose();
        }
    }
}
