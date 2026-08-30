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
        /// One gradient value, taken apart: the two colour segments (bottom null for a solid), their
        /// optional stop positions, and the optional colour hint between them. Bundled rather than
        /// returned as six out-parameters because the four numbers are meaningless without each
        /// other — <see cref="CurveExponent"/> needs all three.
        /// </summary>
        public readonly struct GradientParts
        {
            public readonly string Top;
            /// <summary>Null when the value names a single colour.</summary>
            public readonly string Bottom;
            public readonly float? TopStop;
            public readonly float? BottomStop;
            /// <summary>
            /// CSS colour hint: where the two colours are mixed half and half. Bends the ramp into a
            /// power curve rather than cutting it, so there is no slope discontinuity to read as a
            /// dividing line — which is what a moved stop position gives you and what the eye picks
            /// up as a Mach band (spec 2026-08-30 §14).
            /// </summary>
            public readonly float? Hint;

            public GradientParts(string top, string bottom, float? topStop, float? bottomStop, float? hint)
            {
                Top = top;
                Bottom = bottom;
                TopStop = topStop;
                BottomStop = bottomStop;
                Hint = hint;
            }

            /// <summary>Written or defaulted: the ramp starts at the top edge.</summary>
            public float EffectiveTopStop => TopStop ?? 0f;
            /// <summary>Written or defaulted: the ramp ends at the bottom edge.</summary>
            public float EffectiveBottomStop => BottomStop ?? 1f;

            public float CurveExponent
                => StopCurveExponent(EffectiveTopStop, EffectiveBottomStop, Hint);
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
        /// Splits an optional two-stop gradient value on ','. <c>"#fff,#000"</c> → top <c>"#fff"</c>,
        /// bottom <c>"#000"</c>; no comma → top = raw, bottom = null. Segments are trimmed (authors
        /// write <c>"a, b"</c>) and their stop positions are stripped off, so callers that only
        /// validate colours never see <c>"#fff 70%"</c>. Each segment still carries its own token /
        /// <c>/alpha</c> form — this method does NOT validate segment contents, only the split shape.
        /// Returns false when there are &gt;2 colours or any segment is empty.
        /// </summary>
        public static bool TrySplitGradient(string raw, out string top, out string bottom, out string error)
        {
            var ok = TrySplitGradient(raw, out var parts, out error);
            top = parts.Top;
            bottom = parts.Bottom;
            return ok;
        }

        /// <summary>
        /// The full split: two colour segments, their stop positions (0..1 from the top edge, null
        /// when the author wrote none — the defaults are 0 and 1, i.e. the full-height ramp), and an
        /// optional colour hint written as a bare percentage between them (<c>"A, 70%, B"</c>).
        ///
        /// <para>A bare percentage is unambiguous: no colour form can end in '%', so a middle
        /// segment carrying one is a hint and nothing else.</para>
        /// </summary>
        public static bool TrySplitGradient(string raw, out GradientParts parts, out string error)
        {
            parts = new GradientParts(raw, null, null, null, null);
            error = null;
            if (string.IsNullOrEmpty(raw)) return true;   // empty handled by caller

            var segments = raw.Split(',');
            if (segments.Length > 3)
            {
                error = $"color \"{raw}\": gradient supports exactly two colours (top,bottom), " +
                        "optionally with a hint percentage between them (\"A, 70%, B\")";
                return false;
            }

            for (var i = 0; i < segments.Length; i++)
            {
                segments[i] = segments[i].Trim();
                if (segments[i].Length != 0) continue;
                error = $"color \"{raw}\": gradient segment is empty — expected \"top,bottom\"";
                return false;
            }

            string topRaw = segments[0], bottomRaw = null;
            float? hint = null;

            if (segments.Length == 2)
            {
                if (TryParsePercent(segments[1], out _))
                {
                    error = $"color \"{raw}\": a colour hint must sit BETWEEN two colours " +
                            "(\"A, 70%, B\") — on its own there is nothing for it to bend";
                    return false;
                }
                bottomRaw = segments[1];
            }
            else if (segments.Length == 3)
            {
                if (!TryParsePercent(segments[1], out var h))
                {
                    error = $"color \"{raw}\": gradient supports exactly two colours (top,bottom), " +
                            "optionally with a hint percentage between them (\"A, 70%, B\")";
                    return false;
                }
                hint = h;
                bottomRaw = segments[2];
            }

            if (!TrySplitStop(topRaw, out topRaw, out var topStop, out error)) return false;

            if (bottomRaw == null)
            {
                if (!topStop.HasValue)
                {
                    parts = new GradientParts(topRaw, null, null, null, null);
                    return true;
                }
                error = $"color \"{raw}\": a stop position needs a two-colour gradient " +
                        "(e.g. \"A 70%,B\") — a solid colour has no transition point to move";
                return false;
            }

            if (!TrySplitStop(bottomRaw, out bottomRaw, out var bottomStop, out error)) return false;

            // Compared as the shader will see them, so "A 70%,B" (0.7 → 1) passes and
            // "A 70%,B 30%" does not. Equal is a legal hard edge, not a mistake.
            var effTop = topStop ?? 0f;
            var effBottom = bottomStop ?? 1f;
            if (effBottom < effTop)
            {
                error = $"color \"{raw}\": the second stop position must not sit above the first — " +
                        "the gradient runs top to bottom";
                return false;
            }

            if (hint.HasValue && (hint.Value < effTop || hint.Value > effBottom))
            {
                error = $"color \"{raw}\": the hint must sit between the two stop positions " +
                        $"({effTop * 100f:0.###}%..{effBottom * 100f:0.###}%) — outside them there is " +
                        "no transition left to bend";
                return false;
            }

            parts = new GradientParts(topRaw, bottomRaw, topStop, bottomStop, hint);
            return true;
        }

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
