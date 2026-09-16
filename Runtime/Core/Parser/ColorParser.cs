namespace PromptUGUI.Parser
{
    /// <summary>
    /// Pure C# color parser (no UnityEngine dependency).
    /// Matches ColorUtility.TryParseHtmlString behavior: accepts hex literals and CSS named colors.
    /// Used by both UIDocumentParser (runtime) and UIXmlLint CLI (build-time).
    /// </summary>
    internal static class ColorParser
    {
        /// <summary>
        /// Validates a color string without parsing the actual color values.
        /// Accepted formats: #RGB, #RRGGBB, #RGBA, #RRGGBBAA, or any CSS named color
        /// from the Unity 6 ColorUtility.TryParseHtmlString documented set (case-insensitive).
        /// </summary>
        public static bool TryParseHtmlString(string htmlString)
        {
            if (string.IsNullOrEmpty(htmlString)) return false;

            // Hex form
            if (htmlString[0] == '#')
            {
                var len = htmlString.Length;

                // ColorUtility accepts: #RGB (4 chars), #RRGGBB (7 chars), #RGBA (5 chars), #RRGGBBAA (9 chars)
                if (len != 4 && len != 5 && len != 7 && len != 9)
                    return false;

                // Validate all characters after # are hex digits
                for (var i = 1; i < len; i++)
                {
                    if (!IsHexDigit(htmlString[i]))
                        return false;
                }

                return true;
            }

            // Named color (case-insensitive). Set matches Unity 6 ColorUtility.TryParseHtmlString.
            return NamedColors.Contains(htmlString.ToLowerInvariant());
        }

        /// <summary>
        /// Splits an optional trailing alpha suffix off a colour <em>reference</em> value.
        /// <c>"black/0.5"</c> → base <c>"black"</c>, alpha <c>0.5</c>; <c>"#ff0000/0.3"</c> →
        /// base <c>"#ff0000"</c>, alpha <c>0.3</c>; <c>"primary"</c> → base <c>"primary"</c>,
        /// alpha <c>null</c> (no suffix). The suffix is the text after the LAST '/'; colour
        /// tokens are <c>[a-z0-9-]</c>, hex is <c>#...</c>, named colours are alphabetic —
        /// none contain '/', so the split is unambiguous. Alpha is a 0..1 float and REPLACES
        /// the resolved colour's own alpha (Unity <c>Color.a</c> semantics).
        /// Returns false (with <paramref name="error"/> set) when a '/' is present but the
        /// part before it is empty, or the suffix is empty / non-numeric / out of 0..1.
        /// </summary>
        public static bool TrySplitAlpha(string raw, out string baseValue, out float? alpha, out string error)
        {
            baseValue = raw;
            alpha = null;
            error = null;
            if (string.IsNullOrEmpty(raw)) return true;   // empty handled by caller

            var slash = raw.LastIndexOf('/');
            if (slash < 0) return true;                   // no suffix → value unchanged

            var head = raw.Substring(0, slash);
            var tail = raw.Substring(slash + 1);

            if (head.Length == 0)
            {
                error = $"color \"{raw}\": missing colour before the '/' alpha suffix";
                return false;
            }
            if (tail.Length == 0
                || !float.TryParse(tail, System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out var a))
            {
                error = $"color \"{raw}\": alpha after '/' must be a number in 0..1 (e.g. \"black/0.5\")";
                return false;
            }
            if (a < 0f || a > 1f)
            {
                error = $"color \"{raw}\": alpha {tail} is out of range — must be 0..1";
                return false;
            }

            baseValue = head;
            alpha = a;
            return true;
        }

        /// <summary>
        /// Splits an optional trailing stop position off one gradient <em>segment</em>.
        /// <c>"#fff 70%"</c> → base <c>"#fff"</c>, stop <c>0.7</c>; <c>"primary/0.45 70%"</c> →
        /// base <c>"primary/0.45"</c>, stop <c>0.7</c>; <c>"#fff"</c> → base unchanged, stop
        /// <c>null</c>. The position is the whitespace-separated tail and is written as a
        /// percentage (CSS <c>linear-gradient</c> spelling); it is returned NORMALIZED to 0..1,
        /// measured from the TOP edge — the same direction as "the first colour is the top one".
        ///
        /// <para>The split is unambiguous because no colour form contains whitespace: theme tokens
        /// are <c>[a-z0-9-]</c>, hex is <c>#…</c>, CSS names are alphabetic, and the <c>/alpha</c>
        /// suffix is glued to its colour. Stripping the position FIRST is what lets
        /// <see cref="TrySplitAlpha"/> keep using the last '/' unchanged.</para>
        /// </summary>
        public static bool TrySplitStop(string raw, out string baseValue, out float? stop, out string error)
        {
            baseValue = raw;
            stop = null;
            error = null;
            if (string.IsNullOrEmpty(raw)) return true;   // empty handled by caller

            var parts = raw.Split(Whitespace, System.StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 2)
            {
                error = $"color \"{raw}\": a colour may carry at most one stop position (e.g. \"#fff 70%\")";
                return false;
            }
            if (parts.Length < 2)
            {
                // A lone "70%" is a position that lost its colour — say that, rather than letting it
                // fall through to "invalid color literal" two layers down.
                if (parts.Length == 1 && parts[0].Length > 1 && parts[0][parts[0].Length - 1] == '%')
                {
                    error = $"color \"{raw}\": a stop position needs a colour before it " +
                            "(e.g. \"#fff 70%\")";
                    return false;
                }
                return true;
            }

            var tail = parts[1];
            if (tail.Length < 2
                || tail[tail.Length - 1] != '%'
                || !float.TryParse(tail.Substring(0, tail.Length - 1),
                                   System.Globalization.NumberStyles.Float,
                                   System.Globalization.CultureInfo.InvariantCulture, out var percent))
            {
                error = $"color \"{raw}\": the stop position must be a percentage (e.g. \"70%\")";
                return false;
            }
            if (percent < 0f || percent > 100f)
            {
                error = $"color \"{raw}\": stop position {tail} is out of range — must be 0%..100%";
                return false;
            }

            baseValue = parts[0];
            stop = percent / 100f;
            return true;
        }

        /// <summary>
        /// One gradient value, taken apart (spec 2026-09-17 §5.1): the direction, 1..4 colour segments
        /// (one for a solid), the stop position the author wrote for each (null = defaulted), and the
        /// colour hint written between each neighbouring pair (null = that segment is linear).
        /// Bundled because the numbers are meaningless apart — <see cref="CurveExponents"/> needs
        /// each hint together with the two stops around it.
        /// </summary>
        public readonly struct GradientParts
        {
            public readonly GradientDirection Direction;
            /// <summary>Colour text per stop, positions already stripped. Length 1 for a solid.</summary>
            public readonly string[] Colours;
            /// <summary>Authored position per stop, 0..1 along the gradient line; null when unwritten.</summary>
            public readonly float?[] Stops;
            /// <summary>
            /// CSS colour hint per segment (length <c>Count − 1</c>): where the two colours are mixed half
            /// and half. Bends that segment into a power curve rather than cutting it, so there is no slope
            /// discontinuity to read as a dividing line (spec 2026-08-30 §14).
            /// </summary>
            public readonly float?[] Hints;

            public GradientParts(GradientDirection direction, string[] colours, float?[] stops, float?[] hints)
            {
                Direction = direction;
                Colours = colours;
                Stops = stops;
                Hints = hints;
            }

            public int Count => Colours.Length;
            public bool IsGradient => Colours.Length > 1;

            /// <summary>
            /// Every stop's position with the CSS defaults filled in: the first unwritten one is 0, the
            /// last is 1, and unwritten middles are spread evenly between their nearest written
            /// neighbours (<c>A, B, C 40%, D</c> → 0, 0.2, 0.4, 1).
            /// </summary>
            public float[] EffectiveStops()
            {
                var n = Colours.Length;
                var s = new float[n];
                if (n == 0) return s;
                s[0] = Stops[0] ?? 0f;
                if (n == 1) return s;
                s[n - 1] = Stops[n - 1] ?? 1f;
                for (var i = 1; i < n - 1; i++)
                {
                    if (Stops[i].HasValue) { s[i] = Stops[i].Value; continue; }
                    // Run of unwritten stops i..j−1, bracketed by written (or end) stops at i−1 and j.
                    var j = i + 1;
                    while (j < n - 1 && !Stops[j].HasValue) j++;
                    var lo = s[i - 1];
                    var hi = j == n - 1 ? s[n - 1] : Stops[j].Value;
                    var runLength = j - (i - 1);
                    for (var k = i; k < j; k++)
                        s[k] = lo + (hi - lo) * (k - (i - 1)) / runLength;
                    i = j - 1;
                }
                return s;
            }

            /// <summary>The power each segment's ramp is raised to (length <c>Count − 1</c>); 1 = linear.</summary>
            public float[] CurveExponents()
            {
                var stops = EffectiveStops();
                var e = new float[Hints.Length];
                for (var i = 0; i < e.Length; i++)
                    e[i] = StopCurveExponent(stops[i], stops[i + 1], Hints[i]);
                return e;
            }
        }

        /// <summary>
        /// The power the normalized ramp is raised to so the two colours mix half and half at the
        /// hint. <c>1</c> — no hint — is the plain linear ramp.
        ///
        /// <para>Solving <c>t^E = 0.5</c> gives <c>E = log(0.5) / log(t)</c>, where <c>t</c> is the
        /// hint's position WITHIN the ramp (CSS puts the hint in the same coordinate space as the
        /// stops, so a hint exactly midway between them is the linear case and comes out as 1).</para>
        ///
        /// <para>Both ends are degenerate — a hint sitting on a stop means "flip instantly there",
        /// which is an infinite or zero exponent. Clamping <c>t</c> just inside the open interval
        /// keeps the exponent finite and lands on a hard edge anyway, which is the same picture.</para>
        /// </summary>
        public static float StopCurveExponent(float topStop, float bottomStop, float? hint)
        {
            if (!hint.HasValue) return 1f;
            var span = bottomStop - topStop;
            if (span <= 0f) return 1f;                 // already a hard edge; nothing left to shape

            var t = (hint.Value - topStop) / span;
            if (t < HintEpsilon) t = HintEpsilon;
            else if (t > 1f - HintEpsilon) t = 1f - HintEpsilon;
            return (float)(System.Math.Log(0.5) / System.Math.Log(t));
        }

        private const float HintEpsilon = 1e-3f;

        /// <summary>
        /// Whether a segment is spelled like a gradient direction — <c>&lt;N&gt;deg</c> or
        /// <c>to …</c> — as opposed to a colour. Shape only: a direction that then fails to parse is
        /// still a direction, and gets the direction grammar's error rather than "invalid colour".
        /// No colour form collides: hex starts with '#', CSS names are alphabetic, a hint ends in
        /// '%', and a token cannot contain whitespace. A token COULD be spelled <c>45deg</c>, which
        /// is why the theme parser refuses to declare one (spec 2026-09-17 §8).
        /// </summary>
        public static bool LooksLikeDirection(string segment)
        {
            if (string.IsNullOrEmpty(segment)) return false;
            if (segment.Length >= 2
                && (segment[0] == 't' || segment[0] == 'T')
                && (segment[1] == 'o' || segment[1] == 'O')
                && (segment.Length == 2 || System.Array.IndexOf(Whitespace, segment[2]) >= 0))
                return true;
            return LooksLikeAngle(segment);
        }

        private static bool LooksLikeAngle(string s)
        {
            if (s.Length < 4) return false;
            if (!s.EndsWith("deg", System.StringComparison.OrdinalIgnoreCase)) return false;
            var head = s.Substring(0, s.Length - 3);
            var i = 0;
            if (head[0] == '-' || head[0] == '+') i = 1;
            if (i >= head.Length) return false;
            var digits = 0;
            var dots = 0;
            for (; i < head.Length; i++)
            {
                var c = head[i];
                if (c >= '0' && c <= '9') digits++;
                else if (c == '.' && dots == 0) dots++;
                else return false;
            }
            return digits > 0;
        }

        private const string DirectionGrammar =
            "a direction is \"<N>deg\" or \"to <side or corner>\" — top / bottom / left / right, " +
            "or a vertical + horizontal pair (\"to bottom right\")";

        /// <summary>
        /// Parses one direction segment (spec 2026-09-17 §3.1). Angles follow CSS: 0deg up, 90deg
        /// right, 180deg down, 270deg left, normalized into [0, 360). <c>to &lt;side&gt;</c> is the
        /// matching angle; <c>to &lt;side&gt; &lt;side&gt;</c> (one vertical, one horizontal, either
        /// order) is a corner, whose angle is resolved later against the box's aspect ratio.
        /// </summary>
        public static bool TryParseDirection(string segment, out GradientDirection direction, out string error)
        {
            direction = GradientDirection.Default;
            error = null;

            if (LooksLikeAngle(segment))
            {
                var num = segment.Substring(0, segment.Length - 3);
                if (!float.TryParse(num, System.Globalization.NumberStyles.Float,
                                    System.Globalization.CultureInfo.InvariantCulture, out var deg))
                {
                    error = $"color \"{segment}\": {DirectionGrammar}";
                    return false;
                }
                direction = GradientDirection.Angle(deg);
                return true;
            }

            var words = segment.Split(Whitespace, System.StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 2 || words.Length > 3
                || !string.Equals(words[0], "to", System.StringComparison.OrdinalIgnoreCase))
            {
                error = $"color \"{segment}\": {DirectionGrammar}";
                return false;
            }

            int x = 0, y = 0;
            for (var i = 1; i < words.Length; i++)
            {
                switch (words[i].ToLowerInvariant())
                {
                    case "top": if (y != 0) goto bad; y = 1; break;
                    case "bottom": if (y != 0) goto bad; y = -1; break;
                    case "left": if (x != 0) goto bad; x = -1; break;
                    case "right": if (x != 0) goto bad; x = 1; break;
                    default: goto bad;
                }
            }

            if (x != 0 && y != 0) { direction = GradientDirection.Corner(x, y); return true; }
            if (y > 0) { direction = GradientDirection.Angle(0f); return true; }
            if (x > 0) { direction = GradientDirection.Angle(90f); return true; }
            if (y < 0) { direction = GradientDirection.Angle(180f); return true; }
            direction = GradientDirection.Angle(270f);
            return true;

        bad:
            error = $"color \"{segment}\": {DirectionGrammar}";
            return false;
        }

        /// <summary>
        /// The full split of a colour value (spec 2026-09-17 §3.1): an optional leading direction,
        /// then 1..4 colour segments, each with an optional stop position (<c>"#fff 70%"</c>, 0..1
        /// along the gradient line, null when unwritten), with an optional bare-percentage colour
        /// hint between any two of them (<c>"A, 70%, B"</c>). Segments are trimmed and their
        /// positions are stripped, so callers that only validate colours never see <c>"#fff 70%"</c>;
        /// segment CONTENTS (token / hex / <c>/alpha</c>) are not validated here, only the shape.
        ///
        /// <para>Segment classification is unambiguous: a direction contains whitespace or ends in
        /// <c>deg</c>, a hint ends in '%', and no colour spelling does either.</para>
        /// </summary>
        public static bool TrySplitGradient(string raw, out GradientParts parts, out string error)
        {
            parts = new GradientParts(GradientDirection.Default, new[] { raw }, new float?[1], System.Array.Empty<float?>());
            error = null;
            if (string.IsNullOrEmpty(raw)) return true;   // empty handled by caller

            var segments = raw.Split(',');
            for (var i = 0; i < segments.Length; i++)
            {
                segments[i] = segments[i].Trim();
                if (segments[i].Length != 0) continue;
                error = $"color \"{raw}\": gradient segment is empty — expected \"A, B\"";
                return false;
            }

            var direction = GradientDirection.Default;
            var first = 0;
            if (LooksLikeDirection(segments[0]))
            {
                if (!TryParseDirection(segments[0], out direction, out error)) return false;
                first = 1;
            }

            var colours = new System.Collections.Generic.List<string>(4);
            var stops = new System.Collections.Generic.List<float?>(4);
            var hints = new System.Collections.Generic.List<float?>(3);
            var pendingHint = (float?)null;
            var lastWasHint = false;

            for (var i = first; i < segments.Length; i++)
            {
                var seg = segments[i];

                if (LooksLikeDirection(seg))
                {
                    error = $"color \"{raw}\": the direction must be the first segment " +
                            "(\"to right, A, B\")";
                    return false;
                }

                if (TryParsePercent(seg, out var hint))
                {
                    if (colours.Count == 0 || lastWasHint)
                    {
                        error = $"color \"{raw}\": a colour hint must sit BETWEEN two colours " +
                                "(\"A, 70%, B\") — on its own there is nothing for it to bend";
                        return false;
                    }
                    pendingHint = hint;
                    lastWasHint = true;
                    continue;
                }

                // "45deg #fff": a direction glued to its first colour. Caught here, before TrySplitStop
                // would call the tail a bad stop position.
                var space = seg.IndexOfAny(Whitespace);
                if (space > 0 && LooksLikeAngle(seg.Substring(0, space)))
                {
                    error = $"color \"{raw}\": the direction must be its own comma-separated segment " +
                            "(\"45deg, #fff, #000\")";
                    return false;
                }

                if (!TrySplitStop(seg, out var colour, out var stop, out error)) return false;

                if (colours.Count > 0)
                {
                    hints.Add(pendingHint);
                    pendingHint = null;
                }
                colours.Add(colour);
                stops.Add(stop);
                lastWasHint = false;
            }

            if (lastWasHint)
            {
                error = $"color \"{raw}\": a colour hint must sit BETWEEN two colours " +
                        "(\"A, 70%, B\") — on its own there is nothing for it to bend";
                return false;
            }

            if (colours.Count == 1)
            {
                if (first == 1)
                {
                    error = $"color \"{raw}\": a direction needs at least two colours " +
                            "(\"to right, A, B\")";
                    return false;
                }
                if (stops[0].HasValue)
                {
                    error = $"color \"{raw}\": a stop position needs a two-colour gradient " +
                            "(e.g. \"A 70%,B\") — a solid colour has no transition point to move";
                    return false;
                }
                parts = new GradientParts(direction, colours.ToArray(), stops.ToArray(), System.Array.Empty<float?>());
                return true;
            }

            if (colours.Count > MaxStops)
            {
                error = $"color \"{raw}\": gradient supports 2 to {MaxStops} colours " +
                        $"(\"A, B, C, D\"), got {colours.Count}";
                return false;
            }

            parts = new GradientParts(direction, colours.ToArray(), stops.ToArray(), hints.ToArray());

            // Compared as the shader will see them, so "A 70%,B" (0.7 → 1) passes and "A 70%,B 30%"
            // does not. Equal is a legal hard edge, not a mistake.
            var eff = parts.EffectiveStops();
            for (var i = 1; i < eff.Length; i++)
            {
                if (eff[i] >= eff[i - 1]) continue;
                error = $"color \"{raw}\": stop positions must not decrease along the gradient " +
                        $"({eff[i - 1] * 100f:0.###}% then {eff[i] * 100f:0.###}%)";
                return false;
            }

            for (var i = 0; i < parts.Hints.Length; i++)
            {
                var h = parts.Hints[i];
                if (!h.HasValue || (h.Value >= eff[i] && h.Value <= eff[i + 1])) continue;
                error = $"color \"{raw}\": the hint must sit between the two stop positions of its " +
                        $"segment ({eff[i] * 100f:0.###}%..{eff[i + 1] * 100f:0.###}%) — outside them " +
                        "there is no transition left to bend";
                return false;
            }

            return true;
        }

        /// <summary>Most colour stops a gradient may carry (spec 2026-09-17 LG-D2): the SDF shaders
        /// keep one uniform slot per stop, and the vertex path shares the limit so one value means
        /// the same thing everywhere.</summary>
        public const int MaxStops = 4;
        /// <summary>
        /// A segment that is nothing but a percentage — the colour-hint form. No colour spelling can
        /// end in '%' (tokens are <c>[a-z0-9-]</c>, hex is <c>#…</c>, CSS names are alphabetic), so
        /// this never collides with a colour.
        /// </summary>
        private static bool TryParsePercent(string segment, out float value)
        {
            value = 0f;
            if (segment.Length < 2 || segment[segment.Length - 1] != '%') return false;
            if (segment.IndexOfAny(Whitespace) >= 0) return false;
            if (!float.TryParse(segment.Substring(0, segment.Length - 1),
                                System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var percent))
                return false;
            if (percent < 0f || percent > 100f) return false;
            value = percent / 100f;
            return true;
        }

        private static readonly char[] Whitespace = { ' ', '\t', '\n', '\r' };

        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }

        private static readonly System.Collections.Generic.HashSet<string> NamedColors =
            new System.Collections.Generic.HashSet<string>
            {
                "red", "cyan", "blue", "darkblue", "lightblue", "purple", "yellow",
                "lime", "fuchsia", "white", "silver", "grey", "gray", "black",
                "orange", "brown", "maroon", "green", "olive", "navy", "teal",
                "aqua", "magenta", "transparent"
            };
    }
}
