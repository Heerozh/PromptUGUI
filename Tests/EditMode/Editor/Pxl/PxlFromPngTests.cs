using System.Collections.Generic;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlFromPngTests
    {
        private static readonly Color32 Red = new Color32(255, 0, 0, 255);
        private static readonly Color32 Green = new Color32(0, 255, 0, 255);
        private static readonly Color32 Blue = new Color32(0, 0, 255, 255);

        // Parse generated .pxl and rebuild the pixel grid (top-down) so tests can assert
        // on what the file actually renders to.
        private static Color32[] Reconstruct(string pxl, out int w, out int h)
        {
            var doc = PxlParser.Parse(pxl);
            Assert.AreEqual(1, doc.Sections.Count);
            var map = PxlColorResolver.Resolve(doc, null);
            var s = doc.Sections[0];
            w = s.Width;
            h = s.Height;
            var px = new Color32[w * h];
            for (var y = 0; y < h; y++)
                for (var x = 0; x < w; x++)
                    px[y * w + x] = map[s.Rows[y][x]];
            return px;
        }

        private static void AssertColorEq(Color32 expected, Color32 actual, string msg)
        {
            Assert.AreEqual(expected.r, actual.r, msg + " (r)");
            Assert.AreEqual(expected.g, actual.g, msg + " (g)");
            Assert.AreEqual(expected.b, actual.b, msg + " (b)");
            Assert.AreEqual(expected.a, actual.a, msg + " (a)");
        }

        [Test]
        public void Under_budget_round_trips_losslessly_and_maps_transparent_to_dot()
        {
            // 3×2, one fully-transparent pixel (with non-zero RGB, which must be ignored).
            var transparent = new Color32(100, 100, 100, 0);
            var src = new[]
            {
                Red,  Green, transparent,
                Blue, Red,   Green,
            };

            var text = PxlFromPng.Convert(src, 3, 2, maxColors: 8);
            var doc = PxlParser.Parse(text);
            Assert.AreEqual(3, doc.Chars.Count, "three distinct opaque colors → three chars");

            var got = Reconstruct(text, out var w, out var h);
            Assert.AreEqual(3, w);
            Assert.AreEqual(2, h);
            for (var i = 0; i < src.Length; i++)
            {
                if (src[i].a == 0)
                    Assert.AreEqual(0, got[i].a, $"pixel {i} should be transparent");
                else
                    AssertColorEq(src[i], got[i], $"pixel {i}");
            }
        }

        [Test]
        public void Over_budget_quantizes_within_the_color_limit()
        {
            var palette = new[]
            {
                new Color32(255, 0, 0, 255), new Color32(128, 0, 0, 255),
                new Color32(0, 255, 0, 255), new Color32(0, 128, 0, 255),
                new Color32(0, 0, 255, 255), new Color32(0, 0, 128, 255),
            };
            var src = new Color32[palette.Length];
            for (var i = 0; i < palette.Length; i++) src[i] = palette[i]; // 6×1

            var text = PxlFromPng.Convert(src, palette.Length, 1, maxColors: 3);
            var doc = PxlParser.Parse(text); // throws if any grid char is undefined
            Assert.LessOrEqual(doc.Chars.Count, 3, "must not exceed the color budget");
            Assert.GreaterOrEqual(doc.Chars.Count, 1);

            var got = Reconstruct(text, out _, out _);
            var distinct = new HashSet<uint>();
            foreach (var c in got) distinct.Add(((uint)c.r << 16) | ((uint)c.g << 8) | c.b);
            Assert.LessOrEqual(distinct.Count, 3, "rendered image collapses to ≤ 3 colors");
        }

        [Test]
        public void Dominant_color_gets_the_first_alphabet_char()
        {
            var src = new[] { Red, Red, Red, Green }; // Red dominates
            var text = PxlFromPng.Convert(src, 4, 1, maxColors: 8);
            var doc = PxlParser.Parse(text);
            var map = PxlColorResolver.Resolve(doc, null);

            Assert.IsTrue(doc.Chars.ContainsKey('A'), "first char is 'A'");
            AssertColorEq(Red, map['A'], "dominant color → 'A'");
        }

        [Test]
        public void Semi_transparent_color_is_preserved_as_rgba_hex()
        {
            var semi = new Color32(10, 20, 30, 128);
            var src = new[] { semi, semi };
            var text = PxlFromPng.Convert(src, 2, 1, maxColors: 8);

            StringAssert.Contains("#0a141e80", text, "alpha kept as #RRGGBBAA");
            var got = Reconstruct(text, out _, out _);
            AssertColorEq(semi, got[0], "semi-transparent pixel");
        }

        [Test]
        public void Alphabet_excludes_reserved_chars_and_has_no_duplicates()
        {
            var seen = new HashSet<char>();
            foreach (var c in PxlChars.Alphabet)
            {
                Assert.IsFalse(c == '.' || c == '#' || c == '[' || c == ']',
                    $"reserved char '{c}' must not be in the alphabet");
                Assert.IsTrue(seen.Add(c), $"duplicate char '{c}' in alphabet");
            }
            Assert.GreaterOrEqual(PxlChars.Alphabet.Length, 64);
        }
    }
}
