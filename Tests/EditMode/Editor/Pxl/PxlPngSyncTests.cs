using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlPngSyncTests
    {
        private static readonly Color32 K = new(0x00, 0x00, 0x00, 255);
        private static readonly Color32 W = new(0xff, 0xff, 0xff, 255);
        private static readonly Color32 T = new(0, 0, 0, 0);

        private static PxlPngSync.PngImage Img(int w, int h, params Color32[] px) =>
            new(w, h, px);

        private const string TwoSections =
            "chars:\n  K: #000000\n  W: #ffffff\n[a]\ngrid:\n  KW\n  WK\n\n[b]\ngrid:\n  K\n";

        [Test]
        public void BuildPlan_matches_missing_and_extra()
        {
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["btn.a.png"] = Img(2, 2, K, W, W, K),
                ["btn.stray.png"] = Img(1, 1, K),
            };
            var plan = PxlPngSync.BuildPlan(TwoSections, "btn", pngs, null);
            Assert.IsEmpty(plan.Errors);
            Assert.AreEqual(1, plan.Updates.Count);
            Assert.AreEqual("a", plan.Updates[0].Section.Name);
            Assert.AreEqual(new[] { "b" }, plan.MissingSections.ToArray());
            Assert.AreEqual(new[] { "btn.stray.png" }, plan.ExtraPngs.ToArray());
        }

        [Test]
        public void BuildPlan_implicit_section_matches_plain_name()
        {
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["dot.png"] = Img(1, 1, K) };
            var plan = PxlPngSync.BuildPlan(
                "chars:\n  K: #000000\ngrid:\n  K\n", "dot", pngs, null);
            Assert.IsEmpty(plan.Errors);
            Assert.AreEqual(1, plan.Updates.Count);
        }

        [Test]
        public void BuildPlan_reuses_existing_chars_first_declared_wins()
        {
            var text = "chars:\n  K: #000000\n  X: #000000\ngrid:\n  K\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(1, 1, K) };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            Assert.IsEmpty(plan.Errors);
            Assert.IsEmpty(plan.NewChars);
            Assert.AreEqual("K", plan.Updates[0].Rows.Single());
        }

        [Test]
        public void BuildPlan_new_inline_color_gets_next_free_char()
        {
            var red = new Color32(255, 0, 0, 255);
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(1, 1, red) };
            var plan = PxlPngSync.BuildPlan(
                "chars:\n  A: #000000\ngrid:\n  A\n", "d", pngs, null);
            Assert.IsEmpty(plan.Errors);
            Assert.AreEqual(1, plan.NewChars.Count);
            Assert.AreEqual('B', plan.NewChars[0].ch);
            Assert.AreEqual("#ff0000", plan.NewChars[0].value);
        }

        [Test]
        public void BuildPlan_palette_mode_named_color_and_alpha_variant()
        {
            var palette = GplPalette.Parse("GIMP Palette\n26 28 44\tnight\n");
            var night = new Color32(26, 28, 44, 255);
            var nightHalf = new Color32(26, 28, 44, 128);
            var text = "palette: @p\nchars:\n  K: night\ngrid:\n  K\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.png"] = Img(2, 1, night, nightHalf),
            };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, palette);
            Assert.IsEmpty(plan.Errors);
            Assert.AreEqual(1, plan.NewChars.Count);
            Assert.AreEqual("#1a1c2c80", plan.NewChars[0].value);
        }

        [Test]
        public void BuildPlan_offpalette_color_errors_with_coordinate()
        {
            var palette = GplPalette.Parse("GIMP Palette\n26 28 44\tnight\n");
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.png"] = Img(1, 1, new Color32(1, 2, 3, 255)),
            };
            var plan = PxlPngSync.BuildPlan(
                "palette: @p\nchars:\n  K: night\ngrid:\n  K\n", "d", pngs, palette);
            Assert.AreEqual(1, plan.Errors.Count);
            StringAssert.Contains("#010203", plan.Errors[0]);
            StringAssert.Contains("(0,0)", plan.Errors[0]);
        }

        [Test]
        public void BuildPlan_transparent_maps_to_dot_regardless_of_rgb()
        {
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.png"] = Img(1, 1, new Color32(99, 99, 99, 0)),
            };
            var plan = PxlPngSync.BuildPlan(
                "chars:\n  K: #000000\ngrid:\n  K\n", "d", pngs, null);
            Assert.IsEmpty(plan.Errors);
            Assert.AreEqual(".", plan.Updates[0].Rows.Single());
            Assert.IsEmpty(plan.NewChars);
        }

        [Test]
        public void BuildPlan_resize_violating_border_errors()
        {
            var text = "chars:\n  K: #000000\n[a]\nborder: 2,0,2,0\ngrid:\n  KKKKK\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.a.png"] = Img(3, 1, K, K, K),
            };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            Assert.AreEqual(1, plan.Errors.Count);
            StringAssert.Contains("border", plan.Errors[0]);
        }

        [Test]
        public void BuildPlan_alphabet_exhaustion_errors()
        {
            var px = new Color32[90];
            for (var i = 0; i < 90; i++) px[i] = new Color32((byte)i, (byte)(i * 2), (byte)(i + 7), 255);
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(90, 1, px) };
            var plan = PxlPngSync.BuildPlan("chars:\n  K: #000000\ngrid:\n  K\n", "d", pngs, null);
            Assert.IsTrue(plan.Errors.Any(e => e.Contains("quantize")));
        }

        [Test]
        public void BuildPlan_new_chars_without_chars_block_errors()
        {
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(1, 1, K) };
            var plan = PxlPngSync.BuildPlan("grid:\n  .\n", "d", pngs, null);
            Assert.IsTrue(plan.Errors.Any(e => e.Contains("chars:")));
        }
    }
}
