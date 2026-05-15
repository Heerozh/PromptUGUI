using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using R3;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class Image : Control, IPointerEventSource
    {
        private UnityImage _img;
        private PointerEventRelay _pointerRelay;
        private bool _typeExplicit;

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
