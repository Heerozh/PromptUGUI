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
