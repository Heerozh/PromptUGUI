using System.Text.RegularExpressions;

namespace PromptUGUI.Editor.I18n {
    internal static class TmpRichTextDetector {
        // Recognized TMP rich-text tags. Conservative — only tags whose attributes carry resource
        // references that translators MUST NOT touch. False positives here only add a "preserve" hint
        // comment; false negatives skip the hint.
        static readonly Regex Tag = new(
            @"</?(?:sprite|color|b|i|u|s|size|font|font-weight|align|alpha|space|indent|line-height|line-indent|link|lowercase|uppercase|smallcaps|mark|noparse|page|pos|rotate|style|sub|sup|voffset|width)(\s|=|/|>)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static bool HasTmpTags(string s) =>
            !string.IsNullOrEmpty(s) && Tag.IsMatch(s);
    }
}
