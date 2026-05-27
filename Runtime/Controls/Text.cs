using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Controls
{
    public sealed class Text : Control
    {
        private TMP_Text _tmp;
        private string _fontType = "default";
        private bool _autosize;

        internal TMP_Text TmpComponent => _tmp;

        public override void OnAttached()
        {
            _tmp = GameObject.GetComponent<TMP_Text>();
            if (_tmp == null)
            {
                _tmp = GameObject.AddComponent<TextMeshProUGUI>();
                _tmp.color = ProceduralBuilders.DefaultLabelColor;
            }
            ApplyFont();
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            base.Dispose();
        }

        private void ApplyFont()
        {
            if (_tmp == null) return;
            var settings = PromptUGUI.Application.PromptUGUISettings.Instance;
            var locale = PromptUGUI.Application.UI.Locale.Current;
            var asset = settings?.ResolveFont(locale, _fontType);
            if (asset != null) _tmp.font = asset;
        }

        [UIAttr("text"), Preserve]
        public string TextValue
        {
            set => _tmp.text = value ?? "";
        }

        internal override string PeekDefaultText() => _tmp != null ? _tmp.text : null;

        [UIAttr("fontSize"), Preserve]
        public int Size
        {
            set
            {
                _tmp.fontSize = value;
                if (_autosize) ApplyAutosize();
            }
        }

        [UIAttr, Preserve]
        public bool Autosize
        {
            set
            {
                _autosize = value;
                ApplyAutosize();
            }
        }

        private void ApplyAutosize()
        {
            if (_tmp == null) return;
            if (_autosize)
            {
                var size = _tmp.fontSize;
                _tmp.fontSizeMin = size;
                _tmp.fontSizeMax = size;
                _tmp.characterWidthAdjustment = 50f;
                _tmp.enableAutoSizing = true;
            }
            else
            {
                _tmp.enableAutoSizing = false;
            }
        }

        [UIAttr, Preserve]
        public string Color
        {
            set
            {
                if (ColorUtility.TryParseHtmlString(value, out var c))
                    _tmp.color = c;
            }
        }

        [UIAttr, Preserve]
        public string Align
        {
            set
            {
                _tmp.alignment = value switch
                {
                    "center" => TextAlignmentOptions.Center,
                    "right" => TextAlignmentOptions.Right,
                    _ => TextAlignmentOptions.Left,
                };
            }
        }

        [UIAttr, Preserve]
        public bool Wrap
        {
            set => _tmp.textWrappingMode = value ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        }

        [UIAttr, Preserve]
        public bool RaycastTarget
        {
            set => _tmp.raycastTarget = value;
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

        public override Vector2? GetNativeSize()
        {
            if (_tmp == null || string.IsNullOrEmpty(_tmp.text)) return null;
            _tmp.ForceMeshUpdate();
            return new Vector2(_tmp.preferredWidth, _tmp.preferredHeight);
        }
    }
}
