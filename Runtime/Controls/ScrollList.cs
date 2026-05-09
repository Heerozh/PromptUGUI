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
    public sealed class ScrollList : Control
    {
        private UnityImage _bg;
        private ScrollRect _scroll;
        private RectTransform _content;
        private LayoutGroup _layoutGroup;
        private string _direction = "vertical";
        private string _itemTemplate;
        private float _spacing;
        private string _padding;
        private Func<RectTransform, IControl> _factory;
        private readonly List<IControl> _slots = new();

        public int SlotCount => _slots.Count;

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultContainerColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
            _scroll = GameObject.GetComponent<ScrollRect>() ?? GameObject.AddComponent<ScrollRect>();

            var viewport = ProceduralBuilders.AddChild(RectTransform, "Viewport");
            // Viewport: stencil Mask + alpha=1 sliced Image + showMaskGraphic=false (跟默认 prefab 一致；
            // alpha=1 关键，避免 4af322b 的 UI/Default shader alpha-discard)
            var viewportImg = viewport.gameObject.AddComponent<UnityImage>();
            viewportImg.color = UnityEngine.Color.white;  // alpha=1 关键
            ProceduralBuilders.ApplyDefaultSlicedSprite(viewportImg);
            var viewportMask = viewport.gameObject.AddComponent<Mask>();
            viewportMask.showMaskGraphic = false;
            _scroll.viewport = viewport;

            _content = ProceduralBuilders.AddChild(viewport, "Content");
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
            if (_layoutGroup != null)
            {
                if (UnityEngine.Application.isPlaying)
                    UnityEngine.Object.Destroy(_layoutGroup);
                else
                    UnityEngine.Object.DestroyImmediate(_layoutGroup);
                _layoutGroup = null;
            }
            var fitter = _content.GetComponent<ContentSizeFitter>()
                         ?? _content.gameObject.AddComponent<ContentSizeFitter>();

            if (_direction == "horizontal")
            {
                _scroll.horizontal = true;
                _scroll.vertical = false;
                // 左侧锚点：竖向铺满 viewport，水平方向由 ContentSizeFitter 撑开
                _content.anchorMin = new Vector2(0f, 0f);
                _content.anchorMax = new Vector2(0f, 1f);
                _content.pivot = new Vector2(0f, 0.5f);
                _content.sizeDelta = Vector2.zero;
                _content.anchoredPosition = Vector2.zero;
                _layoutGroup = _content.gameObject.AddComponent<HorizontalLayoutGroup>();
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
                _layoutGroup = _content.gameObject.AddComponent<VerticalLayoutGroup>();
                fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
                fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            }
            ApplySpacingPadding();
        }

        private void ApplySpacingPadding()
        {
            switch (_layoutGroup)
            {
                case HorizontalLayoutGroup h: h.spacing = _spacing; break;
                case VerticalLayoutGroup v: v.spacing = _spacing; break;
            }
            // padding 字符串: "X" | "V,H" | "T,R,B,L"
            if (string.IsNullOrEmpty(_padding) || _layoutGroup == null) return;
            var parts = _padding.Split(',');
            int t = 0, r = 0, b = 0, l = 0;
            switch (parts.Length)
            {
                case 1: int.TryParse(parts[0], out t); r = b = l = t; break;
                case 2:
                    int.TryParse(parts[0], out t); b = t;
                    int.TryParse(parts[1], out r); l = r; break;
                case 4:
                    int.TryParse(parts[0], out t);
                    int.TryParse(parts[1], out r);
                    int.TryParse(parts[2], out b);
                    int.TryParse(parts[3], out l); break;
            }
            _layoutGroup.padding = new RectOffset(l, r, t, b);
        }

        [UIAttr]
        public string ItemTemplate
        {
            set
            {
                _itemTemplate = value;
                _factory = ResolveFactory(value);
            }
        }

        [UIAttr]
        public string Direction
        {
            set { _direction = string.IsNullOrEmpty(value) ? "vertical" : value; ApplyDirection(); }
        }

        [UIAttr]
        public float Spacing { set { _spacing = value; ApplySpacingPadding(); } }

        [UIAttr]
        public string Padding { set { _padding = value; ApplySpacingPadding(); } }

        [UIAttr]
        public string Color
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c)) _bg.color = c;
            }
        }

        [UIAttr]
        public string Sprite
        {
            set
            {
                if (string.IsNullOrEmpty(value)) { _bg.sprite = null; return; }
                _bg.sprite = Resources.Load<Sprite>(value);
            }
        }

        private Func<RectTransform, IControl> ResolveFactory(string tag)
        {
            if (string.IsNullOrEmpty(tag)) return null;
            var owner = PromptUGUI.Application.UI.OwnerScreenOf(this);
            // 1) Template
            if (owner?.Def?.Templates != null && owner.Def.Templates.TryGetValue(tag, out var tpl))
            {
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

        public override void Dispose()
        {
            ClearSlots();
            base.Dispose();
        }
    }
}
