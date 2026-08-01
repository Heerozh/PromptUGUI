using System.Collections.Generic;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.PxlPreview
{
    internal sealed class RenderOptions
    {
        public int Scale = 8;
        public bool Guides;
        public int LabelScale = 2;
    }

    /// <summary>Lays every section of one .pxl out left-to-right in file order,
    /// each on a transparency checkerboard under a text label. One image per file
    /// keeps state comparison (normal vs pressed) a single glance.</summary>
    internal static class Renderer
    {
        private static readonly Rgba Background = new Rgba(30, 30, 36);
        private static readonly Rgba CheckerA = new Rgba(86, 86, 94);
        private static readonly Rgba CheckerB = new Rgba(112, 112, 120);
        private static readonly Rgba LabelColor = new Rgba(228, 228, 232);
        private static readonly Rgba GuideColor = new Rgba(255, 0, 255);

        private const int Margin = 12;
        private const int Gap = 12;
        private const int LabelGap = 4;

        public static Canvas Render(PxlDocument doc, IReadOnlyDictionary<char, Color32> colors,
            string basename, RenderOptions opt)
        {
            var n = doc.Sections.Count;
            var labels = new string[n];
            var colWidths = new int[n];
            var labelHeight = TinyFont.GlyphHeight * opt.LabelScale;
            var maxBlockHeight = 0;
            var totalWidth = Margin * 2 + Gap * (n - 1);

            for (var i = 0; i < n; i++)
            {
                var s = doc.Sections[i];
                labels[i] = Label(s, basename);
                var blockWidth = s.Width * opt.Scale;
                var labelWidth = TinyFont.MeasureWidth(labels[i], opt.LabelScale);
                colWidths[i] = blockWidth > labelWidth ? blockWidth : labelWidth;
                totalWidth += colWidths[i];
                var blockHeight = s.Height * opt.Scale;
                if (blockHeight > maxBlockHeight) maxBlockHeight = blockHeight;
            }

            var canvas = new Canvas(totalWidth,
                Margin * 2 + labelHeight + LabelGap + maxBlockHeight, Background);

            var x = Margin;
            var blockTop = Margin + labelHeight + LabelGap;
            for (var i = 0; i < n; i++)
            {
                var s = doc.Sections[i];
                TinyFont.Draw(canvas, labels[i], x, Margin, opt.LabelScale, LabelColor);
                DrawSection(canvas, s, colors, x, blockTop, opt);
                x += colWidths[i] + Gap;
            }
            return canvas;
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

        private static void DrawSection(Canvas canvas, PxlSection s,
            IReadOnlyDictionary<char, Color32> colors, int left, int top, RenderOptions opt)
        {
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
                var line = s.Rows[row];
                for (var col = 0; col < s.Width; col++)
                {
                    var c = colors[line[col]];
                    if (c.a == 0) continue;
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
