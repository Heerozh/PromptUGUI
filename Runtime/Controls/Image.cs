using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Layout;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityEngine.UI;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class Image : Control, IPointerEventSource
    {
        private UnityImage _img;
        private PointerEventRelay _pointerRelay;
        private bool _typeExplicit;
        private RectMask2D _rectMask;
        private UnityEngine.UI.Mask _stencilMask;     // populated by Task 8
        private string _pendingMaskPadding;
        private bool? _pendingShowMask;               // populated by Task 8

        public override void OnAttached()
        {
            _img = GameObject.GetComponent<UnityImage>()
                   ?? GameObject.AddComponent<UnityImage>();
        }

        private PointerEventRelay EnsureRelay()
            => _pointerRelay ??= GameObject.AddComponent<PointerEventRelay>();

        public Observable<Unit> OnPointerEnter => EnsureRelay().OnPointerEnter;
        public Observable<Unit> OnPointerExit => EnsureRelay().OnPointerExit;
        public Observable<Unit> OnPointerDown => EnsureRelay().OnPointerDown;

        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set => _img.sprite = UI.ResolveSprite(value);
        }

        [UIAttr, Preserve]
        public string Color
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c))
                    _img.color = c;
            }
        }

        [UIAttr, Preserve]
        public string Type
        {
            set
            {
                _typeExplicit = true;
                _img.type = value switch
                {
                    "sliced" => UnityImage.Type.Sliced,
                    "tiled" => UnityImage.Type.Tiled,
                    "filled" => UnityImage.Type.Filled,
                    _ => UnityImage.Type.Simple,
                };
            }
        }

        [UIAttr, Preserve]
        public string Mask
        {
            set
            {
                if (value == "rect")
                {
                    _rectMask ??= GameObject.AddComponent<RectMask2D>();
                    if (!string.IsNullOrEmpty(_pendingMaskPadding))
                        _rectMask.padding = MaskPaddingParser.Parse(_pendingMaskPadding);
                }
                else if (value == "self")
                {
                    _stencilMask ??= GameObject.AddComponent<UnityEngine.UI.Mask>();
                    if (_pendingShowMask.HasValue)
                        _stencilMask.showMaskGraphic = _pendingShowMask.Value;
                }
            }
        }

        [UIAttr, Preserve]
        public string ShowMask
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                _pendingShowMask = bool.Parse(value);
                if (_stencilMask != null)
                    _stencilMask.showMaskGraphic = _pendingShowMask.Value;
            }
        }

        [UIAttr, Preserve]
        public string MaskPadding
        {
            set
            {
                _pendingMaskPadding = value;
                if (_rectMask != null)
                    _rectMask.padding = MaskPaddingParser.Parse(value);
            }
        }

        internal override void OnAfterApply()
        {
            // Auto-pick Sliced for 9-slice sprites when author didn't write type=.
            // Sprite border is set in the Sprite Editor; non-zero on any edge means the
            // asset was authored for 9-slice rendering.
            if (_typeExplicit) return;
            var s = _img.sprite;
            _img.type = (s != null && s.border != Vector4.zero)
                ? UnityImage.Type.Sliced
                : UnityImage.Type.Simple;
        }

        public override Vector2? GetNativeSize()
        {
            if (_img == null || _img.sprite == null) return null;
            // Mirror UnityEngine.UI.Image.SetNativeSize(): rect / pixelsPerUnit
            // (which already folds in the sprite's pixelsPerUnit + pixelsPerUnitMultiplier).
            var ppu = _img.pixelsPerUnit;
            return new Vector2(_img.sprite.rect.width / ppu, _img.sprite.rect.height / ppu);
        }
    }
}
