using NUnit.Framework;
using PromptUGUI.Editor;

namespace PromptUGUI.Tests.Editor
{
    /// <summary>图层解析与合成（spec 2026-08-01-pxl-layers §3/§4）。合成是字符级纯覆盖：
    /// 上层非 '.' 覆盖下层，'.' 穿透。结果只落在 PxlSection.Rows（内存），不回写文件。</summary>
    public class PxlLayerTests
    {
        private const string Chars = "chars:\n  K: #000000\n  M: #808080\n  H: #ffffff\n";

        [Test]
        public void Parse_grid_plus_layer_stacks_bottom_up()
        {
            var doc = PxlParser.Parse(Chars + "grid:\n  KK\n  KK\nlayer: hi\n  H.\n  ..\n");
            var s = doc.Sections[0];
            Assert.AreEqual(2, s.Layers.Count);
            Assert.IsNull(s.Layers[0].Name, "grid: is the anonymous bottom layer");
            Assert.AreEqual("hi", s.Layers[1].Name);
        }

        [Test]
        public void Parse_layer_only_section_is_valid()
        {
            var doc = PxlParser.Parse(Chars + "layer: base\n  KK\n  KK\n");
            var s = doc.Sections[0];
            Assert.AreEqual(1, s.Layers.Count);
            Assert.AreEqual("base", s.Layers[0].Name);
            Assert.AreEqual(new[] { "KK", "KK" }, s.Rows.ToArray());
        }

        [Test]
        public void Flatten_upper_layer_overwrites_and_dot_passes_through()
        {
            var doc = PxlParser.Parse(Chars +
                "grid:\n  KKKK\n  KMMK\n  KKKK\n" +
                "layer: hi\n  ....\n  .HH.\n  ....\n");
            Assert.AreEqual(new[] { "KKKK", "KHHK", "KKKK" }, doc.Sections[0].Rows.ToArray());
        }

        [Test]
        public void Flatten_three_layers_apply_in_declaration_order()
        {
            // 第三层压第二层：同一格 K -> M -> H，最终 H。
            var doc = PxlParser.Parse(Chars +
                "grid:\n  KK\n" +
                "layer: mid\n  M.\n" +
                "layer: top\n  H.\n");
            Assert.AreEqual(new[] { "HK" }, doc.Sections[0].Rows.ToArray());
        }

        [Test]
        public void Flatten_transparent_char_erases_lower_layer()
        {
            // 'X: transparent' 是非 '.' 字符 → 覆盖下层（挖洞），而不是穿透。
            var doc = PxlParser.Parse(
                "chars:\n  K: #000000\n  X: transparent\n" +
                "grid:\n  KKK\n" +
                "layer: hole\n  .X.\n");
            Assert.AreEqual(new[] { "KXK" }, doc.Sections[0].Rows.ToArray());
        }

        [Test]
        public void Parse_blank_line_between_layers_is_allowed()
        {
            var doc = PxlParser.Parse(Chars + "grid:\n  KK\n\nlayer: hi\n  H.\n");
            Assert.AreEqual(2, doc.Sections[0].Layers.Count);
            Assert.AreEqual(new[] { "HK" }, doc.Sections[0].Rows.ToArray());
        }

        [Test]
        public void Parse_layers_are_per_section()
        {
            var doc = PxlParser.Parse(Chars +
                "[normal]\ngrid:\n  KK\nlayer: base\n  M.\n" +
                "[pressed]\ngrid:\n  KK\nlayer: base\n  .M\n");
            Assert.AreEqual(2, doc.Sections.Count);
            Assert.AreEqual(new[] { "MK" }, doc.Sections[0].Rows.ToArray());
            Assert.AreEqual(new[] { "KM" }, doc.Sections[1].Rows.ToArray());
        }

        [Test]
        public void Parse_flat_file_is_a_single_anonymous_layer()
        {
            // 回归：扁平文件在新模型里就是 Layers.Count == 1，Rows 逐字节不变。
            var doc = PxlParser.Parse(Chars + "grid:\n  KMH\n  HMK\n");
            var s = doc.Sections[0];
            Assert.AreEqual(1, s.Layers.Count);
            Assert.IsNull(s.Layers[0].Name);
            Assert.AreEqual(new[] { "KMH", "HMK" }, s.Rows.ToArray());
            Assert.AreEqual(3, s.Width);
            Assert.AreEqual(2, s.Height);
        }

        [Test]
        public void Parse_grid_after_layer_throws()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                Chars + "layer: a\n  KK\ngrid:\n  KK\n"));
            StringAssert.Contains("grid: must come before any layer:", ex.Message);
            Assert.AreEqual(7, ex.Line);
        }

        [Test]
        public void Parse_duplicate_layer_name_in_section_throws()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                Chars + "layer: a\n  KK\nlayer: a\n  H.\n"));
            StringAssert.Contains("duplicate layer name 'a'", ex.Message);
        }

        [Test]
        public void Parse_layer_without_name_throws()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                Chars + "grid:\n  KK\nlayer:\n  H.\n"));
            StringAssert.Contains("layer name", ex.Message);
            Assert.AreEqual(7, ex.Line);
        }

        [Test]
        public void Parse_layer_with_illegal_name_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                Chars + "grid:\n  KK\nlayer: a b\n  H.\n"));
        }

        [Test]
        public void Parse_layer_height_mismatch_reports_layer_header_line()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                Chars + "grid:\n  KK\n  KK\nlayer: hi\n  H.\n"));
            StringAssert.Contains("same size", ex.Message);
            Assert.AreEqual(8, ex.Line, "reported on the offending layer's header line");
        }

        [Test]
        public void Parse_layer_row_width_mismatch_reports_line()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                Chars + "grid:\n  KKK\nlayer: hi\n  HH\n"));
            StringAssert.Contains("row width", ex.Message);
            Assert.AreEqual(8, ex.Line);
        }

        [Test]
        public void Parse_empty_layer_throws()
        {
            Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                Chars + "grid:\n  KK\nlayer: hi\n"));
        }

        [Test]
        public void Parse_border_after_layer_throws()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                Chars + "layer: a\n  KK\n\nborder: 0,0,0,0\n"));
            StringAssert.Contains("border must come before", ex.Message);
        }

        [Test]
        public void Parse_colon_as_chars_key_throws()
        {
            // ':' 保留：否则一行恰好拼出 'layer: x' 的 grid 行会被误判成层头。
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                "chars:\n  :: #000000\ngrid:\n  :\n"));
            StringAssert.Contains("':' cannot be a chars key", ex.Message);
            Assert.AreEqual(2, ex.Line);
        }

        [Test]
        public void Parse_section_with_no_pixel_block_throws()
        {
            var ex = Assert.Throws<PxlParseException>(() => PxlParser.Parse(
                Chars + "[a]\nborder: 0,0,0,0\n[b]\ngrid:\n  K\n"));
            StringAssert.Contains("no grid: or layer:", ex.Message);
        }

        [Test]
        public void Parse_layer_line_span_recorded_for_bottom_layer()
        {
            // GridStartLine/GridEndLine 是 PNG sync 文本手术的锚点，须镜像底层。
            var doc = PxlParser.Parse(Chars + "grid:\n  KK\n  KK\nlayer: hi\n  H.\n  ..\n");
            var s = doc.Sections[0];
            Assert.AreEqual(6, s.GridStartLine);
            Assert.AreEqual(7, s.GridEndLine);
            Assert.AreEqual(6, s.Layers[0].StartLine);
            Assert.AreEqual(7, s.Layers[0].EndLine);
            Assert.AreEqual(8, s.Layers[1].HeaderLine);
            Assert.AreEqual(9, s.Layers[1].StartLine);
        }

        [Test]
        public void Parse_border_and_tiled_still_apply_to_layered_section()
        {
            var doc = PxlParser.Parse(Chars +
                "[a]\nborder: 1,1,1,1\ntiled: true\ngrid:\n  KKK\n  KKK\n  KKK\n" +
                "layer: hi\n  ...\n  .H.\n  ...\n");
            var s = doc.Sections[0];
            Assert.IsTrue(s.Tiled);
            Assert.AreEqual(new UnityEngine.Vector4(1, 1, 1, 1), s.Border);
            Assert.AreEqual(new[] { "KKK", "KHK", "KKK" }, s.Rows.ToArray());
        }
    }
}
