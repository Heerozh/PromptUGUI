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

        [UIAttr, Preserve] public string Dots { set => _dotsAnchor = value; }
        [UIAttr, Preserve]
        public string DotSize
        {
            set
            {
                var parts = (value ?? "").Split('x');
                if (parts.Length == 2
                    && float.TryParse(parts[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var w)
                    && float.TryParse(parts[1], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var h))
                    _dotSize = new Vector2(w, h);
                else
                {
                    _dotSize = new Vector2(8f, 8f);
                    if (!string.IsNullOrEmpty(value))
                        Debug.LogWarning($"<Carousel dotSize='{value}'> not 'WxH'; using 8x8.");
                }
            }
        }
        [UIAttr, Preserve] public float DotSpacing { set => _dotSpacing = value; }
        [UIAttr, Preserve] public string DotMargin { set => _dotMargin = value; }
        [UIAttr(IsSprite = true), Preserve] public string DotSprite { set => _dotSprite = value; }
        [UIAttr(IsSprite = true), Preserve] public string DotSelectedSprite { set => _dotSelectedSprite = value; }
        [UIAttr(IsColor = true), Preserve] public string DotColor { set => _dotColor = value; }
        [UIAttr(IsColor = true), Preserve] public string DotSelectedColor { set => _dotSelectedColor = value; }
        [UIAttr(IsColor = true), Preserve] public string DotHoverColor { set => _dotHoverColor = value; }
        [UIAttr(IsColor = true), Preserve] public string DotPressedColor { set => _dotPressedColor = value; }
        [UIAttr, Preserve] public string DotTint { set => _dotTint = value; }

        [UIAttr, Preserve] public float Interval { set => _view.SetInterval(value); }
        public bool Playing { get => _view.Playing; set => _view.Playing = value; }

        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            _view.SetStaticCards(Children);
            _view.ConfigureDots(
                _dotsAnchor, _dotSize, _dotSpacing, _dotMargin,
                string.IsNullOrEmpty(_dotSprite) ? null : UI.ResolveSprite(_dotSprite),
                string.IsNullOrEmpty(_dotSelectedSprite) ? null : UI.ResolveSprite(_dotSelectedSprite),
                _dotTint,
                string.IsNullOrWhiteSpace(_dotColor) ? Color.white : UI.Theme.Resolve(_dotColor),
                _dotHoverColor, _dotPressedColor,
                string.IsNullOrWhiteSpace(_dotSelectedColor) ? (Color?)null : UI.Theme.Resolve(_dotSelectedColor));
            _view.RebuildIndicator();
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

        public IDisposable BindItems<T>(
            Observable<IReadOnlyList<T>> source,
            Action<IControl, T> bind)
            => BindItems<T, IControl>(source, bind);

        public IDisposable BindItems<T, TSlot>(
            Observable<IReadOnlyList<T>> source,
            Action<TSlot, T> bind) where TSlot : class, IControl
        {
            _itemsSub?.Dispose();
            _itemsSub = source.Subscribe(items => Rebuild(items, bind));
            return _itemsSub;
        }

        private void Rebuild<T, TSlot>(IReadOnlyList<T> items, Action<TSlot, T> bind)
            where TSlot : class, IControl
        {
            if (_factory == null) _factory = ResolveFactory(_itemTemplate);
            _view.ClearCards();
            for (int i = 0; i < items.Count; i++)
            {
                var node = _factory(_strip);
                if (node is TSlot typed) bind(typed, items[i]);
                else throw new InvalidCastException(
                    $"itemTemplate='{_itemTemplate}' instantiated {node.GetType().Name}, " +
                    $"but BindItems expected {typeof(TSlot).Name}");
                _view.AddCard(node);
            }
            _view.OnItemsRebuilt();
        }

        private Func<RectTransform, IControl> ResolveFactory(string tag)
        {
            var owner = UI.OwnerScreenOf(this);
            if (owner?.Def?.Templates != null && owner.Def.Templates.TryGetValue(tag, out var tpl))
            {
                return parent =>
                {
                    var instantiator = UI.GetInstantiator();
                    return instantiator.InstantiateNode(tpl.Body, parent, owner);
                };
            }
            if (UI.Registry.Has(tag))
            {
                return parent =>
                {
                    var instantiator = UI.GetInstantiator();
                    var node = new ElementNode(tag);
                    return instantiator.InstantiateNode(node, parent, owner);
                };
            }
            throw new ParseException(
                $"<Carousel itemTemplate='{tag}'>: tag is neither a registered Control nor a Template");
        }

        public override void Dispose()
        {
            _itemsSub?.Dispose();
            // Dynamically-bound cards aren't in Screen._nodeMap, so dispose them explicitly
            // (matches ScrollList.Dispose -> ClearSlots); static cards double-dispose safely.
            _view.ClearCards();
            _currentChanged.Dispose();
            base.Dispose();
        }
    }
}
