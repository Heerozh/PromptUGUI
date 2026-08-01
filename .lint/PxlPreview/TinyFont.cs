using System.Collections.Generic;

namespace PromptUGUI.PxlPreview
{
    /// <summary>3x5 bitmap font, just enough to stamp a section's name / size /
    /// border onto the preview itself. Without labels a multi-section montage is
    /// ambiguous ("which one is [pressed]?") the moment you stop counting columns.
    /// Uppercase-only; unknown glyphs render blank.</summary>
    internal static class TinyFont
    {
        public const int GlyphWidth = 3;
        public const int GlyphHeight = 5;
        public const int Tracking = 1; // blank columns between glyphs

        private static readonly Dictionary<char, string[]> Glyphs = new Dictionary<char, string[]>
        {
            ['A'] = new[] { "010", "101", "111", "101", "101" },
            ['B'] = new[] { "110", "101", "110", "101", "110" },
            ['C'] = new[] { "011", "100", "100", "100", "011" },
            ['D'] = new[] { "110", "101", "101", "101", "110" },
            ['E'] = new[] { "111", "100", "110", "100", "111" },
            ['F'] = new[] { "111", "100", "110", "100", "100" },
            ['G'] = new[] { "011", "100", "101", "101", "011" },
            ['H'] = new[] { "101", "101", "111", "101", "101" },
            ['I'] = new[] { "111", "010", "010", "010", "111" },
            ['J'] = new[] { "001", "001", "001", "101", "010" },
            ['K'] = new[] { "101", "101", "110", "101", "101" },
            ['L'] = new[] { "100", "100", "100", "100", "111" },
            ['M'] = new[] { "101", "111", "111", "101", "101" },
            ['N'] = new[] { "110", "101", "101", "101", "101" },
            ['O'] = new[] { "010", "101", "101", "101", "010" },
            ['P'] = new[] { "110", "101", "110", "100", "100" },
            ['Q'] = new[] { "010", "101", "101", "111", "011" },
            ['R'] = new[] { "110", "101", "110", "101", "101" },
            ['S'] = new[] { "011", "100", "010", "001", "110" },
            ['T'] = new[] { "111", "010", "010", "010", "010" },
            ['U'] = new[] { "101", "101", "101", "101", "111" },
            ['V'] = new[] { "101", "101", "101", "101", "010" },
            ['W'] = new[] { "101", "101", "111", "111", "101" },
            ['X'] = new[] { "101", "101", "010", "101", "101" },
            ['Y'] = new[] { "101", "101", "010", "010", "010" },
            ['Z'] = new[] { "111", "001", "010", "100", "111" },
            ['0'] = new[] { "111", "101", "101", "101", "111" },
            ['1'] = new[] { "010", "110", "010", "010", "111" },
            ['2'] = new[] { "110", "001", "010", "100", "111" },
            ['3'] = new[] { "110", "001", "010", "001", "110" },
            ['4'] = new[] { "101", "101", "111", "001", "001" },
            ['5'] = new[] { "111", "100", "110", "001", "110" },
            ['6'] = new[] { "011", "100", "110", "101", "010" },
            ['7'] = new[] { "111", "001", "010", "010", "010" },
            ['8'] = new[] { "010", "101", "010", "101", "010" },
            ['9'] = new[] { "010", "101", "011", "001", "110" },
            [':'] = new[] { "000", "010", "000", "010", "000" },
            [','] = new[] { "000", "000", "000", "010", "100" },
            ['.'] = new[] { "000", "000", "000", "000", "010" },
            ['-'] = new[] { "000", "000", "111", "000", "000" },
            ['_'] = new[] { "000", "000", "000", "000", "111" },
            ['/'] = new[] { "001", "001", "010", "100", "100" },
            ['('] = new[] { "001", "010", "010", "010", "001" },
            [')'] = new[] { "100", "010", "010", "010", "100" },
            ['*'] = new[] { "000", "101", "010", "101", "000" },
            ['!'] = new[] { "010", "010", "010", "000", "010" },
            ['?'] = new[] { "110", "001", "010", "000", "010" },
            [' '] = new[] { "000", "000", "000", "000", "000" },
        };

        public static int MeasureWidth(string text, int scale) =>
            text.Length == 0 ? 0 : (text.Length * (GlyphWidth + Tracking) - Tracking) * scale;

        public static void Draw(Canvas canvas, string text, int x, int y, int scale, Rgba color)
        {
            var penX = x;
            foreach (var raw in text)
            {
                var c = char.ToUpperInvariant(raw);
                if (Glyphs.TryGetValue(c, out var rows))
                {
                    for (var gy = 0; gy < GlyphHeight; gy++)
                    {
                        for (var gx = 0; gx < GlyphWidth; gx++)
                        {
                            if (rows[gy][gx] != '1') continue;
                            canvas.Fill(penX + gx * scale, y + gy * scale, scale, scale, color);
                        }
                    }
                }
                penX += (GlyphWidth + Tracking) * scale;
            }
        }
    }
}
