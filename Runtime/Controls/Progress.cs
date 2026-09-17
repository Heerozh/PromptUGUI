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
    ///
    /// <para>The FILL is the primary surface (spec 2026-09-18): every procedural attribute —
    /// <c>glow</c>, <c>haze</c>, <c>borderWidth</c>, <c>glass</c> … — lands on the filled segment.
    /// A procedural fill keeps a full-size rect and hands <c>value</c> to the SDF as a cut
    /// (<see cref="ProceduralPanel.SetCut"/> — the shape shrunk to the value for <c>mode="scale"</c>,
    /// a half-plane for <c>mode="fill"</c>), so its glow escapes the track on every side and wraps
    /// the leading end; no stencil is built for it. <c>radius</c> is the bar's shape and is
    /// shared three ways (§5.1): the fill when procedural, the colour bg through an inner surface,
    /// and — when the fill is a bitmap, which cannot round itself — the clip mask on
    /// <c>MaskWrapper</c>. A track that wants a border or glass of its own is a <c>&lt;Frame&gt;</c>
    /// wrapped around the bar.</para>
    public sealed class Progress : ProceduralControl
    {
        // Image layers — conditionally null/active per spec §6 activation table.
        private UnityImage _bg;              // null disabled until bg=/bgColor= activates it

        // The primary surface is the Fill layer inside MaskWrapper. No Selectable: a Progress is
        // display-only.
        private protected override GameObject SurfaceHost => _fill.gameObject;
        private UnityImage _maskGraphic;     // null until mask= setter runs
        private UnityEngine.UI.Mask _stencilMask;  // pairs with _maskGraphic
        private UnityImage _fill;            // always present (PB-D7)
        private UnityImage _frame;           // null disabled until frame= activates it

        // Attribute state.
        private float _value;
        // Whether a sprite / colour was authored for each layer; ReconcileLayerVisibility turns the
        // GameObjects on and off from these so the two setters cannot race each other. _fillSprite
        // is also what decides where radius goes (RouteRadius).
        private bool _bgSprite, _bgColor, _frameSprite, _frameColor, _fillSprite;

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

        /// <summary>
        /// How the fill advances — the same word for both kinds of fill (spec 2026-09-18 §5.3).
        /// <c>scale</c>: the fill's shape itself shrinks to the value — a bitmap's rect is anchored
        /// to it (a 9-slice keeps both rounded ends), a procedural fill's SDF box is shrunk in the
        /// shader (the radius clamps to it, so a pill bar keeps a round leading end). <c>fill</c>:
        /// cropped at the value — <c>Image.fillAmount</c> for a bitmap, a half-plane intersection
        /// for the SDF; the leading edge is straight either way.
        /// </summary>
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

        /// <summary>Fill bitmap. <c>""</c> / <c>none</c> = no bitmap — the spelling that lets radius shape the fill itself.</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Fill
        {
            set
            {
                _fillSprite = !(string.IsNullOrEmpty(value) || value == "none");
                _fill.sprite = _fillSprite ? UI.ResolveSprite(value) : null;
                ReconcileFill();
            }
        }

        /// <summary>Fill colour: token / <c>/alpha</c> / gradient; the SDF fill once the fill is procedural.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string FillColor
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                Internal.ColorApplier.Apply(_fill, spec);
                Surface.SetFill(spec);
            }
        }

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
                BgSurface.SetFill(spec);
                _bgColor = true;
                ReconcileLayers();
            }
        }

        // The colour track's own surface. It takes exactly one thing, the bar's radius (RouteRadius);
        // a track that wants border / glow / glass is a <Frame> around the bar.
        private Internal.ProceduralSurface _bgSurface;
        private Internal.ProceduralSurface BgSurface => _bgSurface ??= AddInnerSurface(_bg.gameObject);

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
                FrameSurface.SetFill(spec);
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
        /// Clips bg + fill to one rounded shape. Only a BITMAP fill needs it (a bitmap cannot round
        /// itself), so that is the only case where it auto-tracks <c>radius</c> — a procedural fill
        /// rounds itself and is cut in the shader, and a mask here would clip its glow. Writing any
        /// value — including <c>""</c> — opts out of auto-tracking. Mutually exclusive with
        /// <c>mask=</c>: one Graphic per GameObject.
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

        /// <summary>
        /// Held rather than declared: which layer takes the bar's corner depends on whether
        /// <c>fill=</c> carried a bitmap, and that setter may run later in the same pass.
        /// <see cref="RouteRadius"/> settles it in <see cref="OnAfterApply"/>.
        /// </summary>
        private protected override void DeclareRadius(RadiusSpec radius) { }

        /// <summary>
        /// Spec 2026-09-18 §5.1 — <c>radius</c> is the bar's shape, three consumers:
        /// <list type="bullet">
        /// <item>the fill, unless a bitmap was authored for it (radius alone never retires a bitmap
        /// fill — that is what the mask is for; any OTHER procedural attribute does, per
        /// <c>PUI-PROC-SPRITE-CONFLICT</c>);</item>
        /// <item>the colour bg — a bitmap bg has its corners baked in and is left alone;</item>
        /// <item>the clip mask, only for a bitmap fill (<see cref="ReconcileProceduralMask"/>).</item>
        /// </list>
        /// </summary>
        private void RouteRadius()
        {
            var radius = DeclaredRadius;
            if (!radius.HasValue) return;
            var r = radius.Value;
            if (!_fillSprite) Surface.Declare(p => p.SetRadius(r));
            if (_bgColor && !_bgSprite) BgSurface.Declare(p => p.SetRadius(r));
        }

        internal override void OnAfterApply()
        {
            // Radius first: base reconciles the surfaces, and the routing has to be declared
            // before that. ReconcileLayers / ReconcileFill after: they read SurfaceIsDrawing,
            // which is only settled once the surface has reconciled this pass.
            RouteRadius();
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
            // radius alone no longer switches the bg on: the primary surface is the fill's, so a
            // shape with no bgColor is a rounded fill over nothing — not a white track (§5.4).
            _bg.gameObject.SetActive(_bgSprite || _bgColor);
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
        /// Clips bg AND fill to one rounded shape — how a BITMAP fill gets a bar that is rounded at
        /// both ends, since a bitmap cannot round itself. A procedural fill never needs it (it has
        /// the radius and the shader cut), and a mask over it would clip its glow — so the
        /// auto-tracking only fires for a bitmap fill; an explicit <c>maskRadius</c> is the author's
        /// call either way.
        ///
        /// <para>Recomputed every pass rather than latched, so a Variant that changes the radius (or
        /// stops declaring one, or swaps the fill between bitmap and SDF) is honoured. Never
        /// destroys: the panel and Mask are created once and only enabled/disabled after that.</para>
        /// </summary>
        private void ReconcileProceduralMask()
        {
            var spec = _maskRadiusExplicit ? _maskRadius : (_fillSprite ? DeclaredRadius : null);
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

        private Vector2 CutDirection() => _direction switch
        {
            "reverse-horizontal" => Vector2.left,
            "vertical" => Vector2.up,
            "reverse-vertical" => Vector2.down,
            _ => Vector2.right,
        };

        private void ReconcileFill()
        {
            var rt = _fill.rectTransform;
            if (SurfaceIsDrawing)
            {
                // Procedural: the rect stays the whole bar and the value is a cut in the SDF
                // (spec 2026-09-18 §5.2) — round for scale, flat for fill. No layout write per
                // value: the quad re-emits its four vertices and the material is untouched.
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                SurfacePanelOrNull.SetCut(CutDirection(), _value, round: _mode != "fill");
                return;
            }
            // Back on the bitmap path (a theme switch): the parked panel forgets the cut so it
            // comes back whole if the next skin asks for it again.
            SurfacePanelOrNull?.ClearCut();

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
