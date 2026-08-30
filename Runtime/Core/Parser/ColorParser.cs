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
        /// Splits an optional two-stop gradient value on ','. <c>"#fff,#000"</c> → top <c>"#fff"</c>,
        /// bottom <c>"#000"</c>; no comma → top = raw, bottom = null. Segments are trimmed (authors
        /// write <c>"a, b"</c>) and their stop positions are stripped off, so callers that only
        /// validate colours never see <c>"#fff 70%"</c>. Each segment still carries its own token /
        /// <c>/alpha</c> form — this method does NOT validate segment contents, only the split shape.
        /// Returns false when there are &gt;2 segments or any segment is empty.
        /// </summary>
        public static bool TrySplitGradient(string raw, out string top, out string bottom, out string error)
            => TrySplitGradient(raw, out top, out bottom, out _, out _, out error);

        /// <summary>
        /// <see cref="TrySplitGradient(string,out string,out string,out string)"/> plus the two
        /// segments' stop positions (0..1 from the top edge, null when the author wrote none —
        /// the defaults are 0 and 1, i.e. the full-height ramp this feature extends).
        /// A position on a value with no comma is an error: a solid colour has no transition to move.
        /// </summary>
        public static bool TrySplitGradient(string raw, out string top, out string bottom,
                                            out float? topStop, out float? bottomStop, out string error)
        {
            top = raw;
            bottom = null;
            topStop = null;
            bottomStop = null;
            error = null;
            if (string.IsNullOrEmpty(raw)) return true;   // empty handled by caller

            var comma = raw.IndexOf(',');
            if (comma >= 0)
            {
                if (raw.IndexOf(',', comma + 1) >= 0)
                {
                    error = $"color \"{raw}\": gradient supports exactly two colours (top,bottom)";
                    return false;
                }

                var head = raw.Substring(0, comma).Trim();
                var tail = raw.Substring(comma + 1).Trim();
                if (head.Length == 0 || tail.Length == 0)
                {
                    error = $"color \"{raw}\": gradient segment is empty — expected \"top,bottom\"";
                    return false;
                }

                top = head;
                bottom = tail;
            }

            if (!TrySplitStop(top, out var topBase, out topStop, out error)) return false;
            top = topBase;

            if (bottom == null)
            {
                if (!topStop.HasValue) return true;
                error = $"color \"{raw}\": a stop position needs a two-colour gradient " +
                        "(e.g. \"A 70%,B\") — a solid colour has no transition point to move";
                return false;
            }

            if (!TrySplitStop(bottom, out var bottomBase, out bottomStop, out error)) return false;
            bottom = bottomBase;

            // Compared as the shader will see them, so "A 70%,B" (0.7 → 1) passes and
            // "A 70%,B 30%" does not. Equal is a legal hard edge, not a mistake.
            if ((bottomStop ?? 1f) < (topStop ?? 0f))
            {
                error = $"color \"{raw}\": the second stop position must not sit above the first — " +
                        "the gradient runs top to bottom";
                return false;
            }

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
