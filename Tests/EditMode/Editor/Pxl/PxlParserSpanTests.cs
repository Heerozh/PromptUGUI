using NUnit.Framework;
using PromptUGUI.Editor;

namespace PromptUGUI.Tests.Editor
{
    public class PxlParserSpanTests
    {
        // 行号注释（1-based）：
        // 1 chars:
        // 2   K: #000000
        // 3   W: #ffffff
        // 4 [a]
        // 5 grid:
        // 6   KW
        // 7   WK
        // 8 (blank)
        // 9 [b]
        // 10 grid:
        // 11   K
        private const string TwoSections =
            "chars:\n  K: #000000\n  W: #ffffff\n[a]\ngrid:\n  KW\n  WK\n\n[b]\ngrid:\n  K\n";

        [Test]
        public void Grid_line_spans_recorded()
        {
            var doc = PxlParser.Parse(TwoSections);
            Assert.AreEqual(6, doc.Sections[0].GridStartLine);
            Assert.AreEqual(7, doc.Sections[0].GridEndLine);
            Assert.AreEqual(11, doc.Sections[1].GridStartLine);
            Assert.AreEqual(11, doc.Sections[1].GridEndLine);
        }

        [Test]
        public void Chars_block_lines_recorded()
        {
            var doc = PxlParser.Parse(TwoSections);
            Assert.AreEqual(1, doc.CharsHeaderLine);
            Assert.AreEqual(3, doc.CharsLastEntryLine);
        }

        [Test]
        public void Chars_lines_zero_when_absent()
        {
            var doc = PxlParser.Parse("grid:\n  .\n");
            Assert.AreEqual(0, doc.CharsHeaderLine);
            Assert.AreEqual(0, doc.CharsLastEntryLine);
        }

        [Test]
        public void Char_order_preserves_declaration_order()
        {
            var doc = PxlParser.Parse(TwoSections);
            Assert.AreEqual(new[] { 'K', 'W' }, doc.CharOrder.ToArray());
        }

        [Test]
        public void Grid_span_ignores_interleaved_comment_lines()
        {
            // 注释行夹在 grid 行之间：span 覆盖整个区间（含注释行）——
            // sync 替换该区间时注释会丢失，这是已声明的取舍（spec §4.3 实施裁决）。
            var doc = PxlParser.Parse("chars:\n  K: #000000\ngrid:\n  K\n# mid\n  K\n");
            Assert.AreEqual(4, doc.Sections[0].GridStartLine);
            Assert.AreEqual(6, doc.Sections[0].GridEndLine);
        }
    }
}
