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

        // ---- Apply（文本手术）----

        [Test]
        public void Apply_roundtrip_is_byte_identical()
        {
            // spec §4.4 不变量 1：Export → 不改 PNG → Sync → 文本逐字节不变
            const string text =
                "# header comment\npalette: @p\nppu: 16\nchars:\n  K: night\n  W: #f4f4f4\n" +
                "[a]\nborder: 1,1,1,1\ngrid:\n  KKK\n  KWK\n  KKK\n\n[b]\ngrid:\n  WW\n";
            var palette = GplPalette.Parse("GIMP Palette\n26 28 44\tnight\n244 244 244\tpaper\n");
            var doc = PxlParser.Parse(text);
            var colors = PxlColorResolver.Resolve(doc, palette);
            var pngs = new Dictionary<string, PxlPngSync.PngImage>();
            foreach (var (s, i) in doc.Sections.Select((s, i) => (s, i)))
            {
                var bytes = PxlPngExporter.EncodeSection(s, colors);
                var tex = new Texture2D(2, 2);
                ImageConversion.LoadImage(tex, bytes);
                var bottomUp = tex.GetPixels32();
                var topDown = new Color32[tex.width * tex.height];
                for (var y = 0; y < tex.height; y++)
                    System.Array.Copy(bottomUp, (tex.height - 1 - y) * tex.width,
                        topDown, y * tex.width, tex.width);
                pngs[PxlPngExporter.FileNameFor("d", s)] =
                    new PxlPngSync.PngImage(tex.width, tex.height, topDown);
                Object.DestroyImmediate(tex);
            }
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, palette);
            Assert.IsEmpty(plan.Errors);
            Assert.AreEqual(text, PxlPngSync.Apply(text, plan));
        }

        [Test]
        public void Apply_updates_grid_preserves_everything_else()
        {
            const string text =
                "# keep me\nchars:\n  K: #000000\n  W: #ffffff\n[a]\ngrid:\n  KW\n  WK\n\n[b]\ngrid:\n  K\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.a.png"] = Img(2, 2, W, W, W, W),
            };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            var result = PxlPngSync.Apply(text, plan);
            Assert.AreEqual(
                "# keep me\nchars:\n  K: #000000\n  W: #ffffff\n[a]\ngrid:\n  WW\n  WW\n\n[b]\ngrid:\n  K\n",
                result);
        }

        [Test]
        public void Apply_resize_changes_row_count()
        {
            const string text = "chars:\n  K: #000000\ngrid:\n  K\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.png"] = Img(2, 3, K, K, K, K, K, K),
            };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            Assert.AreEqual("chars:\n  K: #000000\ngrid:\n  KK\n  KK\n  KK\n",
                PxlPngSync.Apply(text, plan));
        }

        [Test]
        public void Apply_appends_new_chars_after_last_entry()
        {
            const string text = "chars:\n  K: #000000\ngrid:\n  K\n";
            var red = new Color32(255, 0, 0, 255);
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(1, 1, red) };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            Assert.AreEqual("chars:\n  K: #000000\n  A: #ff0000\ngrid:\n  A\n",
                PxlPngSync.Apply(text, plan));
        }

        [Test]
        public void Apply_result_reimports_with_identical_pixels()
        {
            // spec §4.4 不变量 2（像素保真）+ 3（确定性）
            const string text = "chars:\n  K: #000000\ngrid:\n  K.\n  .K\n";
            var blue = new Color32(0, 0, 255, 255);
            var pngs = new Dictionary<string, PxlPngSync.PngImage>
            {
                ["d.png"] = Img(2, 2, blue, T, T, K),
            };
            var r1 = PxlPngSync.Apply(text, PxlPngSync.BuildPlan(text, "d", pngs, null));
            var r2 = PxlPngSync.Apply(text, PxlPngSync.BuildPlan(text, "d", pngs, null));
            Assert.AreEqual(r1, r2);
            var doc = PxlParser.Parse(r1);
            var colors = PxlColorResolver.Resolve(doc, null);
            var s = doc.Sections[0];
            Assert.AreEqual(blue, colors[s.Rows[0][0]]);
            Assert.AreEqual((byte)0, colors[s.Rows[0][1]].a);
            Assert.AreEqual(K, colors[s.Rows[1][1]]);
        }

        [Test]
        public void Apply_crlf_input_normalized_to_lf()
        {
            var text = "chars:\r\n  K: #000000\r\ngrid:\r\n  K\r\n";
            var pngs = new Dictionary<string, PxlPngSync.PngImage> { ["d.png"] = Img(1, 1, W) };
            var plan = PxlPngSync.BuildPlan(text, "d", pngs, null);
            var result = PxlPngSync.Apply(text, plan);
            StringAssert.DoesNotContain("\r", result);
        }

        [Test]
        public void Apply_throws_when_plan_has_errors()
        {
            var plan = new PxlPngSync.SyncPlan();
            plan.Errors.Add("boom");
            Assert.Throws<System.InvalidOperationException>(
                () => PxlPngSync.Apply("chars:\n  K: #000000\ngrid:\n  K\n", plan));
        }
    }
}
