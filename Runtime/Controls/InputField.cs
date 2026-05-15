using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class InputField : Control
    {
        private UnityImage _bg;
        private TMP_InputField _input;
        private TMP_Text _placeholder;
        private TMP_Text _text;
        private string _fontType = "default";
        private readonly Subject<string> _changed = new();
        private readonly Subject<string> _endEdit = new();
        private readonly Subject<string> _submit = new();

        public Observable<string> OnValueChanged => _changed;
        public Observable<string> OnEndEdit => _endEdit;
        public Observable<string> OnSubmit => _submit;

        public override void OnAttached()
        {
            // Root: sliced bg + TMP_InputField
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultControlBgColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);

            // Text Area: 跟 default prefab 一致 (sizeDelta=-20,-13, pos=(0,-0.5), RectMask2D padding=(-8,-5,-8,-5))
            var textAreaRt = ProceduralBuilders.AddChild(RectTransform, "Text Area");
            textAreaRt.anchorMin = new Vector2(0f, 0f);
            textAreaRt.anchorMax = new Vector2(1f, 1f);
            textAreaRt.offsetMin = Vector2.zero;
            textAreaRt.offsetMax = Vector2.zero;
            textAreaRt.sizeDelta = new Vector2(-20f, -13f);
            textAreaRt.anchoredPosition = new Vector2(0f, -0.5f);
            var textAreaMask = textAreaRt.gameObject.AddComponent<RectMask2D>();
            textAreaMask.padding = new Vector4(-8f, -5f, -8f, -5f);

            _input = GameObject.AddComponent<TMP_InputField>();
            _input.targetGraphic = _bg;
            _input.textViewport = textAreaRt;

            // Placeholder：italic + 半透明 + IgnoreLayout (默认 prefab Placeholder 节点)
            _placeholder = ProceduralBuilders.AddText(textAreaRt, "Placeholder");
            _placeholder.alignment = TextAlignmentOptions.TopLeft;
            _placeholder.fontStyle = FontStyles.Italic;
            _placeholder.color = ProceduralBuilders.DefaultPlaceholderColor;
            _placeholder.text = "Enter text...";
            _placeholder.textWrappingMode = TextWrappingModes.NoWrap;
            var phLE = _placeholder.gameObject.AddComponent<LayoutElement>();
            phLE.ignoreLayout = true;

            // Text：用户输入显示组件
            _text = ProceduralBuilders.AddText(textAreaRt, "Text");
            _text.alignment = TextAlignmentOptions.TopLeft;
            _text.color = ProceduralBuilders.DefaultLabelColor;
            _text.text = string.Empty;

            _input.textComponent = _text;
            _input.placeholder = _placeholder;
            _input.caretColor = ProceduralBuilders.DefaultGlyphColor;
            _input.customCaretColor = false;
            _input.selectionColor = new Color(0.659f, 0.808f, 1f, 0.753f);
            _input.onValueChanged.AddListener(v => _changed.OnNext(v));
            _input.onEndEdit.AddListener(v => _endEdit.OnNext(v));
            _input.onSubmit.AddListener(v => _submit.OnNext(v));

            // AddComponent<TMP_InputField> 在 active GO 上立刻触发 OnEnable, 那一刻
            // textComponent/placeholder/textViewport 都还是 null, RegisterDirtyVerticesCallback
            // 被跳过 → caret 顶点没人触发 redraw, 永远不显示。再触一次 OnEnable cycle 才能
            // 把 MarkGeometryAsDirty/UpdateLabel 绑到现在已经 wire 好的 textComponent 上。
            _input.enabled = false;
            _input.enabled = true;

            ApplyFont();
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        private void ApplyFont()
        {
            if (_text == null) return;
            var settings = PromptUGUI.Application.PromptUGUISettings.Instance;
            var locale = PromptUGUI.Application.UI.Locale.Current;
            var asset = settings?.ResolveFont(locale, _fontType);
            if (asset == null) return;
            _text.font = asset;
            if (_placeholder != null) _placeholder.font = asset;
        }

        [UIAttr("text")]
        public string TextValue
        {
            set => _input.text = value ?? string.Empty;
        }

        [UIAttr]
        public string Placeholder
        {
            set
            {
                if (_placeholder != null) _placeholder.text = value ?? string.Empty;
            }
        }

        [UIAttr]
        public string ContentType
        {
            set
            {
                _input.contentType = value switch
                {
                    "standard" => TMP_InputField.ContentType.Standard,
                    "autocorrected" => TMP_InputField.ContentType.Autocorrected,
                    "integer-number" => TMP_InputField.ContentType.IntegerNumber,
                    "decimal-number" => TMP_InputField.ContentType.DecimalNumber,
                    "alphanumeric" => TMP_InputField.ContentType.Alphanumeric,
                    "name" => TMP_InputField.ContentType.Name,
                    "email" => TMP_InputField.ContentType.EmailAddress,
                    "password" => TMP_InputField.ContentType.Password,
                    "pin" => TMP_InputField.ContentType.Pin,
                    "custom" => TMP_InputField.ContentType.Custom,
                    _ => throw new System.ArgumentException(
                        $"InputField.contentType='{value}' invalid; expected standard|autocorrected|integer-number|decimal-number|alphanumeric|name|email|password|pin|custom"),
                };
            }
        }

        [UIAttr]
        public string LineType
        {
            set
            {
                _input.lineType = value switch
                {
                    "single" => TMP_InputField.LineType.SingleLine,
                    "multi-newline" => TMP_InputField.LineType.MultiLineNewline,
                    "multi-submit" => TMP_InputField.LineType.MultiLineSubmit,
                    _ => throw new System.ArgumentException(
                        $"InputField.lineType='{value}' invalid; expected single|multi-newline|multi-submit"),
                };
            }
        }

        [UIAttr]
        public int CharacterLimit
        {
            set => _input.characterLimit = value;
        }

        [UIAttr]
        public bool ReadOnly
        {
            set => _input.readOnly = value;
        }

        [UIAttr]
        public string Color
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (UnityEngine.ColorUtility.TryParseHtmlString(value, out var c)) _bg.color = c;
            }
        }

        [UIAttr]
        public string Sprite
        {
            set => _bg.sprite = UI.ResolveSprite(value);
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

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            _changed.Dispose();
            _endEdit.Dispose();
            _submit.Dispose();
            base.Dispose();
        }
    }
}
