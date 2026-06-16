namespace PromptUGUI
{
    /// <summary>Visual config for &lt;Markdown&gt; rendering. Plain POCO; colors are theme tokens / hex
    /// resolved via UI.Theme.Resolve, fonts are font-type names resolved via FontApplier.</summary>
    public sealed class MarkdownStyle
    {
        // Per-level transform scale (h1..h6) over BodySize. Headings magnify via RectTransform.localScale
        // (font stays at BodySize → bitmap/pixel fonts stay crisp), NOT a larger font size. Defaults are the
        // legacy {32,28,24,20,18,16} ÷ 16, so the default look is unchanged at BodySize 16.
        public float[] HeadingScales = { 2f, 1.75f, 1.5f, 1.25f, 1.125f, 1f };
        public float BodySize = 16f;
        public string BodyFont = "default";
        public string CodeFont = "default";
        public string LinkColor = "#4EA1FF";
        public string BodyColor = "";   // empty = inherit ProceduralBuilders.DefaultLabelColor (body text gets no color=)
        // How **bold** (and headings / table headers) render. Space-separated tokens: style keywords
        // {bold, underline, italic, strikethrough, none} + at most one color value (theme token / hex /
        // CSS name / "/alpha" suffix). Combinable, e.g. "underline #ffcc00". Default "bold" → TMP <b>
        // (unchanged). "none" → strip. A color token → <color=…>. Parsed by MarkdigRenderer.ComputeBoldWrap.
        public string BoldStyle = "bold";
        public string CodeBackground = "#00000020";
        public string QuoteBarColor = "#888888";
        public float BlockSpacing = 8f;
        // Inner padding (px) insetting the rendered content within the scroll viewport. Default 2 gives
        // outlined/stroked fonts room so the viewport RectMask2D doesn't clip the first/last glyph outlines.
        public int Padding = 2;
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
            c.HeadingScales = (float[])HeadingScales.Clone();
            return c;
        }
    }
}
