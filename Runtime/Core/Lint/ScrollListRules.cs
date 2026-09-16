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
        public const string ReorderHandleCode = "PUI-REORDER-HANDLE-ID";
        public const string ReorderValueCode = "PUI-REORDER-VALUE";

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

        // ───── drag-to-reorder (spec 2026-09-16 §4.5) ─────

        /// <summary>
        /// <c>reorderHold</c> (<c>auto</c> or a duration) and <c>reorderDuration</c> (a duration), base
        /// and every variant, through <c>class=</c> too. Runtime mirrors this with a warning and keeps
        /// the previous value; here it is an error where the value was written.
        /// </summary>
        public static IEnumerable<LintIssue> CheckReorderValues(ElementNode n, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            foreach (var issue in CheckDuration(n, styles, "reorderHold", allowAuto: true))
                yield return issue;
            foreach (var issue in CheckDuration(n, styles, "reorderDuration", allowAuto: false))
                yield return issue;
        }

        private static IEnumerable<LintIssue> CheckDuration(ElementNode n, StyleAttributeView styles, string attr, bool allowAuto)
        {
            styles.Resolve(n, attr, out var baseValue, out var variants);
            if (baseValue != null && !IsDuration(baseValue, allowAuto))
                yield return BadDuration(n, attr, baseValue, allowAuto);
            foreach (var (variant, value) in variants)
                if (value != null && !IsDuration(value, allowAuto))
                    yield return BadDuration(n, $"{attr}.{variant}", value, allowAuto);
        }

        private static LintIssue BadDuration(ElementNode n, string attr, string value, bool allowAuto) =>
            new LintIssue(
                ReorderValueCode, n.Tag, n.Id,
                $"<ScrollList id='{n.Id}'>: {attr}=\"{value}\" is not a duration" +
                (allowAuto ? " (or 'auto')" : "") +
                " — write seconds (0.4 / 0.4s) or milliseconds (400ms). At runtime the value is ignored " +
                "and the previous one kept.");

        // Pure C# twin of AnimationSpec.ParseSeconds (which lives in the Unity half): "0.4", "0.4s",
        // "400ms"; negative is not a duration.
        private static bool IsDuration(string value, bool allowAuto)
        {
            value = value.Trim();
            if (allowAuto && value == "auto") return true;
            if (value.EndsWith("ms")) value = value.Substring(0, value.Length - 2);
            else if (value.EndsWith("s")) value = value.Substring(0, value.Length - 1);
            return float.TryParse(value, System.Globalization.NumberStyles.Float,
                                  System.Globalization.CultureInfo.InvariantCulture, out var f)
                   && f >= 0f;
        }

        /// <summary>
        /// <c>reorderHandle="x"</c> names a node inside each row; a row without it lifts from anywhere
        /// (runtime warns once). Checked against the <c>itemTemplate</c>'s body when this document
        /// declares that template, else against each static child. A template this document does
        /// not declare (an imported library's) cannot be judged and is left alone. Checked whether or
        /// not <c>reorder</c> is on — a variant may switch it on later.
        /// </summary>
        public static IEnumerable<LintIssue> CheckReorderHandle(
            ElementNode n, IReadOnlyDictionary<string, TemplateDef> templates, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            styles.Resolve(n, "reorderHandle", out var handle, out _);
            if (string.IsNullOrEmpty(handle)) yield break;

            styles.Resolve(n, "itemTemplate", out var itemTemplate, out _);
            if (!string.IsNullOrEmpty(itemTemplate))
            {
                if (templates == null || !templates.TryGetValue(itemTemplate, out var tpl) || tpl.Body == null)
                    yield break;
                if (!ContainsId(tpl.Body, handle))
                    yield return new LintIssue(
                        ReorderHandleCode, n.Tag, n.Id,
                        $"<ScrollList id='{n.Id}' reorderHandle='{handle}'>: <Template name='{tpl.Name}'> " +
                        $"(the itemTemplate) has no node with id='{handle}', so the whole row lifts instead. " +
                        "Fix: give the grip node that id, or drop reorderHandle.");
                yield break;
            }

            foreach (var child in n.Children)
            {
                if (child.Tag == "Scrollbar") continue;   // chrome, not a row
                if (ContainsId(child, handle)) continue;
                yield return new LintIssue(
                    ReorderHandleCode, child.Tag, child.Id,
                    $"<ScrollList id='{n.Id}' reorderHandle='{handle}'>: this static row (<{child.Tag}>) has no " +
                    $"node with id='{handle}', so it lifts from anywhere. Fix: give its grip node that id.")
                    .WithSource(child.OriginSrc, child.Line, child.InvokedAt);
            }
        }

        private static bool ContainsId(ElementNode node, string id)
        {
            if (node.Id == id) return true;
            foreach (var child in node.Children)
                if (ContainsId(child, id)) return true;
            return false;
        }
    }
}
