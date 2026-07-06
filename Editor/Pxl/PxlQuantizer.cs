using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>Weighted palette generation: median-cut to seed representatives, then a
    /// fixed number of k-means (Lloyd) passes to minimise weighted squared error. Runs
    /// in RGBA space (semi-transparent edges get their own slots) over a color→count
    /// histogram, so cost scales with distinct-color count, not pixel count.
    ///
    /// Deterministic on purpose — no RNG. Median-cut always splits the box with the
    /// widest single-channel range at that channel's population median, and k-means
    /// starts from those seeds, so identical input yields identical output (a hard
    /// requirement for the round-trip tests). Used by PxlFromPng when a PNG carries more
    /// distinct colors than the requested budget.</summary>
    internal static class PxlQuantizer
    {
        private const int KMeansIterations = 8;

        /// <summary>Reduce the weighted color set to at most <paramref name="k"/>
        /// representative colors. If there are already ≤ k distinct colors they are
        /// returned verbatim (lossless). <paramref name="weights"/> must align with
        /// <paramref name="colors"/>.</summary>
        public static List<Color32> Quantize(
            IReadOnlyList<Color32> colors, IReadOnlyList<int> weights, int k)
        {
            if (k < 1) k = 1;
            var n = colors.Count;
            var result = new List<Color32>();
            if (n == 0) return result;
            if (n <= k)
            {
                for (var i = 0; i < n; i++) result.Add(colors[i]);
                return result;
            }

            // Each box is a list of indices into colors/weights.
            var boxes = new List<List<int>> { Sequence(n) };
            while (boxes.Count < k)
            {
                var target = -1;
                var widest = -1;
                for (var i = 0; i < boxes.Count; i++)
                {
                    if (boxes[i].Count < 2) continue;
                    var range = ChannelRange(colors, boxes[i], out _);
                    if (range > widest) { widest = range; target = i; }
                }
                if (target < 0) break; // every box is a single color — cannot split further
                SplitAtMedian(colors, weights, boxes, target);
            }

            foreach (var box in boxes)
                result.Add(WeightedMean(colors, weights, box));

            KMeansRefine(colors, weights, result);
            return result;
        }

        private static List<int> Sequence(int n)
        {
            var list = new List<int>(n);
            for (var i = 0; i < n; i++) list.Add(i);
            return list;
        }

        // Widest per-channel spread in the box; outputs the channel (0=r,1=g,2=b,3=a).
        private static int ChannelRange(IReadOnlyList<Color32> colors, List<int> box, out int channel)
        {
            int rlo = 255, rhi = 0, glo = 255, ghi = 0, blo = 255, bhi = 0, alo = 255, ahi = 0;
            foreach (var i in box)
            {
                var c = colors[i];
                if (c.r < rlo) rlo = c.r; if (c.r > rhi) rhi = c.r;
                if (c.g < glo) glo = c.g; if (c.g > ghi) ghi = c.g;
                if (c.b < blo) blo = c.b; if (c.b > bhi) bhi = c.b;
                if (c.a < alo) alo = c.a; if (c.a > ahi) ahi = c.a;
            }
            int rr = rhi - rlo, gg = ghi - glo, bb = bhi - blo, aa = ahi - alo;
            channel = 0;
            var best = rr;
            if (gg > best) { best = gg; channel = 1; }
            if (bb > best) { best = bb; channel = 2; }
            if (aa > best) { best = aa; channel = 3; }
            return best;
        }

        private static int Channel(Color32 c, int ch) =>
            ch switch { 0 => c.r, 1 => c.g, 2 => c.b, _ => c.a };

        // Sort the box by its widest channel and cut at the population (weight) median,
        // guaranteeing both halves are non-empty.
        private static void SplitAtMedian(IReadOnlyList<Color32> colors,
            IReadOnlyList<int> weights, List<List<int>> boxes, int target)
        {
            var box = boxes[target];
            ChannelRange(colors, box, out var ch);
            box.Sort((a, b) => Channel(colors[a], ch).CompareTo(Channel(colors[b], ch)));

            long total = 0;
            foreach (var i in box) total += weights[i];
            var half = total / 2;

            long acc = 0;
            var cut = 1;
            for (var idx = 0; idx < box.Count - 1; idx++)
            {
                acc += weights[box[idx]];
                if (acc >= half) { cut = idx + 1; break; }
                cut = idx + 2; // keep advancing if the median lies further right
            }
            if (cut < 1) cut = 1;
            if (cut > box.Count - 1) cut = box.Count - 1;

            var right = box.GetRange(cut, box.Count - cut);
            boxes[target] = box.GetRange(0, cut);
            boxes.Add(right);
        }

        private static Color32 WeightedMean(IReadOnlyList<Color32> colors,
            IReadOnlyList<int> weights, List<int> box)
        {
            long r = 0, g = 0, b = 0, a = 0, w = 0;
            foreach (var i in box)
            {
                var c = colors[i];
                long ww = weights[i];
                r += c.r * ww; g += c.g * ww; b += c.b * ww; a += c.a * ww; w += ww;
            }
            if (w == 0) return colors[box[0]];
            return new Color32(Div(r, w), Div(g, w), Div(b, w), Div(a, w));
        }

        private static void KMeansRefine(IReadOnlyList<Color32> colors,
            IReadOnlyList<int> weights, List<Color32> centroids)
        {
            var k = centroids.Count;
            var assign = new int[colors.Count];
            for (var it = 0; it < KMeansIterations; it++)
            {
                var changed = false;
                for (var i = 0; i < colors.Count; i++)
                {
                    var best = 0;
                    var bestD = int.MaxValue;
                    for (var j = 0; j < k; j++)
                    {
                        var d = Dist2(colors[i], centroids[j]);
                        if (d < bestD) { bestD = d; best = j; }
                    }
                    if (assign[i] != best) { assign[i] = best; changed = true; }
                }

                var sr = new long[k]; var sg = new long[k]; var sb = new long[k];
                var sa = new long[k]; var sw = new long[k];
                for (var i = 0; i < colors.Count; i++)
                {
                    var j = assign[i];
                    var c = colors[i];
                    long w = weights[i];
                    sr[j] += c.r * w; sg[j] += c.g * w; sb[j] += c.b * w; sa[j] += c.a * w; sw[j] += w;
                }
                for (var j = 0; j < k; j++)
                    if (sw[j] > 0)
                        centroids[j] = new Color32(
                            Div(sr[j], sw[j]), Div(sg[j], sw[j]), Div(sb[j], sw[j]), Div(sa[j], sw[j]));

                if (!changed) break; // converged
            }
        }

        internal static int Dist2(Color32 a, Color32 b)
        {
            int dr = a.r - b.r, dg = a.g - b.g, db = a.b - b.b, da = a.a - b.a;
            return dr * dr + dg * dg + db * db + da * da;
        }

        private static byte Div(long num, long den)
        {
            var v = (num + den / 2) / den; // round to nearest
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return (byte)v;
        }
    }
}
