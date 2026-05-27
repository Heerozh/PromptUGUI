using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    /// Linear progress bar (horizontal / vertical, scale or Image.Type.Filled).
    /// Radial fill (cooldown ring) is intentionally out of scope; introduce a
    /// <Cooldown> control instead — see spec PB-D6.
    public sealed class Progress : Control
    {
        // Image layers — conditionally null/active per spec §6 activation table.
        private UnityImage _bg;              // null disabled until bg=/bgColor= activates it
        private UnityImage _maskGraphic;     // null until mask= setter runs
        private UnityEngine.UI.Mask _stencilMask;  // pairs with _maskGraphic
        private UnityImage _fill;            // always present (PB-D7)
        private UnityImage _frame;           // null disabled until frame= activates it

        // Attribute state.
        private float _value;
        private string _direction = "horizontal";
        private string _mode = "scale";

        [UIAttr, Preserve]
        public float Value
        {
            get => _value;
            set
            {
                _value = Mathf.Clamp01(value);
                ReconcileFill();
            }
        }

        [UIAttr, Preserve]
        public string Direction
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                _direction = value;
                ReconcileFill();
            }
        }

        [UIAttr, Preserve]
        public string Mode
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                _mode = value;
                ReconcileFill();
            }
        }

        [UIAttr, Preserve]
        public string Fill
        {
            set
            {
                _fill.sprite = UI.ResolveSprite(value);
                ReconcileFill();
            }
        }

        [UIAttr, Preserve]
        public string FillColor
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (ColorUtility.TryParseHtmlString(value, out var c)) _fill.color = c;
            }
        }

        internal override void OnAfterApply()
        {
            ReconcileFill();
        }

        private void ReconcileFill()
        {
            var rt = _fill.rectTransform;
            if (_mode == "fill")
            {
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                _fill.type = UnityImage.Type.Filled;
                (_fill.fillMethod, _fill.fillOrigin) = _direction switch
                {
                    "horizontal" => (UnityImage.FillMethod.Horizontal, (int)UnityImage.OriginHorizontal.Left),
                    "reverse-horizontal" => (UnityImage.FillMethod.Horizontal, (int)UnityImage.OriginHorizontal.Right),
                    "vertical" => (UnityImage.FillMethod.Vertical, (int)UnityImage.OriginVertical.Bottom),
                    "reverse-vertical" => (UnityImage.FillMethod.Vertical, (int)UnityImage.OriginVertical.Top),
                    _ => (UnityImage.FillMethod.Horizontal, (int)UnityImage.OriginHorizontal.Left),
                };
                _fill.fillAmount = _value;
            }
            else // scale (default)
            {
                // Reset away from Filled, then pick Simple/Sliced per sprite border.
                _fill.fillAmount = 1f;
                _fill.type = (_fill.sprite != null && _fill.sprite.border != Vector4.zero)
                    ? UnityImage.Type.Sliced
                    : UnityImage.Type.Simple;
                (rt.anchorMin, rt.anchorMax) = _direction switch
                {
                    "horizontal" => (Vector2.zero, new Vector2(_value, 1f)),
                    "reverse-horizontal" => (new Vector2(1f - _value, 0f), Vector2.one),
                    "vertical" => (Vector2.zero, new Vector2(1f, _value)),
                    "reverse-vertical" => (new Vector2(0f, 1f - _value), Vector2.one),
                    _ => (Vector2.zero, new Vector2(_value, 1f)),
                };
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
            }
        }

        public override void OnAttached()
        {
            // MaskWrapper: stretch wrapper around Bg + Fill. UI.Mask + UnityImage attached
            // lazily when mask= setter runs (PB-D7 / PB-D8).
            var maskRt = ProceduralBuilders.AddChild(RectTransform, "MaskWrapper");

            // Bg: pre-built but inactive until bg=/bgColor= sets it (PB-D8 / PB-D9 / PB-D10).
            var bgRt = ProceduralBuilders.AddChild(maskRt, "Bg");
            bgRt.gameObject.SetActive(false);
            _bg = bgRt.gameObject.AddComponent<UnityImage>();
            _bg.raycastTarget = false;

            // Fill: always present; reconcile writes its anchors or fillAmount.
            _fill = ProceduralBuilders.AddImage(maskRt, "Fill", raycast: false);

            // Frame: pre-built but inactive until frame= sets it. PB-D16: raycast off.
            var frameRt = ProceduralBuilders.AddChild(RectTransform, "Frame");
            frameRt.gameObject.SetActive(false);
            _frame = frameRt.gameObject.AddComponent<UnityImage>();
            _frame.raycastTarget = false;
        }
    }
}
