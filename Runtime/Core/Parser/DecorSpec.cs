using System;
using System.Collections.Generic;
using System.Globalization;

namespace PromptUGUI.Parser
{
    /// <summary>
    /// What a <c>&lt;Decor&gt;</c> instance draws. The first three are SDF-drawn by
    /// <c>DecorPanel</c>; <see cref="Sprite"/> is an ordinary <c>Image</c>.
    /// </summary>
    /// <remarks>
    /// <see cref="None"/> is the theme's way to take a decoration away — a skin pack that writes
    /// <c>kind="none"</c> hides every instance without the document having to know (the
    /// <c>sprite="none"</c> convention, one layer up).
    /// </remarks>
    public enum DecorKind
    {
        None = 0,
        Bracket = 1,
        Tick = 2,
        Line = 3,
        Sprite = 4,
    }

    /// <summary>
    /// Where one instance sits on the host rect. The spelling is <c>anchor=</c>'s vocabulary on
    /// purpose: one set of words for the author to remember, not two.
    /// </summary>
    public enum DecorSlot
    {
        TopLeft,
        TopRight,
        BottomRight,
        BottomLeft,
        Top,
        Bottom,
        Left,
        Right,
    }

    /// <summary>
    /// A parsed <c>extent=</c>. Three shapes share the slot because they are three answers to the
    /// same question: pixels (<c>10x6</c>), a fraction of the edge (<c>60%</c>, line only) and the
    /// sprite's own pixel size (<c>native</c>, sprite only).
    /// </summary>
    /// <remarks>
    /// <see cref="HasValue"/> is what separates "the author wrote nothing" from "the author wrote
    /// zero": the former defers to <see cref="DecorParser.DefaultExtent"/> for the kind, and since a
    /// Variant can only overwrite a value, <c>extent=""</c> has to keep meaning the former.
    /// </remarks>
    public readonly struct DecorExtentSpec
    {
        public readonly float Width;
        public readonly float Height;
        public readonly bool IsNative;
        public readonly bool IsFraction;
        public readonly bool HasValue;

        private DecorExtentSpec(float width, float height, bool isNative, bool isFraction)
        {
            Width = width;
            Height = height;
            IsNative = isNative;
            IsFraction = isFraction;
            HasValue = true;
        }

        /// <summary>The author wrote nothing — the kind's default applies.</summary>
        public static readonly DecorExtentSpec None = default;

        public static DecorExtentSpec Pixels(float width, float height)
            => new DecorExtentSpec(width, height, false, false);

        /// <summary><paramref name="fraction"/> is 0..1 of the host edge; <c>60%</c> arrives as 0.6.</summary>
        public static DecorExtentSpec Fraction(float fraction)
            => new DecorExtentSpec(fraction, fraction, false, true);

        public static readonly DecorExtentSpec Native = new DecorExtentSpec(0f, 0f, true, false);
    }

    /// <summary>
    /// Parses the <c>&lt;Decor&gt;</c> value grammar (decor spec §4). Pure C# (no UnityEngine types)
    /// so the UIXmlLint CLI compiles it and reports the same errors the runtime would, from the same
    /// implementation.
    /// </summary>
    /// <remarks>
    /// Split into per-attribute parsing plus one cross-attribute <see cref="TryValidate"/> because
    /// <c>[UIAttr]</c> setters fire in no guaranteed order: nothing that needs to see two attributes
    /// at once can live in a setter. The control calls <see cref="TryValidate"/> once per pass from
    /// <c>OnAfterApply</c>, and the lint rules call it on the expanded attribute set.
    /// <para>
    /// Keywords are matched case-sensitively and lower-case, following <see cref="RadiusParser"/>:
    /// an author who writes <c>Bracket</c> gets an error naming the legal words rather than a silent
    /// fix-up that lint and runtime then have to agree on forever.
    /// </para>
    /// </remarks>
    public static class DecorParser
    {
        public const string BracketKeyword = "bracket";
        public const string TickKeyword = "tick";
        public const string LineKeyword = "line";
        public const string SpriteKeyword = "sprite";
        public const string NoneKeyword = "none";
        public const string NativeKeyword = "native";

        private const string LegalKinds =
            "'" + BracketKeyword + "', '" + TickKeyword + "', '" + LineKeyword + "', '" +
            SpriteKeyword + "' or '" + NoneKeyword + "'";

        private const string LegalSlots =
            "corners 'top-left' / 'top-right' / 'bottom-right' / 'bottom-left', " +
            "edges 'top' / 'bottom' / 'left' / 'right'";

        private static readonly DecorSlot[] AllCorners =
        {
            DecorSlot.TopLeft, DecorSlot.TopRight, DecorSlot.BottomRight, DecorSlot.BottomLeft,
        };

        private static readonly DecorSlot[] BottomEdge = { DecorSlot.Bottom };

        // ---- kind ----

        /// <summary>Throwing wrapper used by the runtime attribute setter.</summary>
        public static DecorKind ParseKind(string value)
            => TryParseKind(value, out var kind, out var error) ? kind : throw new ParseException(error);

        /// <summary>
        /// Null / empty parses to <see cref="DecorKind.None"/> (draws nothing) rather than an error,
        /// for the same reason <c>radius=""</c> is legal: a Variant overwrites values, it cannot
        /// remove an attribute, so the empty string is the only way back to "off".
        /// </summary>
        public static bool TryParseKind(string value, out DecorKind kind, out string error)
        {
            kind = DecorKind.None;
            error = null;

            if (string.IsNullOrWhiteSpace(value)) return true;

            switch (value.Trim())
            {
                case BracketKeyword: kind = DecorKind.Bracket; return true;
                case TickKeyword: kind = DecorKind.Tick; return true;
                case LineKeyword: kind = DecorKind.Line; return true;
                case SpriteKeyword: kind = DecorKind.Sprite; return true;
                case NoneKeyword: kind = DecorKind.None; return true;
            }

            error = $"<Decor> kind=\"{value.Trim()}\": unknown kind — expected {LegalKinds}";
            return false;
        }

        // ---- at ----

        /// <summary>Throwing wrapper used by the runtime attribute setter.</summary>
        public static DecorSlot[] ParseAt(string value)
            => TryParseAt(value, out var slots, out var error) ? slots : throw new ParseException(error);

        /// <summary>
        /// Parses the comma list, preserving the author's order (it is the sibling order the
        /// instances end up in). Null / empty yields a <c>null</c> list, meaning "defer to
        /// <see cref="DefaultSlots"/> for whatever the kind turns out to be" — which attribute
        /// order makes impossible to answer here.
        /// </summary>
        public static bool TryParseAt(string value, out DecorSlot[] slots, out string error)
        {
            slots = null;
            error = null;

            if (string.IsNullOrWhiteSpace(value)) return true;

            var raw = value.Trim();
            var parts = raw.Split(',');
            var parsed = new List<DecorSlot>(parts.Length);

            foreach (var part in parts)
            {
                var token = part.Trim();
                if (token.Length == 0)
                {
                    error = $"<Decor> at=\"{raw}\": empty segment — expected {LegalSlots}";
                    return false;
                }
                if (!TryParseSlot(token, out var slot))
                {
                    error = $"<Decor> at=\"{raw}\": unknown position '{token}' — expected {LegalSlots}";
                    return false;
                }
                // Two instances in one slot would sit on top of each other and double the glow —
                // always a typo, never an effect worth having.
                if (parsed.Contains(slot))
                {
                    error = $"<Decor> at=\"{raw}\": '{token}' is listed twice";
                    return false;
                }
                parsed.Add(slot);
            }

            slots = parsed.ToArray();
            return true;
        }

        private static bool TryParseSlot(string token, out DecorSlot slot)
        {
            switch (token)
            {
                case "top-left": slot = DecorSlot.TopLeft; return true;
                case "top-right": slot = DecorSlot.TopRight; return true;
                case "bottom-right": slot = DecorSlot.BottomRight; return true;
                case "bottom-left": slot = DecorSlot.BottomLeft; return true;
                case "top": slot = DecorSlot.Top; return true;
                case "bottom": slot = DecorSlot.Bottom; return true;
                case "left": slot = DecorSlot.Left; return true;
                case "right": slot = DecorSlot.Right; return true;
            }
            slot = DecorSlot.TopLeft;
            return false;
        }

        // ---- size ----

        /// <summary>Throwing wrapper used by the runtime attribute setter.</summary>
        public static DecorExtentSpec ParseExtent(string value)
            => TryParseExtent(value, out var size, out var error) ? size : throw new ParseException(error);

        /// <summary>
        /// Parses <c>W</c> / <c>WxH</c> / <c>P%</c> / <c>native</c>. Whether the last two are legal
        /// for the kind at hand is <see cref="TryValidate"/>'s question, not this one's.
        /// </summary>
        public static bool TryParseExtent(string value, out DecorExtentSpec size, out string error)
        {
            size = DecorExtentSpec.None;
            error = null;

            if (string.IsNullOrWhiteSpace(value)) return true;

            var raw = value.Trim();

            if (string.Equals(raw, NativeKeyword, StringComparison.Ordinal))
            {
                size = DecorExtentSpec.Native;
                return true;
            }

            if (raw.EndsWith("%", StringComparison.Ordinal))
            {
                if (!TryParseNumber(raw.Substring(0, raw.Length - 1), raw, "percentage",
                                    out var percent, out error))
                    return false;
                size = DecorExtentSpec.Fraction(percent / 100f);
                return true;
            }

            var axes = raw.Split('x');
            if (axes.Length > 2)
            {
                error = $"<Decor> extent=\"{raw}\": malformed size — expected 'W', 'WxH', 'P%' " +
                        $"(line) or '{NativeKeyword}' (sprite)";
                return false;
            }

            if (!TryParseNumber(axes[0], raw, "width", out var w, out error)) return false;
            if (axes.Length == 1)
            {
                size = DecorExtentSpec.Pixels(w, w);
                return true;
            }
            if (!TryParseNumber(axes[1], raw, "height", out var h, out error)) return false;

            size = DecorExtentSpec.Pixels(w, h);
            return true;
        }

        private static bool TryParseNumber(string token, string raw, string where,
                                           out float result, out string error)
        {
            result = 0f;
            error = null;
            var s = token.Trim();

            if (s.Length == 0)
            {
                error = $"<Decor> extent=\"{raw}\": {where} is empty";
                return false;
            }
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                error = $"<Decor> extent=\"{raw}\": {where} '{s}' is not a number";
                return false;
            }
            // "NaN" / "Infinity" parse fine under InvariantCulture, and NaN also slips past the
            // negative test below (every comparison with NaN is false).
            if (float.IsNaN(result) || float.IsInfinity(result))
            {
                result = 0f;
                error = $"<Decor> extent=\"{raw}\": {where} '{s}' is not a finite number";
                return false;
            }
            if (result < 0f)
            {
                error = $"<Decor> extent=\"{raw}\": {where} '{s}' is negative";
                return false;
            }
            return true;
        }

        // ---- cross-attribute ----

        /// <summary>
        /// The checks that need to see more than one attribute at once: a bracket wraps a corner and
        /// a tick points off an edge, so the two vocabularies do not mix; <c>%</c> only means
        /// something along an edge, and <c>native</c> only means something when there is a sprite.
        /// </summary>
        /// <param name="slots">The parsed <c>at=</c>, or null when the author left it out.</param>
        public static bool TryValidate(DecorKind kind, IReadOnlyList<DecorSlot> slots,
                                       DecorExtentSpec size, out string error)
        {
            error = null;

            if (slots != null)
            {
                for (var i = 0; i < slots.Count; i++)
                {
                    var slot = slots[i];
                    var isCorner = IsCornerSlot(slot);

                    if (kind == DecorKind.Bracket && !isCorner)
                    {
                        error = $"<Decor> kind=\"{BracketKeyword}\" at=\"{SlotName(slot)}\": a " +
                                $"{BracketKeyword} wraps a corner — use a corner position " +
                                "(top-left / top-right / bottom-right / bottom-left), or " +
                                $"kind=\"{LineKeyword}\" for an edge.";
                        return false;
                    }
                    if ((kind == DecorKind.Tick || kind == DecorKind.Line) && isCorner)
                    {
                        var word = kind == DecorKind.Tick ? TickKeyword : LineKeyword;
                        error = $"<Decor> kind=\"{word}\" at=\"{SlotName(slot)}\": a {word} sits " +
                                "along an edge — use an edge position (top / bottom / left / " +
                                $"right), or kind=\"{BracketKeyword}\" for a corner.";
                        return false;
                    }
                }
            }

            if (size.IsFraction && kind != DecorKind.Line)
            {
                error = $"<Decor> extent=\"…%\": a percentage is a share of the edge it runs along, " +
                        $"so it only applies to kind=\"{LineKeyword}\" — write pixels instead.";
                return false;
            }

            if (size.IsNative && kind != DecorKind.Sprite)
            {
                error = $"<Decor> extent=\"{NativeKeyword}\": '{NativeKeyword}' reads the sprite's " +
                        $"own pixel size, so it only applies to kind=\"{SpriteKeyword}\" — write " +
                        "pixels instead.";
                return false;
            }

            return true;
        }

        // ---- defaults ----

        /// <summary>
        /// Corners for the two kinds that frame something (bracket, sprite), the bottom edge for the
        /// two that underline it (tick, line).
        /// </summary>
        public static DecorSlot[] DefaultSlots(DecorKind kind)
            => kind == DecorKind.Tick || kind == DecorKind.Line ? BottomEdge : AllCorners;

        public static DecorExtentSpec DefaultExtent(DecorKind kind)
        {
            switch (kind)
            {
                case DecorKind.Tick: return DecorExtentSpec.Pixels(10f, 6f);
                case DecorKind.Line: return DecorExtentSpec.Fraction(1f);
                case DecorKind.Sprite: return DecorExtentSpec.Native;
                default: return DecorExtentSpec.Pixels(12f, 12f);
            }
        }

        public static bool IsCornerSlot(DecorSlot slot)
            => slot == DecorSlot.TopLeft || slot == DecorSlot.TopRight
               || slot == DecorSlot.BottomRight || slot == DecorSlot.BottomLeft;

        /// <summary>The author-facing spelling of a slot — the same word <c>at=</c> accepts.</summary>
        public static string SlotName(DecorSlot slot)
        {
            switch (slot)
            {
                case DecorSlot.TopLeft: return "top-left";
                case DecorSlot.TopRight: return "top-right";
                case DecorSlot.BottomRight: return "bottom-right";
                case DecorSlot.BottomLeft: return "bottom-left";
                case DecorSlot.Top: return "top";
                case DecorSlot.Bottom: return "bottom";
                case DecorSlot.Left: return "left";
                default: return "right";
            }
        }
    }
}
