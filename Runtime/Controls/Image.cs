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

        [UIAttr]
        public string Sprite
        {
            set => _img.sprite = UI.ResolveSprite(value);
        }

        [UIAttr]
        public string Color
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c))
                    _img.color = c;
            }
        }

        [UIAttr]
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

        [UIAttr]
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
                // mask="self" path implemented in Task 8
            }
        }

        [UIAttr]
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
    }
}
