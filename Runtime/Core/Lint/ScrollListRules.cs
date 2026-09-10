using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint rules for the grid mode of &lt;ScrollList&gt; (<c>columns</c> / <c>cellSize</c>).
    /// Consumed by both IRWalker (CLI errors) and ScreenInstantiator (runtime warnings).
    /// <para>The children's own attributes are left to the existing shared rules: anchor / margin to
    /// <see cref="LayoutGroupChildRules.CheckChild"/> (a ScrollList is in the layout-group list), and
    /// a cell's own size to <see cref="LayoutGroupChildRules.CheckGridChild"/> with this tag's name.</para>
    /// </summary>
    public static class ScrollListRules
    {
        public const string ColumnsDirectionCode = "PUI-SCROLL-COLUMNS-DIRECTION";
        public const string ColumnsCellSizeCode = "PUI-SCROLL-COLUMNS-CELLSIZE";

        /// <summary>
        /// True when this list asks for the grid in ANY configuration — a base <c>columns</c> or any
        /// <c>columns.&lt;variant&gt;</c> that is not <c>"0"</c>. Declared, not resolved, the same way
        /// <see cref="LayoutGroupChildRules.MightBeOutOfFlow"/> reads <c>flow</c>: the CLI and the
        /// runtime have to agree on which documents carry a grid, and only the declaration is common
        /// to both. Reads through <c>class=</c> so a style pack cannot smuggle one in.
        /// </summary>
        public static bool DeclaresGrid(ElementNode n, StyleAttributeView styles = null)
        {
            if (n == null) return false;
            styles ??= StyleAttributeView.Empty;
            styles.Resolve(n, "columns", out var baseValue, out var variants);
            if (IsGridColumns(baseValue)) return true;
            foreach (var (_, value) in variants)
                if (IsGridColumns(value)) return true;
            return false;
        }

        // "0" (and anything unparseable or non-positive) is the documented "no grid" spelling —
        // a variant that resolves to null is SKIPPED rather than reverted, so dropping the override
        // is not a way back to a single column and authors need a value that means "off".
        private static bool IsGridColumns(string value)
        {
            if (value == null) return false;
            return int.TryParse(value.Trim(), System.Globalization.NumberStyles.Integer,
                                System.Globalization.CultureInfo.InvariantCulture, out var n)
                   && n >= 1;
        }

        private static bool DeclaresHorizontal(ElementNode n, StyleAttributeView styles)
        {
            styles.Resolve(n, "direction", out var baseValue, out var variants);
            if (baseValue == "horizontal") return true;
            foreach (var (_, value) in variants)
                if (value == "horizontal") return true;
            return false;
        }

        /// <summary>Self-check on the &lt;ScrollList&gt; node itself.</summary>
        public static IEnumerable<LintIssue> CheckScrollList(ElementNode n, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            if (!DeclaresGrid(n, styles)) yield break;

            if (DeclaresHorizontal(n, styles))
                yield return new LintIssue(
                    ColumnsDirectionCode, n.Tag, n.Id,
                    $"<ScrollList id='{n.Id}'>: 'columns' and direction=\"horizontal\" cannot both apply — " +
                    "the grid wraps into rows that grow downwards, so it only scrolls vertically, and a " +
                    "row-major grid (rows=) does not exist in v1. At runtime the direction wins and the " +
                    "columns are dropped. Fix: remove direction=\"horizontal\" for a grid, or remove " +
                    "'columns' for a single scrolling row.");

            if (!styles.Declares(n, "cellSize"))
                yield return new LintIssue(
                    ColumnsCellSizeCode, n.Tag, n.Id,
                    $"<ScrollList id='{n.Id}'>: 'columns' needs a 'cellSize' — in grid mode the cell size " +
                    "is the only thing that sizes an item (a child's own size/width/height is ignored), and " +
                    "without it every cell silently falls back to uGUI's 100x100. " +
                    "Fix: add cellSize=\"WxH\".");
        }
    }
}
