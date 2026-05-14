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
            set
            {
                if (string.IsNullOrEmpty(value)) { _img.sprite = null; return; }
                _img.sprite = Resources.Load<Sprite>(value);
            }
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
                _img.type = value switch
                {
                    "sliced" => UnityImage.Type.Sliced,
                    "tiled" => UnityImage.Type.Tiled,
                    "filled" => UnityImage.Type.Filled,
                    _ => UnityImage.Type.Simple,
                };
            }
        }
    }
}
