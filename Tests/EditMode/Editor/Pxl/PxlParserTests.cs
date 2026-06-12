using NUnit.Framework;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PxlParserTests
    {
        [Test]
        public void Parse_implicit_single_section()
        {
            var doc = PxlParser.Parse(
                "chars:\n  K: #1a1c2c\ngrid:\n  .KK.\n  K..K\n");
            Assert.IsNull(doc.PaletteRef);
            Assert.AreEqual(100f, doc.Ppu);
            Assert.AreEqual(1, doc.Sections.Count);
            Assert.IsNull(doc.Sections[0].Name);
            Assert.AreEqual(4, doc.Sections[0].Width);
            Assert.AreEqual(2, doc.Sections[0].Height);
            Assert.AreEqual(".KK.", doc.Sections[0].Rows[0]);
        }

        [Test]
        public void Parse_full_header_and_two_sections()
        {
            var doc = PxlParser.Parse(
                "palette: @main\nppu: 16\nchars:\n  K: dark-blue\n  W: #f4f4f4\n" +
                "# a comment\n" +
                "[normal]\nborder: 1,1,1,1\ngrid:\n  KKK\n  KWK\n  KKK\n\n" +
                "[pressed]\ngrid:\n  WW\n  WW\n");
            Assert.AreEqual("main", doc.PaletteRef);
            Assert.AreEqual(16f, doc.Ppu);
            Assert.AreEqual(2, doc.Chars.Count);
            Assert.AreEqual("dark-blue", doc.Chars['K']);
            Assert.AreEqual(2, doc.Sections.Count);
            Assert.AreEqual("normal", doc.Sections[0].Name);
            Assert.AreEqual(new Vector4(1, 1, 1, 1), doc.Sections[0].Border);
            Assert.AreEqual("pressed", doc.Sections[1].Name);
            Assert.AreEqual(Vector4.zero, doc.Sections[1].Border);
        }

        [Test]
        public void Parse_unknown_grid_char_reports_line()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\ngrid:\n  KK\n  KX\n"));
            Assert.AreEqual(5, ex.Line);
            StringAssert.Contains("'X'", ex.Message);
        }

        [Test]
        public void Parse_ragged_rows_reports_line()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\ngrid:\n  KKK\n  KK\n"));
            Assert.AreEqual(5, ex.Line);
        }

        [Test]
        public void Parse_duplicate_section_name_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\n[a]\ngrid:\n  K\n[a]\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_duplicate_char_key_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\n  K: #ffffff\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_dot_redefined_to_color_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  .: #000000\ngrid:\n  .\n"));
        }

        [Test]
        public void Parse_border_exceeding_size_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\n[a]\nborder: 2,0,2,0\ngrid:\n  KKK\n"));
        }

        [Test]
        public void Parse_empty_grid_section_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\n[a]\ngrid:\n[b]\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_palette_without_at_prefix_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "palette: main\nchars:\n  K: #000000\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_palette_name_with_space_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "palette: @my pal\nchars:\n  K: #000000\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_implicit_then_explicit_section_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\ngrid:\n  K\n[late]\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_unrecognized_line_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\nbogus directive\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_invalid_ppu_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "ppu: 0\nchars:\n  K: #000000\ngrid:\n  K\n"));
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "ppu: abc\nchars:\n  K: #000000\ngrid:\n  K\n"));
        }

        [Test]
        public void Parse_border_after_grid_throws()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  K: #000000\ngrid:\n  K\n\nborder: 0,0,0,0\n"));
            StringAssert.Contains("border must come before grid", ex.Message);
        }

        [Test]
        public void Parse_tiled_true_sets_section_flag()
        {
            var doc = PxlParser.Parse("chars:\n  K: #000000\n\n[a]\nborder: 1,1,1,1\ntiled: true\ngrid:\n  KKK\n  KKK\n  KKK\n");
            Assert.IsTrue(doc.Sections[0].Tiled);
        }

        [Test]
        public void Parse_tiled_defaults_false()
        {
            var doc = PxlParser.Parse("chars:\n  K: #000000\n\n[a]\ngrid:\n  KK\n  KK\n");
            Assert.IsFalse(doc.Sections[0].Tiled);
        }

        [Test]
        public void Parse_tiled_false_explicit_accepted()
        {
            var doc = PxlParser.Parse("chars:\n  K: #000000\n\n[a]\ntiled: false\ngrid:\n  KK\n  KK\n");
            Assert.IsFalse(doc.Sections[0].Tiled);
        }

        [Test]
        public void Parse_tiled_invalid_value_reports_line()
        {
            var ex = Assert.Throws<PxlParseException>(() =>
                PxlParser.Parse("chars:\n  K: #000000\n\n[a]\ntiled: yes\ngrid:\n  KK\n  KK\n"));
            StringAssert.Contains("invalid tiled value 'yes'", ex.Message);
            StringAssert.Contains("line 5", ex.Message);
        }

        [Test]
        public void Parse_tiled_after_grid_throws()
        {
            // Blank line ends the grid; a later tiled: on the same section must hit the
            // "tiled must come before grid" guard (Rows already populated).
            var ex = Assert.Throws<PxlParseException>(() =>
                PxlParser.Parse("chars:\n  K: #000000\n\n[a]\ngrid:\n  KK\n  KK\n\ntiled: true\n"));
            StringAssert.Contains("tiled must come before grid", ex.Message);
        }

        [Test]
        public void Parse_tiled_implicit_section_supported()
        {
            var doc = PxlParser.Parse("chars:\n  K: #000000\n\ntiled: true\ngrid:\n  KK\n  KK\n");
            Assert.IsTrue(doc.Sections[0].Tiled);
        }
    }
}
