namespace PromptUGUI
{
    /// <summary>Visual config for &lt;Markdown&gt; rendering. Plain POCO; colors are theme tokens / hex
    /// resolved via UI.Theme.Resolve, fonts are font-type names resolved via FontApplier.</summary>
    public sealed class MarkdownStyle
    {
        public float[] HeadingSizes = { 32f, 28f, 24f, 20f, 18f, 16f };
        public float BodySize = 16f;
        public string BodyFont = "default";
        public string CodeFont = "default";
        public string LinkColor = "#4EA1FF";
        public string CodeBackground = "#00000020";
        public string QuoteBarColor = "#888888";
        public float BlockSpacing = 8f;
        public float ListIndent = 24f;
        public string BulletGlyph = "•";       // U+2022
        public string CheckedGlyph = "☑";      // U+2611
        public string UncheckedGlyph = "☐";    // U+2610
        public string HrColor = "#888888";
        public float HrThickness = 2f;
        public bool ParagraphWrap = true;

        public static MarkdownStyle CreateDefault() => new();

        public MarkdownStyle Clone()
        {
            var c = (MarkdownStyle)MemberwiseClone();
            c.HeadingSizes = (float[])HeadingSizes.Clone();
            return c;
        }
    }
}
