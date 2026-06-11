using System;
using System.Collections.Generic;
using System.Linq;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class Dropdown : Control
    {
        private UnityImage _bg;
        private UnityImage _templateBg;
        private RectTransform _popupViewport;
        private TMP_Dropdown _tmp;
        private string _fontType = "default";
        private readonly Subject<int> _selected = new();

        // DSS-D2: Dropdown 不读 caption 来算 native（caption 会随用户选项变化，UX 会跳）。
        // 固定默认覆盖"作者忘写 size"的可见性问题；显式 size 仍胜出。
        private const float MinTapHeight = 44f;
        private const float DefaultDropdownWidth = 160f;

        public override Vector2? GetNativeSize()
            => new Vector2(DefaultDropdownWidth, MinTapHeight);

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultControlBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
            _tmp = GameObject.AddComponent<TMP_Dropdown>();
            _tmp.targetGraphic = _bg;

            // Caption (the always-visible label on the closed dropdown button).
            var label = ProceduralBuilders.AddText(RectTransform, "Label");
            label.alignment = TextAlignmentOptions.Left;
            label.rectTransform.anchorMin = new Vector2(0f, 0f);
            label.rectTransform.anchorMax = new Vector2(1f, 1f);
            label.rectTransform.offsetMin = new Vector2(10f, 6f);
            label.rectTransform.offsetMax = new Vector2(-25f, -7f);
            _tmp.captionText = label;

            // Caret on the right edge.
            var arrow = ProceduralBuilders.AddImage(RectTransform, "Arrow", raycast: false);
            arrow.color = ProceduralBuilders.DefaultGlyphColor;
            ProceduralBuilders.ApplyDefaultSimpleSprite(arrow, ProceduralBuilders.SpriteCaret);
            arrow.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            arrow.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
            arrow.rectTransform.sizeDelta = new Vector2(20f, 20f);
            arrow.rectTransform.anchoredPosition = new Vector2(-15f, 0f);

            // Template (popup root, anchored to dropdown's bottom edge so it grows downward).
            var template = ProceduralBuilders.AddChild(RectTransform, "Template");
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.sizeDelta = new Vector2(0f, 150f);
            template.anchoredPosition = new Vector2(0f, 2f);
            template.gameObject.SetActive(false);
            _templateBg = template.gameObject.AddComponent<UnityImage>();
            _templateBg.color = ProceduralBuilders.DefaultPopupBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_templateBg);
            var templateScroll = template.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            templateScroll.horizontal = false;
            templateScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;

            // Viewport: popup viewport mask 三态 + 默认 pugui_9slice_round，spec §2.3。
            // CRITICAL: alpha 必须为 1。alpha=0.01 会触发 UI/Default shader 的 alpha-discard，
            // 把 stencil 写飞 (4af322b 之前的 bug)。
            _popupViewport = ProceduralBuilders.AddChild(template, "Viewport");
            _popupViewport.anchorMin = new Vector2(0f, 0f);
            _popupViewport.anchorMax = new Vector2(1f, 1f);
            _popupViewport.pivot = new Vector2(0f, 1f);
            _popupViewport.offsetMin = Vector2.zero;
            _popupViewport.offsetMax = Vector2.zero;
            _popupViewport.sizeDelta = new Vector2(-18f, 0f);  // 留 18px 给 Vertical Scrollbar
            ProceduralBuilders.ApplyViewportMask(_popupViewport, null, ProceduralBuilders.SpriteRoundedRect);

            // Content (top-anchored; height grows to fit items via TMP_Dropdown's runtime sizing).
            var content = ProceduralBuilders.AddChild(_popupViewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 28f);
            content.anchoredPosition = Vector2.zero;

            // Item template (cloned per option; fixed height + horizontal stretch).
            const float itemHeight = 20f;
            var item = ProceduralBuilders.AddChild(content, "Item");
            item.anchorMin = new Vector2(0f, 0.5f);
            item.anchorMax = new Vector2(1f, 0.5f);
            item.pivot = new Vector2(0.5f, 0.5f);
            item.sizeDelta = new Vector2(0f, itemHeight);

            // Item Background: 独立子节点，simple + 无 sprite + #F5F5F5 (highlighted-tinted 色带)
            var itemBgRt = ProceduralBuilders.AddChild(item, "Item Background");
            var itemBg = itemBgRt.gameObject.AddComponent<UnityImage>();
            itemBg.type = UnityImage.Type.Simple;
            itemBg.sprite = null;
            itemBg.color = new UnityEngine.Color(0.961f, 0.961f, 0.961f, 1f);
            var itemToggle = item.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            itemToggle.targetGraphic = itemBg;

            // Item checkmark anchored on the left side of the item.
            var itemCheckmark = ProceduralBuilders.AddImage(item, "Item Checkmark", raycast: false);
            itemCheckmark.color = ProceduralBuilders.DefaultGlyphColor;
            ProceduralBuilders.ApplyDefaultSimpleSprite(itemCheckmark, ProceduralBuilders.SpriteCheckmark);
            itemCheckmark.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            itemCheckmark.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            itemCheckmark.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            itemCheckmark.rectTransform.sizeDelta = new Vector2(20f, 20f);
            itemCheckmark.rectTransform.anchoredPosition = new Vector2(10f, 0f);
            itemToggle.graphic = itemCheckmark;

            // Item label fills the rest of the item.
            var itemLabel = ProceduralBuilders.AddText(item, "Item Label");
            itemLabel.alignment = TextAlignmentOptions.Left;
            itemLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            itemLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            itemLabel.rectTransform.offsetMin = new Vector2(20f, 1.5f);
            itemLabel.rectTransform.offsetMax = new Vector2(-10f, -1.5f);

            // Scrollbar Vertical (default prefab 在 Template 内有这个子树)
            var scrollbarRt = ProceduralBuilders.AddChild(template, "Scrollbar");
            scrollbarRt.anchorMin = new Vector2(1f, 0f);
            scrollbarRt.anchorMax = new Vector2(1f, 1f);
            scrollbarRt.pivot = new Vector2(1f, 1f);
            scrollbarRt.sizeDelta = new Vector2(20f, 0f);
            scrollbarRt.anchoredPosition = Vector2.zero;
            var scrollbarBg = scrollbarRt.gameObject.AddComponent<UnityImage>();
            scrollbarBg.color = ProceduralBuilders.DefaultControlBgColor; // white
            ProceduralBuilders.ApplyDefaultSlicedSprite(scrollbarBg);
            var scrollbar = scrollbarRt.gameObject.AddComponent<UnityEngine.UI.Scrollbar>();
            scrollbar.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            scrollbar.value = 0f;
            scrollbar.size = 0.2f;

            var slidingArea = ProceduralBuilders.AddChild(scrollbarRt, "Sliding Area");
            slidingArea.sizeDelta = new Vector2(-20f, -20f);

            var sbHandle = ProceduralBuilders.AddImage(slidingArea, "Handle");
            sbHandle.color = UnityEngine.Color.white;
            ProceduralBuilders.ApplyDefaultSlicedSprite(sbHandle);
            sbHandle.rectTransform.anchorMin = new Vector2(0f, 0f);
            sbHandle.rectTransform.anchorMax = new Vector2(1f, 0.2f);
            sbHandle.rectTransform.sizeDelta = new Vector2(20f, 20f);
            sbHandle.rectTransform.anchoredPosition = Vector2.zero;
            scrollbar.targetGraphic = sbHandle;
            scrollbar.handleRect = sbHandle.rectTransform;

            templateScroll.verticalScrollbar = scrollbar;
            templateScroll.verticalScrollbarVisibility = UnityEngine.UI.ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            templateScroll.verticalScrollbarSpacing = -3f;

            templateScroll.viewport = _popupViewport;
            templateScroll.content = content;

            _tmp.template = template;
            _tmp.itemText = itemLabel;

            _tmp.onValueChanged.AddListener(i => _selected.OnNext(i));
            ApplyFont();
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        private void ApplyFont()
        {
            if (_tmp == null) return;
            FontApplier.Apply(_tmp.captionText, _fontType);
            FontApplier.Apply(_tmp.itemText, _fontType);
        }

        [UIAttr, Preserve]
        public int Value
        {
            get => _tmp.value;
            set => _tmp.value = value;
        }

        internal override string PeekRuntimeState() =>
            _tmp != null ? _tmp.value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set => _bg.color = UI.Theme.Resolve(value);
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
        public string PopupSprite
        {
            set
            {
                _templateBg.sprite = UI.ResolveSprite(value);
                ProceduralBuilders.AutoSlice(_templateBg);
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string PopupColor
        {
            set => _templateBg.color = UI.Theme.Resolve(value);
        }

        [UIAttr(IsSprite = true), Preserve]
        public string PopupMask
        {
            set => ProceduralBuilders.ApplyViewportMask(
                _popupViewport, value, ProceduralBuilders.SpriteRoundedRect);
        }

        [UIAttr, Preserve]
        public string Font
        {
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                ApplyFont();
            }
        }

        public Observable<int> OnSelected => _selected;

        public IDisposable BindOptions(Observable<IEnumerable<string>> source) =>
            source.Subscribe(seq => SetOptions(seq.Select(s => new DropdownOption(s)).ToList()));

        public IDisposable BindOptions(Observable<IEnumerable<DropdownOption>> source) =>
            source.Subscribe(seq => SetOptions(seq.ToList()));

        private void SetOptions(List<DropdownOption> opts)
        {
            var wasOpen = _tmp.IsExpanded;
            if (wasOpen) _tmp.Hide();

            _tmp.options.Clear();
            foreach (var o in opts)
            {
                var od = new TMP_Dropdown.OptionData { text = o.Text ?? "", image = o.Icon };
                _tmp.options.Add(od);
            }
            _tmp.RefreshShownValue();

            if (wasOpen) _tmp.Show();
        }

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            _selected.Dispose();
            base.Dispose();
        }
    }
}
