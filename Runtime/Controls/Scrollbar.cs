using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;
using UnityScrollbar = UnityEngine.UI.Scrollbar;

namespace PromptUGUI.Controls
{
    /// <summary>
    /// The scrollbar of a scrolling host, as a part element: <c>&lt;ScrollList&gt;&lt;Scrollbar …/&gt;</c>
    /// (spec 2026-09-12-scrollbar-part-element-design). The node IS the uGUI <c>Scrollbar</c> —
    /// track Image + <c>Sliding Area</c> + <c>Handle</c>, the stock Scroll View subtree — and the
    /// host wires it to its <c>ScrollRect</c>, orients it, and reads <c>spacing</c> / <c>overlay</c>
    /// off it. A host that has no authored bar builds one of these itself, so there is exactly one
    /// implementation of "a scrollbar" whatever the host.
    ///
    /// <para>The track is the control's primary surface, so every <c>&lt;Frame&gt;</c> shape
    /// attribute (<c>radius</c> / <c>borderWidth</c> / <c>glow</c> …) applies to it unchanged; the
    /// handle is an inner layer addressed through the <c>handle*</c> prefix, the way
    /// <c>&lt;Slider&gt;</c> spells it.</para>
    /// </summary>
    /// <remarks>
    /// Geometry (spec §5.1) is written by this control from (orientation, thickness, padding) —
    /// never by the author: <c>anchor</c> / <c>size</c> / <c>margin</c> are rejected by
    /// <c>PUI-SCROLLBAR-LAYOUT-ATTR</c>, and whatever <c>ApplyCommon</c> writes for the defaults is
    /// overwritten again in <see cref="OnAfterApply"/> (the <c>&lt;SafeArea&gt;</c> precedent).
    /// </remarks>
    public sealed class Scrollbar : ProceduralControl
    {
        /// <summary>The node name of every bar, authored or default (an <c>id</c> still renames it).</summary>
        internal const string NodeName = "Scrollbar";

        private UnityImage _track;
        private UnityScrollbar _bar;
        private RectTransform _sliding;
        private UnityImage _handle;
        private IScrollbarHost _host;

        private bool _vertical = true;
        private float _thickness = ScrollbarAttrParser.DefaultThickness;
        private float? _spacing;
        private float _padAlong;
        private float _padAcross;
        private bool _overlay;

        // The track Image + uGUI Scrollbar sit on this node; the panel goes under it.
        private protected override GameObject SurfaceHost => GameObject;

        // SurfaceSelectable stays null on purpose (the <Slider> reasoning): the uGUI Scrollbar's
        // targetGraphic is the HANDLE, and hover/press belong to the handle, not to the groove.

        // Chrome: never a slot in a parent's layout, never a preferred size.
        protected internal override bool ParticipatesInLayout => false;

        // Whatever ApplyCommon writes is overwritten by ApplyGeometry; stretch is the least
        // surprising interim value (same as <Decor>).
        protected override AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
            => new(AnchorVertical.Stretch, AnchorHorizontal.Stretch);

        /// <summary>The uGUI component the host wires into its <c>ScrollRect</c>.</summary>
        internal UnityScrollbar Bar => _bar;
        internal bool IsVertical => _vertical;
        internal float CurrentThickness => _thickness;
        internal bool IsOverlay => _overlay;

        /// <summary>
        /// The distance to the viewport: the author's <c>spacing</c>, else the stock overlap capped
        /// at the thickness (<see cref="ScrollbarAttrParser.DefaultSpacing"/>).
        /// </summary>
        internal float ResolvedSpacing => _spacing ?? ScrollbarAttrParser.DefaultSpacing(_thickness);

        public override void OnAttached()
        {
            _track = GameObject.GetComponent<UnityImage>() ?? GameObject.AddComponent<UnityImage>();
            _track.color = UnityEngine.Color.white;
            ProceduralBuilders.ApplyDefaultInsetSprite(_track);

            _bar = GameObject.GetComponent<UnityScrollbar>() ?? GameObject.AddComponent<UnityScrollbar>();

            _sliding = ProceduralBuilders.AddChild(RectTransform, "Sliding Area");
            _handle = ProceduralBuilders.AddImage(_sliding, "Handle");
            _handle.color = UnityEngine.Color.white;
            ProceduralBuilders.ApplyDefaultSlicedSprite(_handle);

            _bar.targetGraphic = _handle;
            _bar.handleRect = _handle.rectTransform;
            // The stock popup values; a ScrollRect overwrites both the moment it has content.
            _bar.value = 0f;
            _bar.size = 0.2f;

            Orient(vertical: true);
        }

        /// <summary>Called by the host on adoption; thereafter the bar pushes its changes back.</summary>
        internal void AttachHost(IScrollbarHost host) => _host = host;

        /// <summary>
        /// Points the bar along one axis: a vertical bar hugs the host's right edge and scrolls
        /// bottom-to-top, a horizontal one hugs the bottom edge and scrolls left-to-right. The same
        /// node re-orients in place when the host's <c>direction</c> flips (spec §5.4).
        /// </summary>
        internal void Orient(bool vertical)
        {
            _vertical = vertical;
            _bar.direction = vertical
                ? UnityScrollbar.Direction.BottomToTop
                : UnityScrollbar.Direction.LeftToRight;
            ApplyGeometry();
        }

        /// <summary>
        /// Spec §5.1. Thickness <c>t</c>, padding <c>(E, S)</c> along / across, handle thickness
        /// <c>h = t − 2S</c>: the bar is <c>t</c> across and anchor-stretched along; the Sliding Area
        /// is inset <c>S</c> on the sides and <c>E + h/2</c> at the ends; the Handle overhangs the
        /// Sliding Area by <c>h/2</c> at both ends (uGUI's own trick — the handle then reaches the
        /// bar's ends at value 0 / 1 and never gets shorter than <c>h</c>). With no padding this is
        /// the stock Scroll View bar, rect for rect.
        /// </summary>
        private void ApplyGeometry()
        {
            if (_bar == null) return;
            var t = _thickness;
            var h = ScrollbarAttrParser.HandleThickness(t, _padAcross, out _);
            // The side inset as actually applied: the author's value, unless the handle had to be
            // clamped — then the Sliding Area is exactly one handle wide so the handle stays inside.
            var s = (t - h) / 2f;
            var rt = RectTransform;
            var handle = _handle.rectTransform;

            // uGUI writes the along-axis handle anchors from value / size on every visual update;
            // seed them with the same formula so a re-orientation does not show a stale pair
            // until the next UpdateVisuals. Both directions used here are the non-reversed ones.
            var travel = Mathf.Clamp01(_bar.value) * (1f - _bar.size);
            var along = new Vector2(travel, travel + _bar.size);

            if (_vertical)
            {
                rt.anchorMin = new Vector2(1f, 0f);
                rt.anchorMax = new Vector2(1f, 1f);
                rt.pivot = new Vector2(1f, 1f);
                rt.sizeDelta = new Vector2(t, 0f);
                rt.anchoredPosition = Vector2.zero;

                _sliding.anchorMin = Vector2.zero;
                _sliding.anchorMax = Vector2.one;
                _sliding.pivot = new Vector2(0.5f, 0.5f);
                _sliding.sizeDelta = new Vector2(-2f * s, -(2f * _padAlong + h));
                _sliding.anchoredPosition = Vector2.zero;

                handle.anchorMin = new Vector2(0f, along.x);
                handle.anchorMax = new Vector2(1f, along.y);
                handle.sizeDelta = new Vector2(0f, h);
            }
            else
            {
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(1f, 0f);
                rt.pivot = new Vector2(0f, 0f);
                rt.sizeDelta = new Vector2(0f, t);
                rt.anchoredPosition = Vector2.zero;

                _sliding.anchorMin = Vector2.zero;
                _sliding.anchorMax = Vector2.one;
                _sliding.pivot = new Vector2(0.5f, 0.5f);
                _sliding.sizeDelta = new Vector2(-(2f * _padAlong + h), -2f * s);
                _sliding.anchoredPosition = Vector2.zero;

                handle.anchorMin = new Vector2(along.x, 0f);
                handle.anchorMax = new Vector2(along.y, 1f);
                handle.sizeDelta = new Vector2(h, 0f);
            }
            handle.pivot = new Vector2(0.5f, 0.5f);
            handle.anchoredPosition = Vector2.zero;
        }

        internal override void OnAfterApply()
        {
            base.OnAfterApply();
            // ApplyCommon has just written the stretch defaults over the bar's rect — put the real
            // geometry back, then let the host re-read spacing / overlay / thickness.
            ApplyGeometry();
            _host?.OnScrollbarChanged(this);
        }

        // ───── geometry ─────

        /// <summary>
        /// Bar thickness: the width of a vertical bar, the height of a horizontal one. Default 20
        /// (the stock Scroll View); <c>0</c> is the spelling for "no visible bar".
        /// </summary>
        [UIAttr, Preserve]
        public string Thickness
        {
            set { _thickness = ScrollbarAttrParser.ParseThickness(value); ApplyGeometry(); }
        }

        /// <summary>
        /// Distance between the bar and the viewport (<c>ScrollRect.*ScrollbarSpacing</c>): positive
        /// separates them, negative pushes the bar into the viewport. Unset: <c>max(−thickness, −3)</c>.
        /// Not read by uGUI when <c>overlay="true"</c>.
        /// </summary>
        [UIAttr, Preserve]
        public string Spacing
        {
            set => _spacing = ScrollbarAttrParser.ParseSpacing(value);
        }

        /// <summary>
        /// How far the handle sits inside the track: one value for all four sides, or
        /// <c>"ends,sides"</c> — along the bar first, then across it (a vertical bar reads it as
        /// <c>V,H</c>). Across-inset makes the handle narrower than the track; along-inset keeps it
        /// off the ends. A handle thinner than 1 unit is clamped, with a warning.
        /// </summary>
        [UIAttr, Preserve]
        public string Padding
        {
            set
            {
                var (along, across) = ScrollbarAttrParser.ParsePadding(value);
                _padAlong = along;
                _padAcross = across;
                ScrollbarAttrParser.HandleThickness(_thickness, across, out var clamped);
                if (clamped)
                    Debug.LogWarning(
                        $"<Scrollbar id='{Id}'>: padding=\"{value}\" leaves no handle across a " +
                        $"{_thickness.ToString(System.Globalization.CultureInfo.InvariantCulture)}-unit bar " +
                        "(thickness − 2·sides ≤ 0); clamping the handle to 1 unit.");
                ApplyGeometry();
            }
        }

        /// <summary>
        /// <c>true</c> draws the bar over the content (<c>ScrollbarVisibility.AutoHide</c>) instead of
        /// shrinking the viewport for it (<c>AutoHideAndExpandViewport</c>, the default).
        /// </summary>
        [UIAttr, Preserve]
        public bool Overlay
        {
            set => _overlay = value;
        }

        // ───── skin ─────

        /// <summary>Track sprite. <c>""</c> / <c>none</c> = no bitmap (flat colour).</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set => _track.sprite = UI.ResolveSprite(value);
        }

        /// <summary>Track colour: token / <c>/alpha</c> / gradient; the SDF fill in procedural mode.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                ColorApplier.Apply(_track, spec);
                Surface.SetFill(spec);
            }
        }

        /// <summary>Handle sprite. <c>""</c> / <c>none</c> = no bitmap.</summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Handle
        {
            set => _handle.sprite = UI.ResolveSprite(value);
        }

        /// <summary>Handle colour: token / <c>/alpha</c> / gradient; the SDF fill once the handle is procedural.</summary>
        [UIAttr(IsColor = true), Preserve]
        public string HandleColor
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                ColorApplier.Apply(_handle, spec);
                HandleSurface.SetFill(spec);
            }
        }

        // ───── the handle's own surface ─────
        // Border and glow as well as radius (the other controls' inner layers stop at radius): a HUD
        // scrollbar's glowing knob is the whole point of the part element (spec §4.1). The handle
        // IS the uGUI Scrollbar's targetGraphic, so the surface takes the Selectable along and the
        // hover / press tint lands on whichever layer is drawing.

        private ProceduralSurface _handleSurface;
        private ProceduralSurface HandleSurface => _handleSurface ??= AddInnerSurface(_handle.gameObject, _bar);

        /// <summary>Handle corner radius; <c>pill</c> = capsule.</summary>
        [UIAttr, Preserve]
        public string HandleRadius
        {
            set { var v = RadiusParser.Parse(value); HandleSurface.Declare(p => p.SetRadius(v)); }
        }

        /// <summary>Handle inner border width (px, drawn inwards, no layout change).</summary>
        [UIAttr, Preserve]
        public string HandleBorderWidth
        {
            set { var v = ProceduralValueParser.Pixels(value, "handleBorderWidth"); HandleSurface.Declare(p => p.SetBorderWidth(v)); }
        }

        /// <summary>Handle border colour (solid).</summary>
        [UIAttr, Preserve]
        public string HandleBorderColor
        {
            set { var v = UI.Theme.Resolve(value); HandleSurface.Declare(p => p.SetBorderColor(v)); }
        }

        /// <summary>Handle outer glow (px). Inflates the drawn quad, not the layout; the bar sits outside the viewport mask, so nothing clips it.</summary>
        [UIAttr, Preserve]
        public string HandleGlow
        {
            set { var v = ProceduralValueParser.Pixels(value, "handleGlow"); HandleSurface.Declare(p => p.SetGlowSize(v)); }
        }

        /// <summary>Handle glow colour (solid); follows <c>handleColor</c> when unset.</summary>
        [UIAttr, Preserve]
        public string HandleGlowColor
        {
            set { var v = UI.Theme.Resolve(value); HandleSurface.Declare(p => p.SetGlowColor(v)); }
        }
    }
}
