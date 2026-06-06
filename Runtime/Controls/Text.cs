using System;
using PromptUGUI.Application;
using PromptUGUI.Controls.Internal;
using PromptUGUI.Registry;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Controls
{
    public sealed class Text : Control
    {
        private TMP_Text _tmp;
        private string _fontType = "default";
        private bool _autosize;

        internal TMP_Text TmpComponent => _tmp;

        public override void OnAttached()
        {
            _tmp = GameObject.GetComponent<TMP_Text>();
            if (_tmp == null)
            {
                _tmp = GameObject.AddComponent<TextMeshProUGUI>();
                _tmp.color = ProceduralBuilders.DefaultLabelColor;
            }
            ApplyFont();
            PromptUGUI.Application.UI.Locale.Changed += ApplyFont;
        }

        public override void Dispose()
        {
            PromptUGUI.Application.UI.Locale.Changed -= ApplyFont;
            base.Dispose();
        }

        private void ApplyFont()
        {
            FontApplier.Apply(_tmp, _fontType);
        }

        [UIAttr("text"), Preserve]
        public string TextValue
        {
            set => _tmp.text = value ?? "";
        }

        internal override string PeekDefaultText() => _tmp != null ? _tmp.text : null;

        [UIAttr("fontSize"), Preserve]
        public int Size
        {
            set
            {
                _tmp.fontSize = value;
                if (_autosize) ApplyAutosize();
            }
        }

        [UIAttr, Preserve]
        public bool Autosize
        {
            set
            {
                _autosize = value;
                ApplyAutosize();
            }
        }

        private void ApplyAutosize()
        {
            if (_tmp == null) return;
            if (_autosize)
            {
                var size = _tmp.fontSize;
                _tmp.fontSizeMin = size;
                _tmp.fontSizeMax = size;
                _tmp.characterWidthAdjustment = 50f;
                _tmp.enableAutoSizing = true;
            }
            else
            {
                _tmp.enableAutoSizing = false;
            }
        }

        [UIAttr(IsColor = true), Preserve]
        public string Color
        {
            set => _tmp.color = UI.Theme.Resolve(value);
        }

        [UIAttr, Preserve]
        public string Align
        {
            set
            {
                var (h, v) = ParseAlign(value);
                _tmp.horizontalAlignment = h;
                _tmp.verticalAlignment = v;
            }
        }

        private static readonly char[] AlignSeparators = { '-', ' ' };

        // Maps the `align` string onto TMP's two independent alignment axes. Tokens are
        // hyphen- or space-separated and order-independent (e.g. "bottom-right" == "right-bottom");
        // the last token seen per axis wins. Horizontal defaults to Left, vertical to Middle, so the
        // legacy `left`/`center`/`right` values keep their old vertically-centred behaviour while the
        // full TMP grid (6 horizontal × 6 vertical) is now reachable.
        internal static (HorizontalAlignmentOptions, VerticalAlignmentOptions) ParseAlign(string value)
        {
            var h = HorizontalAlignmentOptions.Left;
            var v = VerticalAlignmentOptions.Middle;
            if (!string.IsNullOrEmpty(value))
            {
                foreach (var raw in value.Split(AlignSeparators, StringSplitOptions.RemoveEmptyEntries))
                {
                    switch (raw.Trim().ToLowerInvariant())
                    {
                        case "left": h = HorizontalAlignmentOptions.Left; break;
                        case "center": h = HorizontalAlignmentOptions.Center; break;
                        case "right": h = HorizontalAlignmentOptions.Right; break;
                        case "justified": h = HorizontalAlignmentOptions.Justified; break;
                        case "flush": h = HorizontalAlignmentOptions.Flush; break;
                        case "geo": h = HorizontalAlignmentOptions.Geometry; break;
                        case "top": v = VerticalAlignmentOptions.Top; break;
                        case "middle": v = VerticalAlignmentOptions.Middle; break;
                        case "bottom": v = VerticalAlignmentOptions.Bottom; break;
                        case "baseline": v = VerticalAlignmentOptions.Baseline; break;
                        case "midline": v = VerticalAlignmentOptions.Geometry; break;
                        case "capline": v = VerticalAlignmentOptions.Capline; break;
                        default:
                            throw new ArgumentException(
                                $"align token '{raw}' is not a horizontal "
                                + "(left|center|right|justified|flush|geo) or vertical "
                                + "(top|middle|bottom|baseline|midline|capline) keyword");
                    }
                }
            }
            return (h, v);
        }

        [UIAttr, Preserve]
        public bool Wrap
        {
            set => _tmp.textWrappingMode = value ? TextWrappingModes.Normal : TextWrappingModes.NoWrap;
        }

        // What to do once auto-sizing (if any) has done its best and the text still doesn't fit the
        // rect. Orthogonal to `wrap` and `autosize`. Omitting the attribute leaves TMP's default
        // (Overflow) untouched, so existing UIs are unaffected.
        [UIAttr, Preserve]
        public string Overflow
        {
            set => _tmp.overflowMode = ParseOverflow(value);
        }

        internal static TextOverflowModes ParseOverflow(string value)
        {
            switch ((value ?? "").Trim().ToLowerInvariant())
            {
                case "overflow": return TextOverflowModes.Overflow;
                case "ellipsis": return TextOverflowModes.Ellipsis;
                case "truncate": return TextOverflowModes.Truncate;
                default:
                    throw new ArgumentException(
                        $"overflow value '{value}' is not one of overflow|ellipsis|truncate");
            }
        }

        [UIAttr, Preserve]
        public bool RaycastTarget
        {
            set => _tmp.raycastTarget = value;
        }

        [UIAttr, Preserve]
        public string Font
        {
            set
            {
                _fontType = string.IsNullOrEmpty(value) ? "default" : value;
                ApplyFont();
            }
        }

        public override Vector2? GetNativeSize()
        {
            if (_tmp == null || string.IsNullOrEmpty(_tmp.text)) return null;
            _tmp.ForceMeshUpdate();
            return new Vector2(_tmp.preferredWidth, _tmp.preferredHeight);
        }

        // TMP_Text is itself a live ILayoutElement, so inside a V/HStack an author-omitted axis is left
        // to TMP's own dynamic measurement instead of a frozen native snapshot — height then follows the
        // wrapped content. See Control.UsesIntrinsicLayoutSize. Free-positioning still uses GetNativeSize.
        protected internal override bool UsesIntrinsicLayoutSize => true;
    }
}
