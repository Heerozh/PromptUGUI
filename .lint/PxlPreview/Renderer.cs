using System.Collections.Generic;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.PxlPreview
{
    internal sealed class RenderOptions
    {
        public int Scale = 8;
        public bool Guides;
        public bool Layers;
        public int LabelScale = 2;
    }

    /// <summary>Lays every section of one .pxl out left-to-right in file order,
    /// each on a transparency checkerboard under a text label. One image per file
    /// keeps state comparison (normal vs pressed) a single glance.
    ///
    /// With --layers each section instead gets its own ROW: every layer bottom-to-top
    /// followed by the composite. The composite is what the importer bakes, but only
    /// the per-layer cells show what you actually authored — a .pxl with layers never
    /// stores the composite, so this is the only way to see one layer on its own.</summary>
    internal static class Renderer
    {
        private static readonly Rgba Background = new Rgba(30, 30, 36);
        private static readonly Rgba CheckerA = new Rgba(86, 86, 94);
        private static readonly Rgba CheckerB = new Rgba(112, 112, 120);
        private static readonly Rgba LabelColor = new Rgba(228, 228, 232);
        private static readonly Rgba GuideColor = new Rgba(255, 0, 255);
        private static readonly Rgba EraserMark = new Rgba(255, 0, 255, 110);

        private const int Margin = 12;
        private const int Gap = 12;
        private const int RowGap = 16;
        private const int LabelGap = 4;

        /// <summary>One drawable block: a label plus the character rows to paint.
        /// Geometry (width/height/border) always comes from the owning section — all
        /// layers of a section are the same size by construction.</summary>
        private readonly struct Cell
        {
            public readonly string Label;
            public readonly IReadOnlyList<string> Rows;
            public readonly PxlSection Section;
            public readonly bool IsLayer; // layer cells mark eraser pixels; the composite doesn't

            public Cell(string label, IReadOnlyList<string> rows, PxlSection section,
                bool isLayer = false)
            { Label = label; Rows = rows; Section = section; IsLayer = isLayer; }
        }

        public static Canvas Render(PxlDocument doc, IReadOnlyDictionary<char, Color32> colors,
            string basename, RenderOptions opt)
        {
            var rows = BuildRows(doc, basename, opt);
            var labelHeight = TinyFont.GlyphHeight * opt.LabelScale;

            var canvasWidth = 0;
            var canvasHeight = Margin * 2 + RowGap * (rows.Count - 1);
            var widths = new List<int[]>(rows.Count);
            foreach (var row in rows)
            {
                var w = new int[row.Count];
                var rowWidth = Gap * (row.Count - 1);
                var rowBlockHeight = 0;
                for (var i = 0; i < row.Count; i++)
                {
                    var blockWidth = row[i].Section.Width * opt.Scale;
                    var labelWidth = TinyFont.MeasureWidth(row[i].Label, opt.LabelScale);
                    w[i] = blockWidth > labelWidth ? blockWidth : labelWidth;
                    rowWidth += w[i];
                    var blockHeight = row[i].Section.Height * opt.Scale;
                    if (blockHeight > rowBlockHeight) rowBlockHeight = blockHeight;
                }
                widths.Add(w);
                if (rowWidth > canvasWidth) canvasWidth = rowWidth;
                canvasHeight += labelHeight + LabelGap + rowBlockHeight;
            }

            var canvas = new Canvas(canvasWidth + Margin * 2, canvasHeight, Background);

            var y = Margin;
            for (var r = 0; r < rows.Count; r++)
            {
                var row = rows[r];
                var x = Margin;
                var rowBlockHeight = 0;
                for (var i = 0; i < row.Count; i++)
                {
                    TinyFont.Draw(canvas, row[i].Label, x, y, opt.LabelScale, LabelColor);
                    DrawCell(canvas, row[i], colors, x, y + labelHeight + LabelGap, opt);
                    x += widths[r][i] + Gap;
                    var blockHeight = row[i].Section.Height * opt.Scale;
                    if (blockHeight > rowBlockHeight) rowBlockHeight = blockHeight;
                }
                y += labelHeight + LabelGap + rowBlockHeight + RowGap;
            }
            return canvas;
        }

        private static List<List<Cell>> BuildRows(PxlDocument doc, string basename,
            RenderOptions opt)
        {
            var rows = new List<List<Cell>>();
            if (!opt.Layers)
            {
                // Default: all sections side by side, composites only.
                var single = new List<Cell>(doc.Sections.Count);
                foreach (var s in doc.Sections) single.Add(new Cell(Label(s, basename), s.Rows, s));
                rows.Add(single);
                return rows;
            }

            foreach (var s in doc.Sections)
            {
                var name = s.Name ?? basename;
                // A single-layer section has nothing to decompose — one cell, same as default.
                if (s.Layers.Count == 1)
                {
                    rows.Add(new List<Cell> { new Cell(Label(s, basename), s.Rows, s) });
                    continue;
                }
                var row = new List<Cell>(s.Layers.Count + 1);
                foreach (var l in s.Layers)
                    row.Add(new Cell(name + "/" + (l.Name ?? "(grid)"), l.Rows, s, isLayer: true));
                row.Add(new Cell(Label(s, basename) + " flat", s.Rows, s));
                rows.Add(row);
            }
            return rows;
        }

        private static string Label(PxlSection s, string basename)
        {
            var label = (s.Name ?? basename) + " " + s.Width + "x" + s.Height;
            if (s.Border.x != 0 || s.Border.y != 0 || s.Border.z != 0 || s.Border.w != 0)
            {
                label += " b" + (int)s.Border.x + "," + (int)s.Border.y + "," +
                         (int)s.Border.z + "," + (int)s.Border.w;
            }
            if (s.Tiled) label += " tiled";
            return label;
        }

        private static void DrawCell(Canvas canvas, Cell cell,
            IReadOnlyDictionary<char, Color32> colors, int left, int top, RenderOptions opt)
        {
            var s = cell.Section;
            var scale = opt.Scale;
            var checker = scale >= 4 ? scale : 8;

            for (var y = 0; y < s.Height * scale; y += checker)
            {
                for (var x = 0; x < s.Width * scale; x += checker)
                {
                    var tone = ((x / checker) + (y / checker)) % 2 == 0 ? CheckerA : CheckerB;
                    canvas.Fill(left + x, top + y,
                        Clamp(checker, s.Width * scale - x), Clamp(checker, s.Height * scale - y), tone);
                }
            }

            for (var row = 0; row < s.Height; row++)
            {
                var line = cell.Rows[row];
                for (var col = 0; col < s.Width; col++)
                {
                    var ch = line[col];
                    var c = colors[ch];
                    if (c.a == 0)
                    {
                        // A non-'.' char resolving to alpha 0 ('X: transparent') is an ERASER:
                        // it overwrites the layers below instead of passing through. Painting
                        // nothing would make an eraser layer look identical to an empty one —
                        // exactly the distinction --layers exists to show. Mark it (layer cells
                        // only; in the composite an erased pixel is simply transparent).
                        if (cell.IsLayer && ch != '.')
                        {
                            canvas.Blend(left + col * scale, top + row * scale, scale, scale,
                                EraserMark);
                        }
                        continue;
                    }
                    canvas.Blend(left + col * scale, top + row * scale, scale, scale,
                        new Rgba(c.r, c.g, c.b, c.a));
                }
            }

            if (opt.Guides) DrawGuides(canvas, s, left, top, scale);
        }

        /// <summary>9-slice split lines, in Unity's L,B,R,T border order. Drawn 1
        /// canvas pixel wide (not 1 source pixel) so they mark the seam without
        /// hiding the art underneath.</summary>
        private static void DrawGuides(Canvas canvas, PxlSection s, int left, int top, int scale)
        {
            if (s.Border.x == 0 && s.Border.y == 0 && s.Border.z == 0 && s.Border.w == 0) return;
            var w = s.Width * scale;
            var h = s.Height * scale;
            if (s.Border.x > 0) canvas.Fill(left + (int)s.Border.x * scale, top, 1, h, GuideColor);
            if (s.Border.z > 0) canvas.Fill(left + w - (int)s.Border.z * scale - 1, top, 1, h, GuideColor);
            if (s.Border.w > 0) canvas.Fill(left, top + (int)s.Border.w * scale, w, 1, GuideColor);
            if (s.Border.y > 0) canvas.Fill(left, top + h - (int)s.Border.y * scale - 1, w, 1, GuideColor);
        }

        private static int Clamp(int value, int max) => value > max ? max : value;
    }
}
