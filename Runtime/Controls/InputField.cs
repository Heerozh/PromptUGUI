using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
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
            _placeholder.enableWordWrapping = false;
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
        }
    }
}
