namespace PromptUGUI.Parser
{
    /// <summary>
    /// Pure C# hex color parser (no UnityEngine dependency).
    /// Matches ColorUtility.TryParseHtmlString behavior.
    /// Used by both UIDocumentParser (runtime) and UIXmlLint CLI (build-time).
    /// </summary>
    internal static class ColorParser
    {
        /// <summary>
        /// Validates a hex color string format without parsing the actual color values.
        /// Accepted formats: #RGB, #RRGGBB, #RGBA, #RRGGBBAA
        /// </summary>
        public static bool TryParseHtmlString(string htmlString)
        {
            if (string.IsNullOrEmpty(htmlString)) return false;
            if (htmlString[0] != '#') return false;

            var len = htmlString.Length;

            // ColorUtility accepts: #RGB (4 chars), #RRGGBB (7 chars), #RGBA (5 chars), #RRGGBBAA (9 chars)
            if (len != 4 && len != 5 && len != 7 && len != 9)
                return false;

            // Validate all characters after # are hex digits
            for (var i = 1; i < len; i++)
            {
                var c = htmlString[i];
                if (!IsHexDigit(c))
                    return false;
            }

            return true;
        }

        private static bool IsHexDigit(char c)
        {
            return (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
        }
    }
}
