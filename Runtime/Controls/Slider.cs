using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;
using UnitySlider = UnityEngine.UI.Slider;

namespace PromptUGUI.Controls
{
    public sealed class Slider : ProceduralControl
    {
        private UnityImage _bg;

        // The primary surface is the TRACK (Background), a child. SurfaceSelectable stays null
        // on purpose (spec §13.1): the Slider's targetGraphic is the Handle, and the handle is
        // the part that actually reacts to hover/press. Moving it to the track would make the
        // whole groove flash on hover, which no slider does.
        private protected override GameObject SurfaceHost => _bg.gameObject;
        private UnityImage _fill;
        private UnityImage _handle;
        private UnitySlider _slider;
        private readonly Subject<float> _changed = new();

        // DSS-D3: Slider 无内容驱动自然尺寸；长边 160 + 短边 44 (tap target) 是常用默认。
        private const float MinTapHeight = 44f;
        private const float DefaultSliderLength = 160f;

        public override Vector2? GetNativeSize()
        {
            var horizontal = _slider == null
                          || _slider.direction == UnitySlider.Direction.LeftToRight
                          || _slider.direction == UnitySlider.Direction.RightToLeft;
            return horizontal
                ? new Vector2(DefaultSliderLength, MinTapHeight)
                : new Vector2(MinTapHeight, DefaultSliderLength);
        }

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
            ProceduralBuilders.ApplyDefaultInsetSprite(_bg);

            // Fill Area：跟 Background 同样 Y 内缩，X 两侧各留 10px (handle 半径)
            var fillArea = ProceduralBuilders.AddChild(RectTransform, "Fill Area");
            fillArea.anchorMin = new Vector2(0f, 0.25f);
            fillArea.anchorMax = new Vector2(1f, 0.75f);
            fillArea.anchoredPosition = new Vector2(-5f, 0f);
            fillArea.sizeDelta = new Vector2(-20f, 0f);
            _fill = ProceduralBuilders.AddImage(fillArea, "Fill", raycast: false);
            var fillRt = _fill.rectTransform;
            fillRt.anchorMin = Vector2.zero;
            // 默认 prefab 是 (0,0)，但 Unity Slider.UpdateVisuals() 会在 LeftToRight 方向把
            // anchorMax.y 强制覆写为 1。预设 (0,1) 避免首帧前一瞬间的视觉位差。
            fillRt.anchorMax = new Vector2(0f, 1f);
            fillRt.sizeDelta = new Vector2(10f, 0f);
            _fill.color = ProceduralBuilders.DefaultFillColor;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_fill);

            // Handle Slide Area：水平 stretch，左右各留 10px
            var handleArea = ProceduralBuilders.AddChild(RectTransform, "Handle Slide Area");
            handleArea.anchorMin = Vector2.zero;
            handleArea.anchorMax = Vector2.one;
            handleArea.sizeDelta = new Vector2(-20f, 0f);
            _handle = ProceduralBuilders.AddImage(handleArea, "Handle", raycast: false);
            var handleRt = _handle.rectTransform;
            handleRt.anchorMin = Vector2.zero;
            handleRt.anchorMax = Vector2.zero;
            handleRt.sizeDelta = new Vector2(20f, 0f);
            _handle.color = ProceduralBuilders.DefaultHandleColor;
            // Handle 用 simple type；preserveAspect=false 跟默认 Knob 一致。
            ProceduralBuilders.ApplyDefaultSimpleSprite(_handle, ProceduralBuilders.SpriteKnob);

            _slider = GameObject.GetComponent<UnitySlider>() ?? GameObject.AddComponent<UnitySlider>();
            _slider.targetGraphic = _handle;
            _slider.fillRect = _fill.rectTransform;
            _slider.handleRect = _handle.rectTransform;
            _slider.direction = UnitySlider.Direction.LeftToRight;

            _slider.onValueChanged.AddListener(v => _changed.OnNext(v));
        }

        [UIAttr, Preserve] public float Min { set => _slider.minValue = value; }
        [UIAttr, Preserve] public float Max { set => _slider.maxValue = value; }
        [UIAttr, Preserve]
        public float Value
        {
            get => _slider.value;
            set => _slider.value = value;
        }
        [UIAttr, Preserve] public bool WholeNumbers { set => _slider.wholeNumbers = value; }

        internal override string PeekRuntimeState() =>
            _slider != null ? _slider.value.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;

        [UIAttr, Preserve]
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

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                Internal.ColorApplier.Apply(_bg, spec);
                Surface.SetFill(spec);
            }
        }

        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_bg, value);
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set => _bg.sprite = UI.ResolveSprite(value);
        }

        // 内部图层：与 <Progress> 同一套命名规约 —— 每层一对 `<layer>` (sprite) + `<layer>Color`。
        // 空串清掉 sprite（UI.ResolveSprite 对 null/empty 直接返回 null），纯色轨道 / 滑块由此而来。

        /// <summary>已填充段的 sprite。<c>""</c> = 无图（纯色），同 <c>&lt;Progress fill&gt;</c>。</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Fill
        {
            set => _fill.sprite = UI.ResolveSprite(value);
        }

        /// <summary>已填充段的颜色；支持 token / <c>/alpha</c> / 渐变。</summary>
        [UIAttr(IsColor = true), Preserve]
        public string FillColor
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                Internal.ColorApplier.Apply(_fill, spec);
                FillSurface.SetFill(spec);
            }
        }

        /// <summary>已填充段的圆角。内层只给形状 —— 见 spec §6。</summary>
        [UIAttr, Preserve]
        public string FillRadius
        {
            set { var v = RadiusParser.Parse(value); FillSurface.Declare(p => p.SetRadius(v)); }
        }

        private Internal.ProceduralSurface _fillSurface;
        private Internal.ProceduralSurface FillSurface => _fillSurface ??= AddInnerSurface(_fill.gameObject);

        /// <summary>滑块的 sprite。<c>""</c> = 无图（纯色方块）。</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Handle
        {
            set => _handle.sprite = UI.ResolveSprite(value);
        }

        /// <summary>滑块颜色；支持 token / <c>/alpha</c> / 渐变。</summary>
        [UIAttr(IsColor = true), Preserve]
        public string HandleColor
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                Internal.ColorApplier.Apply(_handle, spec);
                HandleSurface.SetFill(spec);
            }
        }

        /// <summary>滑块圆角；<c>pill</c> = 圆钮。</summary>
        [UIAttr, Preserve]
        public string HandleRadius
        {
            set { var v = RadiusParser.Parse(value); HandleSurface.Declare(p => p.SetRadius(v)); }
        }

        private Internal.ProceduralSurface _handleSurface;
        private Internal.ProceduralSurface HandleSurface => _handleSurface ??= AddInnerSurface(_handle.gameObject);

        public Observable<float> OnValueChanged => _changed;

        public override void Dispose()
        {
            _changed.Dispose();
            base.Dispose();
        }
    }
}
