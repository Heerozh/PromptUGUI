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

        internal override string PeekRuntimeState() =>
            _value.ToString(System.Globalization.CultureInfo.InvariantCulture);

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
        public string Tint
        {
            set
            {
                // _fill / _bg / _frame are all created in OnAttached (bg & frame just
                // start inactive), so they are non-null here regardless of attribute
                // order, and nothing resets .material on activation — applying directly
                // to all three is order-independent.
                ImageTint.Apply(_fill, value);
                ImageTint.Apply(_bg, value);
                ImageTint.Apply(_frame, value);
            }
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Fill
        {
            set
            {
                _fill.sprite = UI.ResolveSprite(value);
                ReconcileFill();
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string FillColor
        {
            set => _fill.color = UI.Theme.Resolve(value);
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Bg
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                _bg.sprite = UI.ResolveSprite(value);
                _bg.gameObject.SetActive(true);
                ProceduralBuilders.AutoSlice(_bg);
                ReconcileMaskVisibility();
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string BgColor
        {
            set
            {
                _bg.color = UI.Theme.Resolve(value);
                _bg.gameObject.SetActive(true);
                ReconcileMaskVisibility();
            }
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Frame
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                _frame.sprite = UI.ResolveSprite(value);
                _frame.gameObject.SetActive(true);
                ProceduralBuilders.AutoSlice(_frame);
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string FrameColor
        {
            set
            {
                _frame.color = UI.Theme.Resolve(value);
                _frame.gameObject.SetActive(true);
            }
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Mask
        {
            set
            {
                if (string.IsNullOrEmpty(value)) return;
                if (_maskGraphic == null)
                {
                    var maskRt = (RectTransform)_fill.transform.parent;
                    _maskGraphic = maskRt.gameObject.AddComponent<UnityImage>();
                    _maskGraphic.raycastTarget = false;
                    _stencilMask = maskRt.gameObject.AddComponent<UnityEngine.UI.Mask>();
                }
                _maskGraphic.sprite = UI.ResolveSprite(value);
                ProceduralBuilders.AutoSlice(_maskGraphic);
                ReconcileMaskVisibility();
            }
        }

        internal override void OnAfterApply()
        {
            ProceduralBuilders.AutoSlice(_bg);
            ProceduralBuilders.AutoSlice(_frame);
            ProceduralBuilders.AutoSlice(_maskGraphic);
            ReconcileMaskVisibility();
            ReconcileFill();
        }

        private void ReconcileMaskVisibility()
        {
            if (_stencilMask != null)
                _stencilMask.showMaskGraphic = !_bg.gameObject.activeSelf;
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
                // Reset away from Filled, then pick type via DeriveType (hint tiled → Tiled, border → Sliced, else Simple).
                _fill.fillAmount = 1f;
                _fill.type = Controls.Internal.ProceduralBuilders.DeriveType(_fill.sprite);
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

        public override Vector2? GetNativeSize()
        {
            if (_frame != null && _frame.sprite != null) return NativeOf(_frame);
            if (_bg != null && _bg.sprite != null) return NativeOf(_bg);
            return new Vector2(160f, 16f);
        }

        private static Vector2 NativeOf(UnityImage img)
        {
            var ppu = img.pixelsPerUnit;
            return new Vector2(img.sprite.rect.width / ppu, img.sprite.rect.height / ppu);
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
