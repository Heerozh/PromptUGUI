using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;
using UnitySlider = UnityEngine.UI.Slider;

namespace PromptUGUI.Controls
{
    public sealed class Slider : Control
    {
        private UnityImage _bg;
        private UnityImage _fill;
        private UnityImage _handle;
        private UnitySlider _slider;
        private readonly Subject<float> _changed = new();

        public override void OnAttached()
        {
            // Background：竖向内缩到中间 50% (Y 0.25 — 0.75) sliced 轨道
            var bgRt = ProceduralBuilders.AddChild(RectTransform, "Background");
            bgRt.anchorMin = new Vector2(0f, 0.25f);
            bgRt.anchorMax = new Vector2(1f, 0.75f);
            bgRt.offsetMin = Vector2.zero;
            bgRt.offsetMax = Vector2.zero;
            _bg = bgRt.gameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultTrackColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);

            // Fill Area：跟 Background 同样 Y 内缩，X 两侧各留 10px (handle 半径)
            var fillArea = ProceduralBuilders.AddChild(RectTransform, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.25f);
            fillArea.anchorMax = new Vector2(1f, 0.75f);
            fillArea.anchoredPosition = new Vector2(-5f, 0f);
            fillArea.sizeDelta = new Vector2(-20f, 0f);
            _fill = ProceduralBuilders.AddImage(fillArea, "Fill", raycast: false);
            var fillRt = _fill.rectTransform;
            fillRt.anchorMin = Vector2.zero;
            // Y=1: Unity Slider.UpdateVisuals() forces anchorMax.y=1 for horizontal fill;
            // set explicitly so code intent is clear.
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.sizeDelta = new Vector2(10f, 0f);
            _fill.color = ProceduralBuilders.DefaultFillColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_fill);

            // Handle Slide Area：水平 stretch，左右各留 10px
            var handleArea = ProceduralBuilders.AddChild(RectTransform, "Handle Slide Area");
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.sizeDelta = new Vector2(-20f, 0f);
            handleArea.anchoredPosition = Vector2.zero;
            _handle = ProceduralBuilders.AddImage(handleArea, "Handle", raycast: false);
            var handleRt = _handle.rectTransform;
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.zero;
            handleRt.sizeDelta = new Vector2(20f, 0f);
            _handle.color = ProceduralBuilders.DefaultHandleColor;
            // Handle 用 simple type；preserveAspect=false（与默认 Knob 一致）
            ProceduralBuilders.ApplyDefaultSimpleSprite(_handle, ProceduralBuilders.SpriteRoundedRect);

            _slider = GameObject.GetComponent<UnitySlider>() ?? GameObject.AddComponent<UnitySlider>();
            _slider.targetGraphic = _handle;
            _slider.fillRect = _fill.rectTransform;
            _slider.handleRect = _handle.rectTransform;
            _slider.direction = UnitySlider.Direction.LeftToRight;

            _slider.onValueChanged.AddListener(v => _changed.OnNext(v));
        }

        [UIAttr] public float Min { set => _slider.minValue = value; }
        [UIAttr] public float Max { set => _slider.maxValue = value; }
        [UIAttr]
        public float Value
        {
            get => _slider.value;
            set => _slider.value = value;
        }
        [UIAttr] public bool WholeNumbers { set => _slider.wholeNumbers = value; }

        [UIAttr]
        public string Direction
        {
            set
            {
                _slider.direction = value switch
                {
                    "horizontal" => UnitySlider.Direction.LeftToRight,
                    "vertical" => UnitySlider.Direction.BottomToTop,
                    "reverse-horizontal" => UnitySlider.Direction.RightToLeft,
                    "reverse-vertical" => UnitySlider.Direction.TopToBottom,
                    _ => throw new System.ArgumentException(
                        $"Slider.direction='{value}' invalid; expected horizontal|vertical|reverse-horizontal|reverse-vertical"),
                };
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

        public Observable<float> OnValueChanged => _changed;

        public override void Dispose()
        {
            _changed.Dispose();
            base.Dispose();
        }
    }
}
