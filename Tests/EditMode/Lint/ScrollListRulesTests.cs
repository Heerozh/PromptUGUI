using System.Linq;
using NUnit.Framework;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.Tests.EditMode.Lint
{
    public class ScrollListRulesTests
    {
        private static UIDocument Doc(string inner)
            => UIDocumentParser.Parse($@"<?xml version='1.0'?>
<PromptUGUI version='1'><Screen name='S'>{inner}</Screen></PromptUGUI>");

        private static ElementNode Node(params (string Key, string Value)[] attrs)
        {
            var n = new ElementNode("ScrollList") { Id = "slots" };
            foreach (var (key, value) in attrs)
            {
                var dot = key.IndexOf('.');
                if (dot < 0) n.Attributes[key] = value;
                else n.VariantOverrides[key.Substring(0, dot)] =
                    new System.Collections.Generic.List<(string Variant, string Value)>
                    {
                        (key.Substring(dot + 1), value),
                    };
            }
            return n;
        }

        // ───── DeclaresGrid: declared, not resolved ─────

        [Test]
        public void DeclaresGrid_True_For_A_Positive_Base_Columns()
            => Assert.IsTrue(ScrollListRules.DeclaresGrid(Node(("columns", "4"))));

        [Test]
        public void DeclaresGrid_False_Without_Columns()
            => Assert.IsFalse(ScrollListRules.DeclaresGrid(Node()));

        [Test]
        public void DeclaresGrid_False_For_Columns_Zero()
            => Assert.IsFalse(ScrollListRules.DeclaresGrid(Node(("columns", "0"))),
                "columns='0' is the documented way to spell 'no grid'");

        [Test]
        public void DeclaresGrid_True_When_Only_A_Variant_Turns_The_Grid_On()
            => Assert.IsTrue(ScrollListRules.DeclaresGrid(Node(("columns", "0"), ("columns.portrait", "4"))),
                "a configuration that CAN be a grid must be linted as one");

        [Test]
        public void DeclaresGrid_True_When_A_Variant_Turns_The_Grid_Off()
            => Assert.IsTrue(ScrollListRules.DeclaresGrid(Node(("columns", "4"), ("columns.portrait", "0"))));

        // ───── PUI-SCROLL-COLUMNS-DIRECTION ─────

        [Test]
        public void Columns_With_Horizontal_Direction_Is_An_Error()
        {
            var issues = IRWalker.Walk(Doc(
                "<ScrollList columns='4' cellSize='40x40' direction='horizontal'/>")).ToList();
            Assert.That(issues.Any(i => i.Code == ScrollListRules.ColumnsDirectionCode));
        }

        [Test]
        public void A_Horizontal_Variant_Also_Trips_ColumnsDirection()
        {
            var issues = IRWalker.Walk(Doc(
                "<ScrollList columns='4' cellSize='40x40' direction.portrait='horizontal'/>")).ToList();
            Assert.That(issues.Any(i => i.Code == ScrollListRules.ColumnsDirectionCode),
                "the variant is one of the configurations this document can render in");
        }

        [Test]
        public void Horizontal_Without_A_Grid_Is_Fine()
        {
            var issues = IRWalker.Walk(Doc(
                "<ScrollList columns='0' direction='horizontal'/>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == ScrollListRules.ColumnsDirectionCode));
        }

        [Test]
        public void A_Vertical_Grid_Is_Fine()
        {
            var issues = IRWalker.Walk(Doc("<ScrollList columns='4' cellSize='40x40'/>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == ScrollListRules.ColumnsDirectionCode));
        }

        // ───── PUI-SCROLL-COLUMNS-CELLSIZE ─────

        [Test]
        public void Columns_Without_CellSize_Is_An_Error()
        {
            var issues = IRWalker.Walk(Doc("<ScrollList columns='4'/>")).ToList();
            Assert.That(issues.Any(i => i.Code == ScrollListRules.ColumnsCellSizeCode));
        }

        [Test]
        public void A_Base_CellSize_Satisfies_The_Rule()
        {
            var issues = IRWalker.Walk(Doc("<ScrollList columns='4' cellSize='66x100'/>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == ScrollListRules.ColumnsCellSizeCode));
        }

        [Test]
        public void A_Variant_Only_CellSize_Satisfies_The_Rule()
        {
            var issues = IRWalker.Walk(Doc("<ScrollList columns='4' cellSize.portrait='66x100'/>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == ScrollListRules.ColumnsCellSizeCode));
        }

        [Test]
        public void A_CellSize_Arriving_Through_A_Style_Satisfies_The_Rule()
        {
            var issues = IRWalker.Walk(UIDocumentParser.Parse(@"<?xml version='1.0'?>
<PromptUGUI version='1'>
  <Style name='cell' cellSize='66x100'/>
  <Screen name='S'><ScrollList columns='4' class='cell'/></Screen>
</PromptUGUI>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == ScrollListRules.ColumnsCellSizeCode),
                "a style pack is a legitimate place to put the cell size");
        }

        [Test]
        public void No_Grid_Means_No_CellSize_Requirement()
        {
            var issues = IRWalker.Walk(Doc("<ScrollList columns='0'/>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == ScrollListRules.ColumnsCellSizeCode));
        }

        [Test]
        public void A_Plain_List_Trips_Neither_Rule()
        {
            var issues = IRWalker.Walk(Doc("<ScrollList itemTemplate='Row' spacing='4'/>")).ToList();
            Assert.IsFalse(issues.Any(i => i.Code == ScrollListRules.ColumnsDirectionCode
                                        || i.Code == ScrollListRules.ColumnsCellSizeCode));
        }
    }
}
