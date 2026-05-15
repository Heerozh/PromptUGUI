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

        [UIAttr("text")]
        public string TextValue
        {
            set => _tmp.text = value ?? "";
        }

        [UIAttr("fontSize")]
        public int Size
        {
            set => _tmp.fontSize = value;
        }

        [UIAttr]
        public string Color
        {
            set
            {
                if (ColorUtility.TryParseHtmlString(value, out var c))
                    _tmp.color = c;
            }
        }

        [UIAttr]
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

        [UIAttr]
        public bool Wrap
        {
            set => _tmp.textWrappingMode = value ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        }

        [UIAttr]
        public bool RaycastTarget
        {
            set => _tmp.raycastTarget = value;
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
    }
}
