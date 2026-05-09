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

            // Construct minimum required children for TMP_Dropdown:
            // Label + Arrow + Template (Viewport > Content > Item).
            var label = ProceduralBuilders.AddText(RectTransform, "Label");
            _tmp.captionText = label;

            var arrow = ProceduralBuilders.AddImage(RectTransform, "Arrow", raycast: false);
            arrow.color = ProceduralBuilders.DefaultGlyphColor;
            ProceduralBuilders.ApplyDefaultSimpleSprite(arrow, ProceduralBuilders.SpriteCaret);
            arrow.rectTransform.anchorMin = new Vector2(1f, 0.5f);
            arrow.rectTransform.anchorMax = new Vector2(1f, 0.5f);
            arrow.rectTransform.pivot = new Vector2(1f, 0.5f);
            arrow.rectTransform.sizeDelta = new Vector2(14f, 10f);
            arrow.rectTransform.anchoredPosition = new Vector2(-12f, 0f);

            var template = ProceduralBuilders.AddChild(RectTransform, "Template");
            template.gameObject.SetActive(false);
            var templateBg = template.gameObject.AddComponent<UnityImage>();
            templateBg.color = ProceduralBuilders.DefaultPopupBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(templateBg);
            var viewport = ProceduralBuilders.AddChild(template, "Viewport");
            viewport.gameObject.AddComponent<UnityEngine.UI.Mask>();
            viewport.gameObject.AddComponent<UnityImage>();
            var content = ProceduralBuilders.AddChild(viewport, "Content");
            var item = ProceduralBuilders.AddChild(content, "Item");
            var itemBg = item.gameObject.AddComponent<UnityImage>();
            itemBg.color = ProceduralBuilders.DefaultControlBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(itemBg);
            var itemToggle = item.gameObject.AddComponent<UnityEngine.UI.Toggle>();
            itemToggle.targetGraphic = itemBg;
            var itemCheckmark = ProceduralBuilders.AddImage(item, "Item Checkmark", raycast: false);
            itemCheckmark.color = ProceduralBuilders.DefaultGlyphColor;
            ProceduralBuilders.ApplyDefaultSimpleSprite(itemCheckmark, ProceduralBuilders.SpriteCheckmark);
            itemToggle.graphic = itemCheckmark;
            var itemLabel = ProceduralBuilders.AddText(item, "Item Label");
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
