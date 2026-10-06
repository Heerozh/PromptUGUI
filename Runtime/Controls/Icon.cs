using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    public sealed class Icon : Control, ISpriteSlotHost
    {
        private UnityImage _img;
        // The last name written, resolved or not: `name` is runtime-owned (registered as the
        // RuntimeStateAttr), and the lock compares what was written, not what it resolved to.
        private string _name;
        // Resolves the name; for an on-demand runtime set, waits for the sprite and refreshes this icon
        // when it arrives (spec 2026-10-01-runtime-sprite-sets-design §7).
        private AsyncSpriteSlot _slot;

        internal override string PeekRuntimeState() => _name;

        public override void OnAttached()
        {
            // FxImage, not a plain Image: with no blur / glow / linear tint written it behaves
            // exactly like one (no material of its own, mesh untouched), and it is what makes those
            // attributes possible at all — see FxImage's class note.
            _img = GameObject.GetComponent<UnityImage>()
                   ?? GameObject.AddComponent<FxImage>();
            _img.preserveAspect = true;
            _img.raycastTarget = false;
            _img.color = UnityEngine.Color.white;
            _slot = new AsyncSpriteSlot(this, this);
        }

        [UIAttr(IsSprite = true), Preserve]
        public string Name
        {
            set
            {
                _name = value;
                _slot.Set(value);
            }
        }

        UnityImage ISpriteSlotHost.SlotGraphic => _img;

        string ISpriteSlotHost.SlotTag => "Icon";

        // <Icon> stays atlas-only: every non-empty value goes to UI.SpriteResolver, ':' or not.
        StaticResult ISpriteSlotHost.ResolveStatic(string value, out Sprite sprite)
        {
            sprite = null;
            if (UI.SpriteResolver == null)
            {
                if (UI.IsSpriteResolverLoadInFlight) return StaticResult.Deferred;
                UILog.Error(this,
                    $"Icon '{value}': UI.SpriteResolver is not registered. " +
                    $"Call SpriteResolverHelpers.UseSpriteSetResolver(spriteSets) " +
                    $"before opening Screens that contain <Icon>." + UI.RuntimeSetsHint());
                return StaticResult.Failed;
            }
            sprite = UI.SpriteResolver(value);
            if (sprite != null) return StaticResult.Ok;
            UILog.Error(this, UI.BuildSpriteResolutionFailureMessage("Icon", value));
            return StaticResult.Failed;
        }

        void ISpriteSlotHost.RefreshDerived() => ImageFxApplier.Flush(_img);

        bool ISpriteSlotHost.SizeDependsOnSprite => SizeFromNative;

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

        /// <summary>Blur radius (px). Softens the icon itself; the layout rect is untouched.</summary>
        [UIAttr, Preserve]
        public string Blur
        {
            set => ImageFxApplier.SetBlur(_img, "Icon", value);
        }

        /// <summary>Outer glow reach (px). Inflates the drawn quad, never the layout rect.</summary>
        [UIAttr, Preserve]
        public string Glow
        {
            set => ImageFxApplier.SetGlow(_img, "Icon", value);
        }

        /// <summary>Glow colour. Solid only; unwritten, the glow takes the icon's own blurred
        /// colour.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string GlowColor
        {
            set => ImageFxApplier.SetGlowColor(_img, "Icon", value);
        }

        /// <summary>Exposure of the picture and its glow (≥ 1, default 1 = unchanged): the tinted
        /// body whitens at its core while a self-coloured glow keeps its hue. Any <c>type</c>.</summary>
        [UIAttr, Preserve]
        public string Intensity
        {
            set => ImageFxApplier.SetIntensity(_img, "Icon", value);
        }

        /// <summary>
        /// Draws the icon and its glow in grey (luminance). The author's switch — a disabled Btn
        /// greys it through a separate one. From C# it shows at once, but a ReSolve replays a
        /// declared <c>grayscale=</c>: leave it out of the XML when code drives it.
        /// </summary>
        [UIAttr, Preserve]
        public bool Grayscale
        {
            get => ImageFxApplier.GetGrayscale(_img);
            set
            {
                ImageFxApplier.SetGrayscale(_img, "Icon", value);
                // Inside a pass OnAfterApply flushes once every setter has run; from code nothing
                // else would until the next canvas rebuild.
                if (!InApplyPass) ImageFxApplier.Flush(_img);
            }
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

        internal override void OnAfterApply()
        {
            // Setters run in an unspecified order and blur / glow depend on the sprite, so the
            // material is resolved once here — before anything renders, without waiting for a
            // canvas rebuild.
            ImageFxApplier.Flush(_img);
            _slot.AfterPass();
        }

        public override Vector2? GetNativeSize() =>
            _img != null && _img.sprite != null ? (Vector2?)_img.sprite.rect.size : null;
    }
}
