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
        private AspectRatioFitter _fitter;
        private RectMask2D _rectMask;
        private UnityEngine.UI.Mask _stencilMask;     // populated by Task 8
        private string _pendingMaskPadding;
        private bool? _pendingShowMask;               // populated by Task 8

        public override void OnAttached()
        {
            // FxImage, not a plain Image: with no blur / glow / linear tint written it behaves
            // exactly like one (no material of its own, mesh untouched), and it is what makes those
            // attributes possible at all — see FxImage's class note.
            _img = GameObject.GetComponent<UnityImage>()
                   ?? GameObject.AddComponent<FxImage>();
        }

        private PointerEventRelay EnsureRelay()
            => _pointerRelay ??= GameObject.AddComponent<PointerEventRelay>();

        private AspectRatioFitter EnsureFitter()
            => _fitter ??= GameObject.AddComponent<AspectRatioFitter>();

        public Observable<Unit> OnPointerEnter => EnsureRelay().OnPointerEnter;
        public Observable<Unit> OnPointerExit => EnsureRelay().OnPointerExit;
        public Observable<Unit> OnPointerDown => EnsureRelay().OnPointerDown;

        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set => _img.sprite = UI.ResolveSprite(value);
        }

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                if (spec.IsGradient) Internal.RotateFlipApplier.ReserveSlot(_img);
                Internal.ColorApplier.Apply(_img, spec);
            }
        }

        [UIAttr, Preserve]
        public string Tint
        {
            set => ImageTint.Apply(_img, value);
        }

        /// <summary>Blur radius (px). Softens the picture itself; the layout rect is untouched.</summary>
        [UIAttr, Preserve]
        public string Blur
        {
            set => ImageFxApplier.SetBlur(_img, "Image", value);
        }

        /// <summary>Outer glow reach (px). Inflates the drawn quad, never the layout rect.</summary>
        [UIAttr, Preserve]
        public string Glow
        {
            set => ImageFxApplier.SetGlow(_img, "Image", value);
        }

        /// <summary>Glow colour. Solid only; unwritten, the glow takes the sprite's own blurred
        /// colour.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string GlowColor
        {
            set => ImageFxApplier.SetGlowColor(_img, "Image", value);
        }

        private float _rotation;
        private string _flip;

        /// <summary>Clockwise degrees. Mesh-level: the RectTransform and layout are untouched.</summary>
        [UIAttr, Preserve]
        public float Rotation
        {
            get => _rotation;
            set { _rotation = value; Internal.RotateFlipApplier.Apply(_img, _rotation, _flip); }
        }

        /// <summary><c>x</c> / <c>y</c> / <c>xy</c> / <c>none</c>. Mirrors the mesh about its centre.</summary>
        [UIAttr, Preserve]
        public string Flip
        {
            get => _flip;
            set { _flip = value; Internal.RotateFlipApplier.Apply(_img, _rotation, _flip); }
        }

        [UIAttr, Preserve]
        public string Type
        {
            set
            {
                _typeExplicit = true;
                switch (value)
                {
                    case "contain":
                    case "cover":
                        // Fit 模式：sprite 完整画进 ARF 算好的 rect（9-slice 对 contain/cover 无意义）。
                        // 框 = 父级 rect，由 AspectRatioFitter 相对父级驱动；Image 自身 anchor/size 被接管。
                        _img.type = UnityImage.Type.Simple;
                        var f = EnsureFitter();
                        f.enabled = true;
                        f.aspectMode = value == "cover"
                            ? AspectRatioFitter.AspectMode.EnvelopeParent
                            : AspectRatioFitter.AspectMode.FitInParent;
                        break;
                    default:
                        _img.type = value switch
                        {
                            "sliced" => UnityImage.Type.Sliced,
                            "tiled" => UnityImage.Type.Tiled,
                            "filled" => UnityImage.Type.Filled,
                            _ => UnityImage.Type.Simple,
                        };
                        if (_fitter != null) _fitter.enabled = false;
                        break;
                }
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
            // Fit 模式：用最终 sprite 算 aspectRatio（Sprite/Type setter 同循环、顺序不保证，
            // 这里在所有 setter 之后跑；sprite 变化（含 variant 换图）也会重算）。
            if (_fitter != null && _fitter.enabled && _img.sprite != null)
            {
                var r = _img.sprite.rect;
                if (r.height > 0f) _fitter.aspectRatio = r.width / r.height;
            }

            // Auto-pick Sliced for 9-slice sprites when author didn't write type=.
            // Sprite border is set in the Sprite Editor; non-zero on any edge means the
            // asset was authored for 9-slice rendering.
            if (!_typeExplicit)
                _img.type = Internal.ProceduralBuilders.DeriveType(_img.sprite);

            // After the type is final: blur / glow only apply to the Simple quad, and the author
            // cannot see from the XML that this sprite turned into a 9-slice (lint can't either —
            // it never loads the asset), so say so once here.
            ImageFxApplier.WarnIfFxOnNonSimple(_img, "Image");
            ImageFxApplier.Flush(_img);
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
