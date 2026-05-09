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
            _bg = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _bg.color = ProceduralBuilders.DefaultTrackColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_bg);
            var fillArea = ProceduralBuilders.AddChild(RectTransform, "FillArea");
            _fill = ProceduralBuilders.AddImage(fillArea, "Fill", raycast: false);
            _fill.color = ProceduralBuilders.DefaultFillColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_fill);
            var handleArea = ProceduralBuilders.AddChild(RectTransform, "HandleArea");
            _handle = ProceduralBuilders.AddImage(handleArea, "Handle", raycast: false);
            _handle.color = ProceduralBuilders.DefaultHandleColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_handle);

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
