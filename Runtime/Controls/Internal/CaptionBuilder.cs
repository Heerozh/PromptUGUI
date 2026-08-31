using TMPro;
using UnityEngine;
using UnityImage = UnityEngine.UI.Image;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The three-slot caption row — <c>[icon] label [caret]</c> — shared by the controls that show a
    /// title with a fold indicator: <see cref="TabMenu"/>'s collapsed handle and
    /// <see cref="Collapsible"/>'s header (spec 2026-08-31-collapsible-design §6).
    ///
    /// <para>Two layout modes, because the two controls want opposite things from the caret.
    /// <b>Inline</b> (<c>arrowAtRight: false</c>, TabMenu) places all three left to right and lets
    /// the row hug its text — a channel switcher wants the caret right after the name, and
    /// <see cref="ContentWidth()"/> states that same geometry in closed form so the handle can size
    /// itself to it. <b>Pinned</b> (<c>arrowAtRight: true</c>, Collapsible) hangs the caret off the
    /// right edge and stretches the label into whatever is left — a header bar spans its panel, so
    /// there is no hugging to do and the caret belongs at the far end.</para>
    ///
    /// <para>Pinned mode is anchor-driven rather than hand-placed on purpose: a header's width is
    /// decided by the layout pass (its panel stretches it), so re-reading the host rect on every
    /// resize would mean re-laying out from a callback. Anchors do it for free.</para>
    ///
    /// <para>The builder owns construction, metrics and placement only. Sprites, colours and fonts
    /// arrive through the exposed <see cref="Icon"/> / <see cref="Label"/> / <see cref="Arrow"/>
    /// nodes, so each control keeps its own attribute surface and its own defaults.</para>
    /// </summary>
    internal sealed class CaptionBuilder
    {
        private readonly RectTransform _host;
        private readonly bool _arrowAtRight;

        private float _padX;
        private float _gap;
        private float _iconSize;
        private float _arrowSize;
        private string _fontType = "default";

        public UnityImage Icon { get; }
        public TMP_Text Label { get; }
        public UnityImage Arrow { get; }

        /// <summary>
        /// The caret's mesh-level turn, in degrees — 0 at rest, 180 when the thing it fronts is open.
        ///
        /// <para>Mesh-level and not a transform turn: rotation happens about the pivot, and the
        /// caret's pivot is an edge (its left one in inline mode), so a transform turn swings the
        /// glyph away from where it was placed. <see cref="RotateFlipEffect"/> turns the vertices
        /// about the rect's centre, so a square caret turns in place.</para>
        /// </summary>
        public float ArrowRotation
        {
            get => _arrowRotate.Rotation;
            set => _arrowRotate.Rotation = value;
        }

        private readonly RotateFlipEffect _arrowRotate;

        /// <summary>Inset from the row's left edge (and, in pinned mode, from its right edge).</summary>
        public float PadX
        {
            get => _padX;
            set { _padX = value; Layout(); }
        }

        /// <summary>Space between icon, label and caret.</summary>
        public float Gap
        {
            get => _gap;
            set { _gap = value; Layout(); }
        }

        public float IconSize
        {
            get => _iconSize;
            set { _iconSize = value; Layout(); }
        }

        public float ArrowSize
        {
            get => _arrowSize;
            set { _arrowSize = value; Layout(); }
        }

        /// <summary>Font type key from settings; assigning it re-resolves the label's font asset.</summary>
        public string FontType
        {
            get => _fontType;
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                ApplyFont();
            }
        }

        /// <summary>
        /// How much of the row's right end the caret claims, label inset included. Zero when there is
        /// no caret — that width goes back to the label (and, in <see cref="Collapsible"/>, to the
        /// author's <c>&lt;Header&gt;</c> host).
        /// </summary>
        public float ArrowZoneWidth => HasArrow ? _padX + _arrowSize + _gap : 0f;

        private bool HasIcon => Icon != null && Icon.enabled;
        private bool HasArrow => Arrow != null && Arrow.enabled;

        public CaptionBuilder(RectTransform host, bool arrowAtRight,
                              float padX, float gap, float iconSize, float arrowSize, float fontSize)
        {
            _host = host;
            _arrowAtRight = arrowAtRight;
            _padX = padX;
            _gap = gap;
            _iconSize = iconSize;
            _arrowSize = arrowSize;

            Icon = ProceduralBuilders.AddImage(host, "Icon", raycast: false);
            Icon.enabled = false;
            var irt = Icon.rectTransform;
            irt.anchorMin = new Vector2(0f, 0.5f);
            irt.anchorMax = new Vector2(0f, 0.5f);
            irt.pivot = new Vector2(0f, 0.5f);

            Label = ProceduralBuilders.AddText(host, "Label");
            Label.alignment = TextAlignmentOptions.Left;
            Label.raycastTarget = false;
            Label.fontSize = fontSize;
            Label.text = "";
            var lrt = Label.rectTransform;
            if (arrowAtRight)
            {
                // Stretched on both axes: the label fills the row's height so TMP's middle-left
                // alignment centres it, and its width is written as insets by Layout().
                lrt.anchorMin = Vector2.zero;
                lrt.anchorMax = Vector2.one;
                lrt.pivot = new Vector2(0.5f, 0.5f);
            }
            else
            {
                lrt.anchorMin = new Vector2(0f, 0.5f);
                lrt.anchorMax = new Vector2(0f, 0.5f);
                lrt.pivot = new Vector2(0f, 0.5f);
            }

            Arrow = ProceduralBuilders.AddImage(host, "Arrow", raycast: false);
            Arrow.color = ProceduralBuilders.DefaultGlyphColor;
            ProceduralBuilders.ApplyDefaultSimpleSprite(Arrow, ProceduralBuilders.SpriteCaret);
            _arrowRotate = Arrow.gameObject.AddComponent<RotateFlipEffect>();
            var art = Arrow.rectTransform;
            if (arrowAtRight)
            {
                art.anchorMin = new Vector2(1f, 0.5f);
                art.anchorMax = new Vector2(1f, 0.5f);
                art.pivot = new Vector2(1f, 0.5f);
            }
            else
            {
                art.anchorMin = new Vector2(0f, 0.5f);
                art.anchorMax = new Vector2(0f, 0.5f);
                art.pivot = new Vector2(0f, 0.5f);
            }

            ApplyFont();
            Layout();
        }

        /// <summary>Sets the icon sprite; <c>null</c> collapses the slot.</summary>
        public void SetIconSprite(Sprite sprite)
        {
            Icon.sprite = sprite;
            Icon.enabled = sprite != null;
            Layout();
        }

        /// <summary>
        /// Sets the caret sprite; <c>null</c> switches the Image off rather than leaving it sprite-less
        /// (a sprite-less Image draws a solid block).
        /// </summary>
        public void SetArrowSprite(Sprite sprite)
        {
            Arrow.sprite = sprite;
            Arrow.enabled = sprite != null;
            Layout();
        }

        public void SetText(string text)
        {
            Label.text = text ?? "";
            Layout();
        }

        public void SetFontSize(float size)
        {
            Label.fontSize = size;
            Layout();
        }

        public void ApplyFont() => FontApplier.Apply(Label, _fontType);

        /// <summary>
        /// Places the three slots, each collapsing to nothing when it has no content. Cheap and
        /// idempotent, so every metric / content setter simply calls it again.
        /// </summary>
        public void Layout()
        {
            var labelLeft = _padX + (HasIcon ? _iconSize + _gap : 0f);

            if (HasIcon)
            {
                Icon.rectTransform.sizeDelta = new Vector2(_iconSize, _iconSize);
                Icon.rectTransform.anchoredPosition = new Vector2(_padX, 0f);
            }

            if (_arrowAtRight)
            {
                var lrt = Label.rectTransform;
                lrt.offsetMin = new Vector2(labelLeft, 0f);
                lrt.offsetMax = new Vector2(-(HasArrow ? ArrowZoneWidth : _padX), 0f);

                if (HasArrow)
                {
                    Arrow.rectTransform.sizeDelta = new Vector2(_arrowSize, _arrowSize);
                    Arrow.rectTransform.anchoredPosition = new Vector2(-_padX, 0f);
                }
                return;
            }

            var textWidth = MeasureText(Label.text).x;
            Label.rectTransform.sizeDelta = new Vector2(textWidth, Label.rectTransform.sizeDelta.y);
            Label.rectTransform.anchoredPosition = new Vector2(labelLeft, 0f);

            if (HasArrow)
            {
                Arrow.rectTransform.sizeDelta = new Vector2(_arrowSize, _arrowSize);
                Arrow.rectTransform.anchoredPosition =
                    new Vector2(labelLeft + textWidth + _gap, 0f);
            }
        }

        /// <summary>
        /// The text's unconstrained natural size — NOT <c>preferredWidth</c>, which TMP measures at
        /// the live rect and would feed the previous solve's value back on a ReSolve. Mirrors
        /// Btn / Tab / Text.
        /// </summary>
        public Vector2 MeasureText(string text)
            => string.IsNullOrEmpty(text) ? Vector2.zero : Label.GetPreferredValues(text);

        /// <summary>The row's natural width for what it currently shows.</summary>
        public float ContentWidth() => ContentWidth(Label.text, HasIcon);

        /// <summary>
        /// The row's natural width for content it is <em>about to</em> show — the first layout pass
        /// runs before the caption is filled in, so a caller measuring itself has to say what is
        /// coming.
        /// </summary>
        public float ContentWidth(string text, bool hasIcon)
        {
            var w = _padX;
            if (hasIcon) w += _iconSize + _gap;
            w += MeasureText(text).x;
            if (HasArrow) w += _gap + _arrowSize;
            return w + _padX;
        }

        // The host is kept for symmetry with future layout modes that need it; pinned mode
        // deliberately does not read it (see the class remarks).
        public RectTransform Host => _host;
    }
}
