namespace PromptUGUI.Editor
{
    /// <summary>Single source of truth for the .pxl grid character alphabet, shared by
    /// the PNG-round-trip tools (PxlPngSync) and the PNG→.pxl converter (PxlFromPng).
    /// A-Z a-z 0-9 first (readable), then the remaining printable ASCII minus the four
    /// characters the format reserves: '.' (transparent), '#' (comment), '[' / ']'
    /// (section headers). Those reserved characters are intentionally absent here so a
    /// generated key can never collide with grammar.</summary>
    internal static class PxlChars
    {
        public const string Alphabet =
            "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789" +
            "!\"$%&'()*+,-/:;<=>?@\\^_`{|}~";
    }
}
