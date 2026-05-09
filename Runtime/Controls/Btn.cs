using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class Btn : Control
    {
        private UnityImage _bg;
        private Button _btn;
        private TMP_Text _autoLabel;
        private string _fontType = "default";
        private readonly Subject<Unit> _click = new();

        public override void OnAttached()
        {
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = PromptUGUI.Controls.Internal.ProceduralBuilders.DefaultBtnColor;
            PromptUGUI.Controls.Internal.ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
            _btn = GameObject.GetComponent<Button>() ?? GameObject.AddComponent<Button>();
            _btn.targetGraphic = _bg;
            _btn.onClick.AddListener(() => _click.OnNext(Unit.Default));
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        private TMP_Text EnsureLabel()
        {
            if (_autoLabel != null) return _autoLabel;
            var go = new GameObject("Label", typeof(RectTransform));
            go.transform.SetParent(GameObject.transform, worldPositionStays: false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            _autoLabel = go.AddComponent<TextMeshProUGUI>();
            _autoLabel.alignment = TextAlignmentOptions.Center;
            _autoLabel.raycastTarget = false;
            ApplyFont();
            return _autoLabel;
        }

        private void ApplyFont()
        {
            if (_autoLabel == null) return;
            var settings = PromptUGUI.Application.PromptUGUISettings.Instance;
            var locale = PromptUGUI.Application.UI.Locale.Current;
            var asset = settings?.ResolveFont(locale, _fontType);
            if (asset != null) _autoLabel.font = asset;
        }

        [UIAttr]
        public string Text
        {
            set
            {
                if (string.IsNullOrEmpty(value) && _autoLabel == null) return;
                EnsureLabel().text = value ?? "";
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
                if (string.IsNullOrEmpty(value)) { _bg.sprite = null; return; }
                _bg.sprite = Resources.Load<Sprite>(value);
            }
        }

        public Observable<Unit> OnClick => _click;

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            _click.Dispose();
            base.Dispose();
        }
    }
}
