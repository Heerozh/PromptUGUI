using System.Collections.Generic;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.IR;
using PromptUGUI.Layout;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using UnityEngine;
using UnityColor = UnityEngine.Color;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls
{
    /// <summary>
    /// A host's non-layout decoration: corner brackets, an edge tick, an emphasis line. One authored
    /// node fans out into one instance per <c>at=</c> position, so the four brackets of a selected
    /// card are one element rather than four.
    ///
    /// <para><b>It only draws and places.</b> Everything around it is machinery that already exists:
    /// "only while selected" is <c>&lt;Show on="state-selected"&gt;</c>, "swap with the theme" is
    /// <c>class=</c> (and <c>kind="none"</c> for a theme that wants no decoration at all), and
    /// staying out of the layout flow is the <c>flow="false"</c> channel — forced on here, since a
    /// decoration hanging off the host's edges has no business claiming a slot in a Stack.</para>
    /// </summary>
    /// <remarks>
    /// Instances are created on first demand and afterwards only toggled — never destroyed (the
    /// Add-block Strategy C rule) — so a Variant or theme flipping <c>kind</c> / <c>at</c> back and
    /// forth round-trips exactly. Like every other reconciling control here, the declared state is
    /// cleared in <see cref="OnBeforeApply"/> and rebuilt by the setters each pass, because
    /// <c>ControlAttributeApplier</c> signals "no longer declared" by <em>not</em> calling the
    /// setter (spec §8's "compute, don't latch").
    /// </remarks>
    public sealed class Decor : Control
    {
        /// <summary>Follows the shipped <c>__Surface</c> / <c>__FocusCursor</c> convention.</summary>
        internal const string InstancePrefix = "__Decor:";

        /// <summary>The sprite layer's node, inside the slot node it shares with the SDF layer.</summary>
        private const string SpriteNodeName = "__DecorSprite";

        private const float DefaultThickness = 2f;

        private DecorKind _kind;
        private DecorSlot[] _authoredSlots;
        private DecorExtentSpec _extent;
        private float _thickness = DefaultThickness;
        private UnityColor _fillTop = UnityColor.white;
        private UnityColor _fillBottom = UnityColor.white;
        private float _glow;
        private UnityColor _glowColor;
        private bool _glowColorDeclared;
        private float _inset;
        private float _offset;
        private Sprite _sprite;
        private bool _mirror = true;

        private readonly Dictionary<DecorSlot, Instance> _instances = new();

        /// <summary>
        /// One slot's node plus whichever drawing layer it has needed so far. Both layers are built
        /// lazily and never destroyed: <c>Graphic</c> is <c>[DisallowMultipleComponent]</c>, so an
        /// SDF kind and a sprite kind cannot share a GameObject, and a theme that swaps one for the
        /// other has to find both still standing. A document that only ever uses one of them only
        /// ever builds one.
        /// </summary>
        private sealed class Instance
        {
            public RectTransform Rect;
            public DecorPanel Panel;
            public UnityImage Image;
        }

        // A decoration covers the host, so its own node is the host's rect; the instances inside it
        // anchor to the corners and edges of that.
        protected override AnchorPreset GetDefaultAnchor(SizeSpec sizeSpec)
            => new(AnchorVertical.Stretch, AnchorHorizontal.Stretch);

        // Never takes a slot in a parent Stack, never contributes a preferred size (spec §5).
        protected internal override bool ParticipatesInLayout => false;

        internal override void OnBeforeApply()
        {
            _kind = DecorKind.None;
            _authoredSlots = null;
            _extent = DecorExtentSpec.None;
            _thickness = DefaultThickness;
            _fillTop = UnityColor.white;
            _fillBottom = UnityColor.white;
            _glow = 0f;
            _glowColorDeclared = false;
            _inset = 0f;
            _offset = 0f;
            _sprite = null;
            _mirror = true;
        }

        /// <summary>What to draw: <c>bracket</c> / <c>tick</c> / <c>line</c> / <c>none</c>.</summary>
        [UIAttr, Preserve]
        public string Kind
        {
            set => _kind = DecorParser.ParseKind(value);
        }

        /// <summary>
        /// Where the instances sit — a comma list of <c>anchor=</c>'s own words. Corners
        /// (<c>top-left</c> …) for <c>bracket</c>, edges (<c>top</c> / <c>bottom</c> /
        /// <c>left</c> / <c>right</c>) for <c>tick</c> and <c>line</c>. Left out: all four corners
        /// for a bracket, the bottom edge for a tick or line.
        /// </summary>
        [UIAttr, Preserve]
        public string At
        {
            set => _authoredSlots = DecorParser.ParseAt(value);
        }

        /// <summary>
        /// How big the decoration is: <c>W</c> / <c>WxH</c> in pixels — a bracket's arm lengths, a
        /// tick's base × height — or <c>P%</c> of the edge it runs along, for a line.
        ///
        /// <para>Deliberately not called <c>size</c>: that name is already a common layout attribute
        /// (it sizes the node itself), so it never reaches a control's own setter — and on a Decor,
        /// whose node always fills its host, sizing the node is meaningless anyway.</para>
        /// </summary>
        [UIAttr, Preserve]
        public string Extent
        {
            set => _extent = DecorParser.ParseExtent(value);
        }

        /// <summary>Stroke width of a bracket or a line (px).</summary>
        [UIAttr, Preserve]
        public string Thickness
        {
            set => _thickness = string.IsNullOrWhiteSpace(value)
                ? DefaultThickness
                : ProceduralValueParser.Pixels(value, "thickness");
        }

        /// <summary>
        /// Fill colour. Theme token / hex / CSS name / <c>/alpha</c> suffix, and the comma double
        /// value for a top-to-bottom gradient — the same parse every other <c>color=</c> gets.
        /// </summary>
        [UIAttr, Preserve]
        public string Color
        {
            set
            {
                var spec = UI.Theme.ResolveSpec(value);
                _fillTop = spec.Top;
                _fillBottom = spec.Bottom;
            }
        }

        /// <summary>Outer glow radius (px). Inflates the drawn quad, never the layout rect.</summary>
        [UIAttr, Preserve]
        public string Glow
        {
            set => _glow = ProceduralValueParser.Pixels(value, "glow");
        }

        /// <summary>Glow colour. Solid only; follows the fill when not written.</summary>
        [UIAttr, Preserve]
        public string GlowColor
        {
            set
            {
                _glowColor = UI.Theme.Resolve(value);
                _glowColorDeclared = true;
            }
        }

        /// <summary>
        /// Signed distance from the host's edge: positive moves the instance inwards from flush,
        /// negative pushes it outside (a tick that hangs off the edge).
        /// </summary>
        [UIAttr, Preserve]
        public string Inset
        {
            set => _inset = SignedPixels(value, "inset");
        }

        /// <summary>Signed shift along the edge, from its centre. Edge kinds only.</summary>
        [UIAttr, Preserve]
        public string Offset
        {
            set => _offset = SignedPixels(value, "offset");
        }

        /// <summary>
        /// The picture to draw, for <c>kind="sprite"</c>. Resolved through the project's sprite
        /// resolver like every other <c>sprite=</c>, so <c>.pxl</c> pixel art, an atlas entry and an
        /// Addressable all arrive the same way.
        /// </summary>
        [UIAttr(IsSprite = true), Preserve]
        public string Sprite
        {
            set => _sprite = string.IsNullOrWhiteSpace(value) ? null : UI.ResolveSprite(value);
        }

        /// <summary>
        /// Whether the library reflects the artwork into the slots the author did not draw: corner
        /// art is drawn for the top-left and mirrored into the other three, edge art is drawn for
        /// the bottom and mirrored / rotated onto the other three edges. Turn it off for ornament
        /// that only reads one way round. Default <c>true</c>.
        /// </summary>
        [UIAttr, Preserve]
        public string Mirror
        {
            set => _mirror = string.IsNullOrWhiteSpace(value) || bool.Parse(value.Trim());
        }

        internal override void OnAfterApply() => Reconcile();

        /// <summary>Test seam: re-runs the pass without going through the attribute applier.</summary>
        internal void ReconcileForTests() => Reconcile();

        private void Reconcile()
        {
            // Cross-attribute checks can only happen here: [UIAttr] setters fire in no guaranteed
            // order, so nothing that needs to see kind AND at at once can live in one of them.
            if (!DecorParser.TryValidate(_kind, _authoredSlots, _extent, out var error))
                throw new ParseException(error);

            var slots = _authoredSlots ?? DecorParser.DefaultSlots(_kind);
            var extent = _extent.HasValue ? _extent : DecorParser.DefaultExtent(_kind);
            var on = _kind != DecorKind.None;

            // An Image with no sprite paints a solid rectangle — a much worse outcome than the
            // missing ornament lint is already going to name.
            var drawing = on && (_kind != DecorKind.Sprite || _sprite != null);

            foreach (var pair in _instances)
            {
                var wanted = drawing && System.Array.IndexOf(slots, pair.Key) >= 0;
                if (!wanted && pair.Value.Rect.gameObject.activeSelf)
                    pair.Value.Rect.gameObject.SetActive(false);
            }

            if (!drawing) return;

            var sprite = _kind == DecorKind.Sprite;

            foreach (var slot in slots)
            {
                var inst = EnsureInstance(slot);
                if (!inst.Rect.gameObject.activeSelf) inst.Rect.gameObject.SetActive(true);

                if (sprite) DrawAsSprite(inst, slot, extent);
                else DrawAsSdf(inst, slot, extent);
            }
        }

        private void DrawAsSdf(Instance inst, DecorSlot slot, DecorExtentSpec extent)
        {
            if (inst.Image != null && inst.Image.gameObject.activeSelf)
                inst.Image.gameObject.SetActive(false);

            var panel = inst.Panel ??= inst.Rect.gameObject.AddComponent<DecorPanel>();
            panel.enabled = true;

            Place(inst.Rect, slot, extent);

            panel.SetKind(_kind);
            panel.SetSlot(slot);
            panel.SetFill(_fillTop, _fillBottom);
            panel.SetThickness(_thickness);
            panel.SetGlowSize(_glow);
            if (_glowColorDeclared) panel.SetGlowColor(_glowColor);
            else panel.ClearGlowColor();

            // Eagerly, so a freshly built instance owns its material before anything renders
            // (Frame.OnAfterApply does the same for its panel).
            panel.FlushParams();
        }

        private void DrawAsSprite(Instance inst, DecorSlot slot, DecorExtentSpec extent)
        {
            if (inst.Panel != null) inst.Panel.enabled = false;

            if (inst.Image == null)
            {
                var go = new GameObject(SpriteNodeName, typeof(RectTransform), typeof(CanvasRenderer));
                go.transform.SetParent(inst.Rect, false);
                var rt = (RectTransform)go.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;
                inst.Image = go.AddComponent<UnityImage>();
                inst.Image.raycastTarget = false;
            }
            if (!inst.Image.gameObject.activeSelf) inst.Image.gameObject.SetActive(true);

            inst.Image.sprite = _sprite;
            // On a sprite the fill colour is an ordinary tint, the way it is on every other Image;
            // a gradient has nowhere to go here, so the top stop stands for the whole value.
            inst.Image.color = _fillTop;

            PlaceSprite(inst.Rect, slot, extent);
        }

        private Instance EnsureInstance(DecorSlot slot)
        {
            if (_instances.TryGetValue(slot, out var existing)) return existing;

            var go = new GameObject(InstancePrefix + DecorParser.SlotName(slot),
                                    typeof(RectTransform));
            go.transform.SetParent(RectTransform, false);
            var inst = new Instance { Rect = (RectTransform)go.transform };
            _instances[slot] = inst;
            return inst;
        }

        /// <summary>
        /// Places one instance flush against its corner or edge, on the inside, then applies
        /// <c>inset</c> / <c>offset</c>. A fractional line takes its length from stretched anchors
        /// rather than a resolved number, so it follows the host's width without this code ever
        /// having to know it — and without a first-frame ordering problem.
        /// </summary>
        private void Place(RectTransform rt, DecorSlot slot, DecorExtentSpec extent)
        {
            SlotGeometry(slot, out var anchor, out var inward, out var along);
            var horizontalEdge = slot == DecorSlot.Top || slot == DecorSlot.Bottom;
            var verticalEdge = slot == DecorSlot.Left || slot == DecorSlot.Right;

            var length = extent.IsFraction ? 0f : extent.Width;
            var cross = _kind == DecorKind.Line ? _thickness : extent.Height;

            Vector2 min = anchor, max = anchor, delta;

            if (extent.IsFraction && (horizontalEdge || verticalEdge))
            {
                var half = Mathf.Clamp01(extent.Width) * 0.5f;
                if (horizontalEdge)
                {
                    min.x = 0.5f - half;
                    max.x = 0.5f + half;
                    delta = new Vector2(0f, cross);
                }
                else
                {
                    min.y = 0.5f - half;
                    max.y = 0.5f + half;
                    delta = new Vector2(cross, 0f);
                }
            }
            else if (verticalEdge)
            {
                // The base of a tick and the run of a line follow the edge they sit on, so on the
                // left / right edges the authored W×H arrives transposed.
                delta = new Vector2(cross, length);
            }
            else if (horizontalEdge)
            {
                delta = new Vector2(length, cross);
            }
            else
            {
                delta = new Vector2(extent.Width, extent.Height);
            }

            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.pivot = anchor;
            rt.sizeDelta = delta;
            rt.anchoredPosition = inward * _inset + along * _offset;
            rt.localScale = Vector3.one;
            rt.localRotation = Quaternion.identity;
        }

        /// <summary>
        /// Places a sprite instance. Unlike the SDF layers — whose orientation is folded into the
        /// mesh — a picture has to be physically turned: the author draws the top-left corner (or
        /// the bottom edge) and the other slots are reflections and quarter turns of it. The rect
        /// therefore keeps the artwork's own proportions and pivots at its centre, with the
        /// transform doing the placing.
        /// </summary>
        private void PlaceSprite(RectTransform rt, DecorSlot slot, DecorExtentSpec extent)
        {
            SlotGeometry(slot, out var anchor, out var inward, out var along);
            var verticalEdge = slot == DecorSlot.Left || slot == DecorSlot.Right;

            var size = extent.IsNative && _sprite != null
                ? _sprite.rect.size
                : new Vector2(extent.Width, extent.Height);

            // A quarter turn swaps which of the artwork's own axes faces the host, so the depth to
            // clear when insetting follows the rotation rather than the rect.
            var rotated = _mirror && verticalEdge;
            var depth = rotated || !verticalEdge ? size.y : size.x;

            rt.anchorMin = anchor;
            rt.anchorMax = anchor;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.sizeDelta = size;

            rt.anchoredPosition = DecorParser.IsCornerSlot(slot)
                ? new Vector2(inward.x * (size.x * 0.5f + _inset),
                              inward.y * (size.y * 0.5f + _inset))
                : inward * (depth * 0.5f + _inset) + along * _offset;

            rt.localScale = _mirror ? MirrorScale(slot) : Vector3.one;
            rt.localRotation = rotated
                ? Quaternion.Euler(0f, 0f, slot == DecorSlot.Right ? 90f : -90f)
                : Quaternion.identity;
        }

        /// <summary>
        /// The reflection that carries the canonical artwork (top-left corner, bottom edge) into
        /// this slot. The two vertical edges are turned instead, not reflected — see
        /// <see cref="PlaceSprite"/>.
        /// </summary>
        private static Vector3 MirrorScale(DecorSlot slot)
        {
            switch (slot)
            {
                case DecorSlot.TopRight: return new Vector3(-1f, 1f, 1f);
                case DecorSlot.BottomRight: return new Vector3(-1f, -1f, 1f);
                case DecorSlot.BottomLeft: return new Vector3(1f, -1f, 1f);
                case DecorSlot.Top: return new Vector3(1f, -1f, 1f);
                default: return Vector3.one;
            }
        }

        private static void SlotGeometry(DecorSlot slot, out Vector2 anchor,
                                         out Vector2 inward, out Vector2 along)
        {
            switch (slot)
            {
                case DecorSlot.TopLeft:
                    anchor = new Vector2(0f, 1f); inward = new Vector2(1f, -1f); break;
                case DecorSlot.TopRight:
                    anchor = new Vector2(1f, 1f); inward = new Vector2(-1f, -1f); break;
                case DecorSlot.BottomRight:
                    anchor = new Vector2(1f, 0f); inward = new Vector2(-1f, 1f); break;
                case DecorSlot.BottomLeft:
                    anchor = new Vector2(0f, 0f); inward = new Vector2(1f, 1f); break;
                case DecorSlot.Top:
                    anchor = new Vector2(0.5f, 1f); inward = new Vector2(0f, -1f); break;
                case DecorSlot.Bottom:
                    anchor = new Vector2(0.5f, 0f); inward = new Vector2(0f, 1f); break;
                case DecorSlot.Left:
                    anchor = new Vector2(0f, 0.5f); inward = new Vector2(1f, 0f); break;
                default:
                    anchor = new Vector2(1f, 0.5f); inward = new Vector2(-1f, 0f); break;
            }

            // Corners have no edge to slide along, so offset is inert there (lint warns).
            along = slot == DecorSlot.Left || slot == DecorSlot.Right
                ? new Vector2(0f, 1f)
                : DecorParser.IsCornerSlot(slot) ? Vector2.zero : new Vector2(1f, 0f);
        }

        private static float SignedPixels(string value, string attrName)
        {
            if (string.IsNullOrWhiteSpace(value)) return 0f;
            if (!float.TryParse(value.Trim(), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var px))
                throw new ParseException(
                    $"{attrName}=\"{value}\": expected a number of pixels (negative moves outward)");
            if (float.IsNaN(px) || float.IsInfinity(px))
                throw new ParseException($"{attrName}=\"{value}\": must be a finite number");
            return px;
        }
    }
}
