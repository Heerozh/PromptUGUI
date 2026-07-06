using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>Converts a decoded PNG (top-down, row-major Color32) into .pxl source
    /// text. Pure and Unity-asset-free so it unit-tests directly; the Editor glue
    /// (menu, PNG decode, file write) lives in CreatePxlFromPngMenu.
    ///
    /// Rules (spec discussed 2026-07-06):
    ///  • Fully transparent pixels (a == 0) become '.', excluded from the palette.
    ///  • ≤ maxColors distinct opaque colors → emitted verbatim (lossless); most pixel
    ///    art is already limited-palette and must survive a round-trip untouched.
    ///  • More than that → PxlQuantizer reduces to the budget in RGBA space.
    ///  • Chars are assigned from PxlChars.Alphabet in descending pixel-population order
    ///    (dominant color gets 'A'), deterministically.
    ///  • Output is self-contained: inline #RRGGBB / #RRGGBBAA, no external .gpl.</summary>
    internal static class PxlFromPng
    {
        /// <param name="pixels">top-down, row-major, length == width*height.</param>
        public static string Convert(Color32[] pixels, int width, int height, int maxColors)
        {
            var budget = Mathf.Clamp(maxColors, 1, PxlChars.Alphabet.Length);

            // Histogram of opaque colors (packed RGBA → count).
            var hist = new Dictionary<uint, int>();
            foreach (var c in pixels)
            {
                if (c.a == 0) continue;
                var key = Pack(c);
                hist.TryGetValue(key, out var cnt);
                hist[key] = cnt + 1;
            }

            var distinct = new List<Color32>(hist.Count);
            var weights = new List<int>(hist.Count);
            foreach (var kv in hist) { distinct.Add(Unpack(kv.Key)); weights.Add(kv.Value); }

            var palette = distinct.Count <= budget
                ? new List<Color32>(distinct)
                : PxlQuantizer.Quantize(distinct, weights, budget);

            // Map each source color to its nearest palette entry; tally palette populations.
            var paletteWeight = new int[palette.Count];
            var nearest = new int[distinct.Count];
            for (var i = 0; i < distinct.Count; i++)
            {
                var best = 0;
                var bestD = int.MaxValue;
                for (var j = 0; j < palette.Count; j++)
                {
                    var d = PxlQuantizer.Dist2(distinct[i], palette[j]);
                    if (d < bestD) { bestD = d; best = j; }
                }
                nearest[i] = best;
                paletteWeight[best] += weights[i];
            }

            // Keep only used entries (drops quantizer reps that nothing maps to, and any
            // duplicate centroids), ordered by population desc then packed value for a
            // stable, dominant-first char assignment.
            var order = new List<int>();
            for (var j = 0; j < palette.Count; j++)
                if (paletteWeight[j] > 0) order.Add(j);
            order.Sort((a, b) =>
            {
                var byPop = paletteWeight[b].CompareTo(paletteWeight[a]);
                return byPop != 0 ? byPop : Pack(palette[a]).CompareTo(Pack(palette[b]));
            });

            var charForPalette = new Dictionary<int, char>();
            for (var rank = 0; rank < order.Count; rank++)
                charForPalette[order[rank]] = PxlChars.Alphabet[rank];

            var colorToChar = new Dictionary<uint, char>(distinct.Count);
            for (var i = 0; i < distinct.Count; i++)
                colorToChar[Pack(distinct[i])] = charForPalette[nearest[i]];

            var sb = new StringBuilder();
            sb.Append("# Generated from PNG by PromptUGUI — edit with the `authoring-promptugui-pxl` skill.\n");
            sb.Append("# Add a `border: L,B,R,T` (and `tiled: true`) line below if this is a 9-slice.\n");
            sb.Append("ppu: 100\n");
            if (order.Count > 0)
            {
                sb.Append("chars:\n");
                for (var rank = 0; rank < order.Count; rank++)
                    sb.Append("  ").Append(PxlChars.Alphabet[rank]).Append(": ")
                        .Append(HexValue(palette[order[rank]])).Append('\n');
            }
            sb.Append("grid:\n");
            for (var y = 0; y < height; y++)
            {
                sb.Append("  ");
                for (var x = 0; x < width; x++)
                {
                    var c = pixels[y * width + x];
                    sb.Append(c.a == 0 ? '.' : colorToChar[Pack(c)]);
                }
                sb.Append('\n');
            }
            return sb.ToString();
        }

        private static string HexValue(Color32 c) => c.a == 255
            ? $"#{c.r:x2}{c.g:x2}{c.b:x2}"
            : $"#{c.r:x2}{c.g:x2}{c.b:x2}{c.a:x2}";

        private static uint Pack(Color32 c) =>
            ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | c.a;

        private static Color32 Unpack(uint k) =>
            new Color32((byte)(k >> 24), (byte)(k >> 16), (byte)(k >> 8), (byte)k);
    }
}
