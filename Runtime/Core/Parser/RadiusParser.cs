using System;
using System.Globalization;

namespace PromptUGUI.Parser
{
    /// <summary>How one corner is taken off the rectangle.</summary>
    /// <remarks>
    /// The numeric values are part of the contract: they ride into the shader as
    /// <c>_CornerKind</c> and are compared against the <c>PUGUI_CORNER_*</c> defines in
    /// <c>UI-PanelSDF.cginc</c>.
    /// </remarks>
    public enum CornerKind
    {
        /// <summary>Quarter circle — the only treatment that existed before corner treatments.</summary>
        Round = 0,
        /// <summary>Straight chamfer from <c>Width</c> along one edge to <c>Height</c> along the other.</summary>
        Cut = 1,
        /// <summary>Rectangular bite of <c>Width</c> × <c>Height</c> taken out of the corner.</summary>
        Notch = 2,
    }

    /// <summary>
    /// A whole-shape keyword, which overrides every per-corner value. Both members resolve against
    /// the live rect and are therefore deliberately left symbolic here — see
    /// <see cref="RadiusSpec"/>.
    /// </summary>
    /// <remarks>Numeric values ride into the shader as <c>_Shape</c>; see <c>PUGUI_SHAPE_*</c>.</remarks>
    public enum PanelShape
    {
        None = 0,
        /// <summary>Both short-axis ends fully rounded: radius = min(width, height) / 2.</summary>
        Pill = 1,
        /// <summary>Left and right sides drawn to a point at the vertical centre.</summary>
        Hexagon = 2,
    }

    /// <summary>One corner's treatment plus its two sizes, in canvas units.</summary>
    /// <remarks>
    /// <see cref="Height"/> mirrors <see cref="Width"/> for <see cref="CornerKind.Round"/> — a
    /// circle has only one size — so the shader can read one size pair per corner without first
    /// branching on the kind.
    /// </remarks>
    public readonly struct CornerSpec
    {
        public readonly CornerKind Kind;
        /// <summary>Reach along the horizontal edge (for Round: the radius).</summary>
        public readonly float Width;
        /// <summary>Reach along the vertical edge (for Round: the radius).</summary>
        public readonly float Height;
        /// <summary>
        /// Fillet radius for every vertex this treatment creates — the two chamfer ends of a
        /// <see cref="CornerKind.Cut"/>, the mouth and the reflex corner of a
        /// <see cref="CornerKind.Notch"/> (spec 2026-08-29 §5). Always zero for
        /// <see cref="CornerKind.Round"/>, which has no vertex to fillet.
        /// </summary>
        public readonly float Fillet;

        public CornerSpec(CornerKind kind, float width, float height)
            : this(kind, width, height, 0f)
        {
        }

        public CornerSpec(CornerKind kind, float width, float height, float fillet)
        {
            Kind = kind; Width = width; Height = height; Fillet = fillet;
        }

        public static CornerSpec Round(float radius)
            => new CornerSpec(CornerKind.Round, radius, radius);

        public static readonly CornerSpec Square = new CornerSpec(CornerKind.Round, 0f, 0f);

        /// <summary>
        /// True when this corner takes nothing off the rectangle. A treatment with either size at
        /// zero removes no area, whatever its keyword says.
        /// </summary>
        public bool IsSquare => Width <= 0f || Height <= 0f;
    }

    /// <summary>
    /// Corner spec produced by <see cref="RadiusParser"/>. Four independent corners in CSS
    /// <c>border-radius</c> order (clockwise from top-left) plus the whole-shape sentinels.
    /// </summary>
    /// <remarks>
    /// <see cref="Shape"/> is deliberately NOT resolved to numbers here: the pill radius is
    /// <c>min(width, height) / 2</c> and the hexagon tip is half the height, both of which depend
    /// on the live rect. Resolving them in C# would make two same-styled panels of different sizes
    /// carry different values and lose material sharing (see <c>ProceduralMaterialCache</c>) — the
    /// shader resolves them per-fragment from its own size input instead, for free.
    /// </remarks>
    public readonly struct RadiusSpec
    {
        public readonly CornerSpec TopLeftCorner;
        public readonly CornerSpec TopRightCorner;
        public readonly CornerSpec BottomRightCorner;
        public readonly CornerSpec BottomLeftCorner;

        public readonly PanelShape Shape;

        /// <summary>
        /// How far a <see cref="PanelShape.Hexagon"/> tip reaches in from the left/right edge.
        /// Zero means "auto": the shader takes half the height, giving a 45° tip.
        /// </summary>
        public readonly float HexWidth;

        public RadiusSpec(CornerSpec topLeft, CornerSpec topRight,
                          CornerSpec bottomRight, CornerSpec bottomLeft,
                          PanelShape shape = PanelShape.None, float hexWidth = 0f)
        {
            TopLeftCorner = topLeft;
            TopRightCorner = topRight;
            BottomRightCorner = bottomRight;
            BottomLeftCorner = bottomLeft;
            Shape = shape;
            HexWidth = hexWidth;
        }

        /// <summary>Four round corners — the shape of every spec that existed before treatments.</summary>
        public RadiusSpec(float tl, float tr, float br, float bl, bool isPill = false)
            : this(CornerSpec.Round(tl), CornerSpec.Round(tr),
                   CornerSpec.Round(br), CornerSpec.Round(bl),
                   isPill ? PanelShape.Pill : PanelShape.None)
        {
        }

        /// <summary>Horizontal reach of the top-left corner; its radius when the corner is round.</summary>
        public float TopLeft => TopLeftCorner.Width;
        public float TopRight => TopRightCorner.Width;
        public float BottomRight => BottomRightCorner.Width;
        public float BottomLeft => BottomLeftCorner.Width;

        public static readonly RadiusSpec Zero = new RadiusSpec(0f, 0f, 0f, 0f);
        public static readonly RadiusSpec Pill = new RadiusSpec(0f, 0f, 0f, 0f, true);

        /// <param name="fillet">
        /// Fillet radius for all six vertices. Rides in the four corners' <see cref="CornerSpec.Fillet"/>
        /// (their kind and size are overridden by the shader's hexagon sentinel; the fillet is the
        /// one thing that has to travel from here to the GPU).
        /// </param>
        public static RadiusSpec Hexagon(float hexWidth = 0f, float fillet = 0f)
        {
            var corner = new CornerSpec(CornerKind.Round, 0f, 0f, fillet);
            return new RadiusSpec(corner, corner, corner, corner, PanelShape.Hexagon, hexWidth);
        }

        public bool IsPill => Shape == PanelShape.Pill;

        /// <summary>True when the panel is a plain rectangle — no sentinel, no corner takes anything off.</summary>
        public bool IsZero => Shape == PanelShape.None
                              && TopLeftCorner.IsSquare && TopRightCorner.IsSquare
                              && BottomRightCorner.IsSquare && BottomLeftCorner.IsSquare;
    }

    /// <summary>
    /// Parses the <c>radius</c> attribute. Pure C# (no UnityEngine types) so the UIXmlLint CLI
    /// compiles it and surfaces syntax errors before the author ever opens Unity.
    /// </summary>
    /// <remarks>
    /// Grammar:
    /// <code>
    /// radius      := "" | whole-shape | corner-list
    /// whole-shape := "pill" | "hexagon" [ SP number ]
    /// corner-list := segment | segment "," segment "," segment "," segment
    /// whole-shape := "pill" | "hexagon" [ SP number ] [ SP fillet ]
    /// segment     := number | keyword SP size [ SP fillet ]
    /// keyword     := "cut" | "notch"
    /// size        := number [ "x" number ]
    /// fillet      := "r" number                       // one glued token: r8
    /// </code>
    /// Keywords are matched case-sensitively and lower-case: an author who writes <c>CUT</c> gets an
    /// error naming the legal words, which is a better outcome than a silent case fix-up that lint
    /// and runtime then have to agree on forever. The fillet's <c>r</c> is glued to its number for
    /// the same reason the <c>x</c> in <c>WxH</c> is (spec 2026-08-29 §4).
    /// </remarks>
    public static class RadiusParser
    {
        public const string PillKeyword = "pill";
        public const string HexagonKeyword = "hexagon";
        public const string CutKeyword = "cut";
        public const string NotchKeyword = "notch";
        public const string FilletPrefix = "r";

        private const string LegalCornerWords =
            "a number (round), '" + CutKeyword + " W' / '" + CutKeyword + " WxH', '" +
            NotchKeyword + " W' / '" + NotchKeyword + " WxH' — each optionally followed by a " +
            "fillet 'rN' (e.g. '" + CutKeyword + " 16 r6')";

        /// <summary>Throwing wrapper used by the runtime attribute setters.</summary>
        public static RadiusSpec Parse(string value)
            => TryParse(value, out var spec, out var error) ? spec : throw new ParseException(error);

        /// <summary>
        /// Null / empty parses to <see cref="RadiusSpec.Zero"/> (square corners) rather than an
        /// error — a Variant can only override an attribute's value, never remove it, so
        /// <c>radius.desktop=""</c> is the only way back to square and must stay legal.
        /// </summary>
        public static bool TryParse(string value, out RadiusSpec spec, out string error)
        {
            spec = RadiusSpec.Zero;
            error = null;

            if (string.IsNullOrWhiteSpace(value)) return true;

            var raw = value.Trim();

            // Whole-shape keywords span the entire value, so they are settled before the value is
            // ever split on commas — otherwise "hexagon 32" would look like one malformed segment.
            if (TryParseWholeShape(raw, out spec, out error, out var wasWholeShape)) return true;
            if (wasWholeShape) return false;

            var parts = raw.Split(',');

            foreach (var part in parts)
            {
                var keyword = FirstToken(part);
                if (!string.Equals(keyword, PillKeyword, StringComparison.Ordinal)
                    && !string.Equals(keyword, HexagonKeyword, StringComparison.Ordinal))
                    continue;
                error = $"radius=\"{raw}\": '{keyword}' is a whole-shape keyword and cannot be " +
                        $"mixed with per-corner values (write radius=\"{keyword}\" on its own)";
                return false;
            }

            if (parts.Length != 1 && parts.Length != 4)
            {
                error = $"radius=\"{raw}\": expected 1 value (all corners), 4 values " +
                        "(top-left,top-right,bottom-right,bottom-left) or a whole-shape keyword " +
                        $"('{PillKeyword}' / '{HexagonKeyword}') — got {parts.Length} " +
                        "comma-separated values";
                return false;
            }

            if (parts.Length == 1)
            {
                if (!TryParseCorner(parts[0], raw, "value", out var all, out error)) return false;
                spec = new RadiusSpec(all, all, all, all);
                return true;
            }

            if (!TryParseCorner(parts[0], raw, "top-left", out var tl, out error)) return false;
            if (!TryParseCorner(parts[1], raw, "top-right", out var tr, out error)) return false;
            if (!TryParseCorner(parts[2], raw, "bottom-right", out var br, out error)) return false;
            if (!TryParseCorner(parts[3], raw, "bottom-left", out var bl, out error)) return false;

            spec = new RadiusSpec(tl, tr, br, bl);
            return true;
        }

        /// <param name="wasWholeShape">
        /// True when the value opened with a whole-shape keyword, so a false return means "that
        /// keyword was written wrong" rather than "this is a corner list" — without it the caller
        /// would fall through and report a far less useful segment error.
        /// </param>
        private static bool TryParseWholeShape(string raw, out RadiusSpec spec, out string error,
                                               out bool wasWholeShape)
        {
            spec = RadiusSpec.Zero;
            error = null;
            wasWholeShape = false;

            // A comma means the author is writing a corner list. Handing those straight back lets
            // the mix check own them, and "cannot be mixed with per-corner values" reads far better
            // than a complaint about the keyword's own size grammar.
            if (raw.IndexOf(',') >= 0) return false;

            var tokens = Tokenize(raw);
            if (tokens.Length == 0) return false;

            if (string.Equals(tokens[0], PillKeyword, StringComparison.Ordinal))
            {
                wasWholeShape = true;
                if (tokens.Length > 1)
                {
                    error = $"radius=\"{raw}\": '{PillKeyword}' takes no size or fillet — it is " +
                            "always min(width, height) / 2, and already all arc";
                    return false;
                }
                spec = RadiusSpec.Pill;
                return true;
            }

            if (!string.Equals(tokens[0], HexagonKeyword, StringComparison.Ordinal)) return false;
            wasWholeShape = true;

            // hexagon [size] [fillet] — both optional, in that order.
            var next = 1;
            var w = 0f;
            var fillet = 0f;
            if (next < tokens.Length && !IsFilletToken(tokens[next]))
            {
                if (tokens[next].IndexOf('x') >= 0)
                {
                    error = $"radius=\"{raw}\": '{HexagonKeyword}' takes a single horizontal size — " +
                            "its tip height is always half the rect, so there is no second axis to set";
                    return false;
                }
                if (!TryParseNumber(tokens[next], raw, $"'{HexagonKeyword}' size", out w, out error))
                    return false;
                next++;
            }
            if (next < tokens.Length)
            {
                if (!TryParseFillet(tokens[next], raw, $"'{HexagonKeyword}'", out fillet, out error))
                    return false;
                next++;
            }
            if (next < tokens.Length)
            {
                error = $"radius=\"{raw}\": '{HexagonKeyword}' takes at most a size then a fillet " +
                        $"(write radius=\"{HexagonKeyword}\", \"{HexagonKeyword} 32\", " +
                        $"\"{HexagonKeyword} r6\" or \"{HexagonKeyword} 32 r6\")";
                return false;
            }

            spec = RadiusSpec.Hexagon(w, fillet);
            return true;
        }

        private static bool TryParseCorner(string segment, string raw, string corner,
                                           out CornerSpec result, out string error)
        {
            result = CornerSpec.Square;
            error = null;
            var s = segment.Trim();

            if (s.Length == 0)
            {
                error = $"radius=\"{raw}\": {corner} segment is empty";
                return false;
            }

            var tokens = Tokenize(s);

            if (tokens.Length == 1)
            {
                if (IsCornerKeyword(tokens[0]))
                {
                    error = $"radius=\"{raw}\": {corner} segment '{s}' needs a size " +
                            $"(write '{tokens[0]} 16' or '{tokens[0]} 24x16')";
                    return false;
                }
                if (!TryParseNumber(tokens[0], raw, $"{corner} segment", out var r, out error))
                {
                    error += $" — expected {LegalCornerWords}";
                    return false;
                }
                result = CornerSpec.Round(r);
                return true;
            }

            if (!TryParseKind(tokens[0], out var kind))
            {
                if (IsNumber(tokens[0]) && IsFilletToken(tokens[1]))
                {
                    // "16 r4": a round corner is already all arc; there is no vertex to fillet.
                    error = $"radius=\"{raw}\": {corner} segment '{s}' — a bare number is already " +
                            $"a round corner and takes no fillet (write '{tokens[0]}' alone)";
                    return false;
                }
                error = $"radius=\"{raw}\": {corner} segment '{s}' starts with an unknown " +
                        $"keyword '{tokens[0]}' — expected {LegalCornerWords}";
                return false;
            }

            if (tokens.Length == 2 && IsFilletToken(tokens[1]))
            {
                error = $"radius=\"{raw}\": {corner} segment '{s}' needs a size before the fillet " +
                        $"(write '{tokens[0]} 16 {tokens[1]}' or '{tokens[0]} 24x16 {tokens[1]}')";
                return false;
            }

            if (tokens.Length == 4 && string.Equals(tokens[2], FilletPrefix, StringComparison.Ordinal))
            {
                // "cut 16 r 8" is the one malformed shape worth its own message: "too many parts"
                // would send the author looking at the size.
                error = $"radius=\"{raw}\": {corner} segment '{s}' — write the fillet as " +
                        $"'{FilletPrefix}{tokens[3]}' with no space " +
                        $"(e.g. '{tokens[0]} {tokens[1]} {FilletPrefix}{tokens[3]}')";
                return false;
            }

            if (tokens.Length > 3)
            {
                error = $"radius=\"{raw}\": {corner} segment '{s}' has too many parts — " +
                        $"expected {LegalCornerWords}";
                return false;
            }

            if (!TryParseSize(tokens[1], raw, $"{corner} segment '{s}'", out var w, out var h,
                              out error))
                return false;

            var fillet = 0f;
            if (tokens.Length == 3
                && !TryParseFillet(tokens[2], raw, $"{corner} segment '{s}'", out fillet, out error))
                return false;

            result = new CornerSpec(kind, w, h, fillet);
            return true;
        }

        /// <summary>Parses the glued <c>rN</c> fillet token.</summary>
        private static bool TryParseFillet(string token, string raw, string where,
                                           out float fillet, out string error)
        {
            fillet = 0f;
            error = null;

            if (!IsFilletToken(token))
            {
                error = $"radius=\"{raw}\": {where} has an unexpected trailing part '{token}' — " +
                        $"a fillet is written '{FilletPrefix}N' (e.g. {FilletPrefix}8)";
                return false;
            }
            if (!TryParseNumber(token.Substring(FilletPrefix.Length), raw, $"{where} fillet",
                                out fillet, out _))
            {
                error = $"radius=\"{raw}\": {where} fillet '{token}' must be '{FilletPrefix}' " +
                        "followed by a finite non-negative number (e.g. r8, r2.5)";
                return false;
            }
            return true;
        }

        /// <summary>True for anything that opens like a fillet (<c>r…</c>), well-formed or not.</summary>
        private static bool IsFilletToken(string token)
            => token.StartsWith(FilletPrefix, StringComparison.Ordinal);

        private static bool IsNumber(string token)
            => float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        /// <summary>Parses <c>W</c> or <c>WxH</c>; a lone size squares itself (45° cut, square notch).</summary>
        private static bool TryParseSize(string token, string raw, string where,
                                         out float width, out float height, out string error)
        {
            width = 0f;
            height = 0f;

            var axes = token.Split('x');
            if (axes.Length > 2)
            {
                error = $"radius=\"{raw}\": {where} has a malformed size '{token}' — " +
                        "expected 'W' or 'WxH'";
                return false;
            }

            if (!TryParseNumber(axes[0], raw, $"{where} width", out width, out error)) return false;
            if (axes.Length == 1)
            {
                height = width;
                return true;
            }
            return TryParseNumber(axes[1], raw, $"{where} height", out height, out error);
        }

        private static bool TryParseNumber(string token, string raw, string where,
                                           out float result, out string error)
        {
            result = 0f;
            error = null;
            var s = token.Trim();

            if (s.Length == 0)
            {
                error = $"radius=\"{raw}\": {where} is empty";
                return false;
            }
            if (!float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out result))
            {
                error = $"radius=\"{raw}\": {where} '{s}' is not a number";
                return false;
            }
            // "NaN" / "Infinity" parse fine under InvariantCulture, and NaN also slips past the
            // negative test below (every comparison with NaN is false).
            if (float.IsNaN(result) || float.IsInfinity(result))
            {
                result = 0f;
                error = $"radius=\"{raw}\": {where} '{s}' is not a finite number";
                return false;
            }
            if (result < 0f)
            {
                error = $"radius=\"{raw}\": {where} '{s}' is negative";
                return false;
            }
            return true;
        }

        private static bool TryParseKind(string token, out CornerKind kind)
        {
            if (string.Equals(token, CutKeyword, StringComparison.Ordinal))
            {
                kind = CornerKind.Cut;
                return true;
            }
            if (string.Equals(token, NotchKeyword, StringComparison.Ordinal))
            {
                kind = CornerKind.Notch;
                return true;
            }
            kind = CornerKind.Round;
            return false;
        }

        private static bool IsCornerKeyword(string token)
            => TryParseKind(token, out _);

        private static string FirstToken(string segment)
        {
            var tokens = Tokenize(segment);
            return tokens.Length == 0 ? string.Empty : tokens[0];
        }

        private static string[] Tokenize(string s)
            => s.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
    }
}
