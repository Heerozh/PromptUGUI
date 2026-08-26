using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    /// Linear progress bar (horizontal / vertical, scale or Image.Type.Filled).
    /// Radial fill (cooldown ring) is intentionally out of scope; introduce a
    /// <Cooldown> control instead — see spec PB-D6.
    public sealed class Progress : ProceduralControl
    {
        // Image layers — conditionally null/active per spec §6 activation table.
        private UnityImage _bg;              // null disabled until bg=/bgColor= activates it

        // The primary surface is the Bg layer inside MaskWrapper, so a procedural shape is
        // clipped by the same mask the fill is. No Selectable: a Progress is display-only.
        private protected override GameObject SurfaceHost => _bg.gameObject;
        private UnityImage _maskGraphic;     // null until mask= setter runs
        private UnityEngine.UI.Mask _stencilMask;  // pairs with _maskGraphic
        private UnityImage _fill;            // always present (PB-D7)
        private UnityImage _frame;           // null disabled until frame= activates it

        // Attribute state.
        private float _value;
        // Whether a sprite / colour was authored for each layer; ReconcileLayerVisibility turns the
        // GameObjects on and off from these so the two setters cannot race each other.
        private bool _bgSprite, _bgColor, _frameSprite, _frameColor;

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
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                Internal.ColorApplier.Apply(_fill, spec);
                FillSurface.SetFill(spec.Top, spec.Bottom);
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

        [UIAttr(IsSprite = true), Preserve]
        public string Bg
        {
            set
            {
                _bgSprite = !(string.IsNullOrEmpty(value) || value == "none");
                _bg.sprite = _bgSprite ? UI.ResolveSprite(value) : null;
                if (_bgSprite) ProceduralBuilders.AutoSlice(_bg);
                ReconcileLayers();
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string BgColor
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                Internal.ColorApplier.Apply(_bg, spec);
                Surface.SetFill(spec.Top, spec.Bottom);
                _bgColor = true;
                ReconcileLayers();
            }
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Frame
        {
            set
            {
                _frameSprite = !(string.IsNullOrEmpty(value) || value == "none");
                _frame.sprite = _frameSprite ? UI.ResolveSprite(value) : null;
                if (_frameSprite) ProceduralBuilders.AutoSlice(_frame);
                ReconcileLayers();
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string FrameColor
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                Internal.ColorApplier.Apply(_frame, spec);
                FrameSurface.SetFill(spec.Top, spec.Bottom);
                _frameColor = true;
                ReconcileLayers();
            }
        }

        /// <summary>边框层的圆角。</summary>
        [UIAttr, Preserve]
        public string FrameRadius
        {
            set { var v = RadiusParser.Parse(value); FrameSurface.Declare(p => p.SetRadius(v)); }
        }

        private Internal.ProceduralSurface _frameSurface;
        private Internal.ProceduralSurface FrameSurface => _frameSurface ??= AddInnerSurface(_frame.gameObject);

        /// <summary>
        /// 把 bg + fill 一起裁成一个圆角形状 —— 这才是「端到端都圆的进度条」的做法。
        /// <c>radius=</c> 只管 bg 那一层，而 fill 是压在它上面的另一张方角 Image。
        ///
        /// <para>不写时**自动跟随 <c>radius</c>**（同 <c>&lt;ScrollList mask&gt;</c> 跟随 bg sprite、
        /// <c>&lt;Dropdown popupMask&gt;</c> 跟随 popupSprite 的既有规约）。显式写了任何值 ——
        /// 包括 <c>""</c> —— 就退出自动跟随。与 <c>mask=</c> 互斥：一个 GameObject 上只能有一个
        /// Graphic。</para>
        /// </summary>
        [UIAttr, Preserve]
        public string MaskRadius
        {
            set
            {
                _maskRadiusExplicit = true;
                _maskRadius = string.IsNullOrWhiteSpace(value) ? null : (RadiusSpec?)RadiusParser.Parse(value);
                ReconcileProceduralMask();
            }
        }

        private bool _maskRadiusExplicit;
        private bool _maskSpriteExplicit;
        private RadiusSpec? _maskRadius;
        private Internal.ProceduralPanel _maskPanel;

        [UIAttr(IsSprite = true), Preserve]
        public string Mask
        {
            set
            {
                _maskSpriteExplicit = true;
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
            // Base first: ReconcileLayers below reads SurfaceIsDrawing, which is only settled once
            // the surface has reconciled this pass.
            base.OnAfterApply();
            ReconcileProceduralMask();
            ProceduralBuilders.AutoSlice(_bg);
            ProceduralBuilders.AutoSlice(_frame);
            ProceduralBuilders.AutoSlice(_maskGraphic);
            ReconcileLayers();
            ReconcileFill();
        }

        /// <summary>
        /// A layer is shown when EITHER a sprite or a colour was authored for it.
        ///
        /// <para>Derived from those flags rather than switched on inside each setter, for two
        /// reasons. <c>bg=""</c> has to be able to turn the layer back OFF — it could previously only
        /// ever be switched on, so a Variant flip or a theme switch left a stale sprite showing. And
        /// because the answer is computed from BOTH flags, the sprite and colour setters no longer
        /// race each other within one apply pass, whose attribute order is unspecified.</para>
        ///
        /// <para>Called from the setters too, not just <c>OnAfterApply</c>: code that assigns
        /// <c>progress.Bg</c> at runtime never goes through an apply pass.</para>
        /// </summary>
        private void ReconcileLayers()
        {
            // …or a procedural surface is drawing: the surface lives INSIDE the Bg layer, so
            // leaving that layer switched off would hide the shape the author just asked for.
            _bg.gameObject.SetActive(_bgSprite || _bgColor || SurfaceIsDrawing);
            _frame.gameObject.SetActive(_frameSprite || _frameColor);
            ReconcileMaskVisibility();
        }

        private void ReconcileMaskVisibility()
        {
            if (_stencilMask == null) return;
            // Only the SPRITE mask keeps its dual role (PB-D9: with no bg authored, the mask sprite
            // doubles as the track). A procedural mask is a pure clipper — bg is the track, and
            // painting the mask as well would just stack two shapes on top of each other.
            _stencilMask.showMaskGraphic = _maskGraphic != null && !_bg.gameObject.activeSelf;
        }

        /// <summary>
        /// Clips bg AND fill to one rounded shape — the only way to get a bar that is rounded at
        /// both ends, since <c>radius=</c> shapes the bg alone and the fill is a square-cornered
        /// Image on top of it.
        ///
        /// <para>Recomputed every pass rather than latched, so a Variant that changes the radius (or
        /// stops declaring one) is honoured. Never destroys: the panel and Mask are created once and
        /// only enabled/disabled after that.</para>
        /// </summary>
        private void ReconcileProceduralMask()
        {
            // Unset maskRadius auto-tracks radius=, matching <ScrollList mask> / <Dropdown popupMask>.
            var spec = _maskRadiusExplicit ? _maskRadius : DeclaredRadius;
            // An authored sprite mask owns the Graphic slot on MaskWrapper, and Graphic is
            // [DisallowMultipleComponent] — so it wins outright and lint reports the pair.
            var want = spec.HasValue && !_maskSpriteExplicit;

            if (!want)
            {
                if (_maskPanel != null)
                {
                    _maskPanel.SetMaskSource(false);
                    _maskPanel.enabled = false;
                    if (_stencilMask != null && _maskGraphic == null) _stencilMask.enabled = false;
                }
                return;
            }

            var wrapper = (RectTransform)_fill.transform.parent;
            if (_maskPanel == null)
            {
                if (wrapper.GetComponent<UnityEngine.UI.Graphic>() != null) return;
                _maskPanel = wrapper.gameObject.AddComponent<Internal.ProceduralPanel>();
                _stencilMask ??= wrapper.gameObject.AddComponent<UnityEngine.UI.Mask>();
            }

            _maskPanel.enabled = true;
            // The clip is the SHAPE, so the panel has to emit geometry even though it paints
            // nothing — the stencil is written by its fragments.
            _maskPanel.SetMaskSource(true);
            _maskPanel.SetRadius(spec.Value);
            _maskPanel.FlushParams();
            _stencilMask.enabled = true;
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
