using System;
using System.Collections.Generic;
using System.Linq;
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
        private TMP_Dropdown _tmp;
        private string _fontType = "default";
        private readonly Subject<int> _selected = new();

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
            arrow.rectTransform.sizeDelta = new Vector2(14f, 10f);
            arrow.rectTransform.anchoredPosition = new Vector2(-12f, 0f);

            // Template (popup root, anchored to dropdown's bottom edge so it grows downward).
            var template = ProceduralBuilders.AddChild(RectTransform, "Template");
            template.anchorMin = new Vector2(0f, 0f);
            template.anchorMax = new Vector2(1f, 0f);
            template.pivot = new Vector2(0.5f, 1f);
            template.sizeDelta = new Vector2(0f, 150f);
            template.anchoredPosition = new Vector2(0f, 2f);
            template.gameObject.SetActive(false);
            var templateBg = template.gameObject.AddComponent<UnityImage>();
            templateBg.color = ProceduralBuilders.DefaultPopupBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(templateBg);
            var templateScroll = template.gameObject.AddComponent<UnityEngine.UI.ScrollRect>();
            templateScroll.horizontal = false;
            templateScroll.movementType = UnityEngine.UI.ScrollRect.MovementType.Clamped;

            // Viewport (full-fills the template; mask the items so they don't bleed past the popup).
            var viewport = ProceduralBuilders.AddChild(template, "Viewport");
            viewport.anchorMin = new Vector2(0f, 0f);
            viewport.anchorMax = new Vector2(1f, 1f);
            viewport.pivot = new Vector2(0f, 1f);
            viewport.offsetMin = Vector2.zero;
            viewport.offsetMax = Vector2.zero;
            var viewportImg = viewport.gameObject.AddComponent<UnityImage>();
            viewportImg.color = new UnityEngine.Color(1f, 1f, 1f, 0.01f); // mask requires a graphic
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>().showMaskGraphic = false;

            // Content (top-anchored; height grows to fit items via TMP_Dropdown's runtime sizing).
            var content = ProceduralBuilders.AddChild(viewport, "Content");
            content.anchorMin = new Vector2(0f, 1f);
            content.anchorMax = new Vector2(1f, 1f);
            content.pivot = new Vector2(0.5f, 1f);
            content.sizeDelta = new Vector2(0f, 28f);
            content.anchoredPosition = Vector2.zero;

            // Item template (cloned per option; fixed height + horizontal stretch).
            const float itemHeight = 24f;
            var item = ProceduralBuilders.AddChild(content, "Item");
            item.anchorMin = new Vector2(0f, 0.5f);
            item.anchorMax = new Vector2(1f, 0.5f);
            item.pivot = new Vector2(0.5f, 0.5f);
            item.sizeDelta = new Vector2(0f, itemHeight);

            var itemBg = item.gameObject.AddComponent<UnityImage>();
            itemBg.color = ProceduralBuilders.DefaultControlBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(itemBg);
            var itemToggle = item.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            itemToggle.targetGraphic = itemBg;

            // Item checkmark anchored on the left side of the item.
            var itemCheckmark = ProceduralBuilders.AddImage(item, "Item Checkmark", raycast: false);
            itemCheckmark.color = ProceduralBuilders.DefaultGlyphColor;
            ProceduralBuilders.ApplyDefaultSimpleSprite(itemCheckmark, ProceduralBuilders.SpriteCheckmark);
            itemCheckmark.rectTransform.anchorMin = new Vector2(0f, 0.5f);
            itemCheckmark.rectTransform.anchorMax = new Vector2(0f, 0.5f);
            itemCheckmark.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            itemCheckmark.rectTransform.sizeDelta = new Vector2(14f, 14f);
            itemCheckmark.rectTransform.anchoredPosition = new Vector2(12f, 0f);
            itemToggle.graphic = itemCheckmark;

            // Item label fills the rest of the item.
            var itemLabel = ProceduralBuilders.AddText(item, "Item Label");
            itemLabel.alignment = TextAlignmentOptions.Left;
            itemLabel.rectTransform.anchorMin = new Vector2(0f, 0f);
            itemLabel.rectTransform.anchorMax = new Vector2(1f, 1f);
            itemLabel.rectTransform.offsetMin = new Vector2(24f, 0f);
            itemLabel.rectTransform.offsetMax = new Vector2(-10f, 0f);

            templateScroll.viewport = viewport;
            templateScroll.content = content;

            _tmp.template = template;
            _tmp.itemText = itemLabel;

            _tmp.onValueChanged.AddListener(i => _selected.OnNext(i));
            ApplyFont();
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        private void ApplyFont()
        {
            if (_tmp?.captionText == null) return;
            var settings = PromptUGUI.Application.PromptUGUISettings.Instance;
            var locale = PromptUGUI.Application.UI.Locale.Current;
            var asset = settings?.ResolveFont(locale, _fontType);
            if (asset != null)
            {
                _tmp.captionText.font = asset;
                if (_tmp.itemText != null) _tmp.itemText.font = asset;
            }
        }

        [UIAttr]
        public int Value
        {
            get => _tmp.value;
            set => _tmp.value = value;
        }

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

        [UIAttr]
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
