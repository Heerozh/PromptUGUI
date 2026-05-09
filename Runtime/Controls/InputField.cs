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
        }
    }
}
