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
