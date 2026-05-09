using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;
using UnityToggle = UnityEngine.UI.Toggle;

namespace PromptUGUI.Controls
{
    public sealed class Toggle : Control
    {
        private UnityImage _bg;
        private UnityImage _checkmark;
        private UnityToggle _toggle;
        private TMP_Text _label;
        private string _fontType = "default";
        private string _groupName;
        private readonly Subject<bool> _changed = new();

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _toggle = GameObject.GetComponent<UnityToggle>() ?? GameObject.AddComponent<UnityToggle>();
            _toggle.targetGraphic = _bg;

            _checkmark = ProceduralBuilders.AddImage(RectTransform, "Checkmark", raycast: false);
            _toggle.graphic = _checkmark;

            _label = ProceduralBuilders.AddText(RectTransform, "Label");
            ApplyFont();

            _toggle.onValueChanged.AddListener(v => _changed.OnNext(v));
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        private void ApplyFont()
        {
            if (_label == null) return;
            var settings = PromptUGUI.Application.PromptUGUISettings.Instance;
            var locale = PromptUGUI.Application.UI.Locale.Current;
            var asset = settings?.ResolveFont(locale, _fontType);
            if (asset != null) _label.font = asset;
        }

        [UIAttr]
        public string Text
        {
            set
            {
                if (_label != null) _label.text = value ?? "";
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
                if (string.IsNullOrEmpty(value)) { _checkmark.sprite = null; return; }
                _checkmark.sprite = Resources.Load<Sprite>(value);
            }
        }

        [UIAttr]
        public bool IsOn
        {
            get => _toggle.isOn;
            set => _toggle.isOn = value;
        }

        [UIAttr]
        public string Group
        {
            set
            {
                _groupName = value;
                if (string.IsNullOrEmpty(value)) { _toggle.group = null; return; }
                var screen = PromptUGUI.Application.UI.OwnerScreenOf(this);
                _toggle.group = screen?.ToggleGroups.GetOrCreate(value);
            }
        }

        public Observable<bool> OnValueChanged => _changed;

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            _changed.Dispose();
            base.Dispose();
        }
    }
}
