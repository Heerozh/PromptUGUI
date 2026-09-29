using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint rules for &lt;ScrollList&gt;. The grid mode (<c>columns</c> / <c>cellSize</c>,
    /// <see cref="CheckScrollList"/>) is consumed by both IRWalker (CLI errors) and ScreenInstantiator
    /// (runtime warnings); drag-to-reorder and <c>virtualize</c> are CLI-only — the control reports
    /// what it has to ignore itself.
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

        // virtualize= (spec 2026-09-29-scrolllist-virtualization §4.3). CLI errors only; the control warns once
        // at runtime itself, with the same code, when it has to ignore something (VIR-P1).
        public const string VirtualLayoutCode = "PUI-SCROLL-VIRTUAL-LAYOUT";
        public const string VirtualReorderCode = "PUI-SCROLL-VIRTUAL-REORDER";
        public const string VirtualReuseCode = "PUI-SCROLL-VIRTUAL-REUSE";
        public const string VirtualHugCode = "PUI-SCROLL-VIRTUAL-HUG";
        public const string VirtualVariantCode = "PUI-SCROLL-VIRTUAL-VARIANT";
        public const string VirtualTemplateCode = "PUI-SCROLL-VIRTUAL-TEMPLATE";

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

        // ───── virtualize (spec 2026-09-29-scrolllist-virtualization §4.3) ─────

        /// <summary>
        /// True when this list asks to be virtual: <c>virtualize</c> is true in its base value or in any variant, through
        /// <c>class=</c> too. The mode is decided once, when the list is built, from whichever of them applies then — so
        /// each of them counts.
        /// </summary>
        public static bool DeclaresVirtualize(ElementNode n, StyleAttributeView styles = null)
        {
            if (n == null) return false;
            return FirstMatch(n, styles ?? StyleAttributeView.Empty, "virtualize", IsTrue).Attr != null;
        }

        /// <summary>
        /// What cannot go with <c>virtualize</c>. CLI only (VIR-P1): at runtime the list sees the merged values, warns
        /// once with the same code and falls back, and the Screen still opens.
        /// </summary>
        public static IEnumerable<LintIssue> CheckVirtualize(ElementNode n, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            styles.Resolve(n, "virtualize", out _, out var virtualizeVariants);
            if (virtualizeVariants.Count > 0)
                yield return new LintIssue(
                    VirtualVariantCode, n.Tag, n.Id,
                    $"<ScrollList id='{n.Id}'>: virtualize.{virtualizeVariants[0].Variant} — virtualize is decided once, " +
                    "when the list is built (it picks Content's layout group), so a variant that switches later is " +
                    "ignored with a runtime warning. Fix: write virtualize without a variant; for a list that must " +
                    "differ per variant, give each variant its own list.");
            if (!DeclaresVirtualize(n, styles)) yield break;

            if (DeclaresGrid(n, styles) || DeclaresHorizontal(n, styles))
                yield return new LintIssue(
                    VirtualLayoutCode, n.Tag, n.Id,
                    $"<ScrollList id='{n.Id}'>: virtualize needs a single vertical column — with 'columns' or " +
                    "direction=\"horizontal\" (here or in a variant) the list is not virtualized at runtime and lays " +
                    "out every row. Fix: drop columns / direction=\"horizontal\", or drop virtualize.");

            var (reorderAttr, _) = FirstMatch(n, styles, "reorder", IsTrue);
            if (reorderAttr != null)
                yield return new LintIssue(
                    VirtualReorderCode, n.Tag, n.Id,
                    $"<ScrollList id='{n.Id}'>: {reorderAttr}=\"true\" on a virtual list — its rows only exist near " +
                    "the viewport, so there is nothing to drag them past; at runtime drag-to-reorder stays off. " +
                    "Fix: drop reorder, or drop virtualize.");

            var (reuseAttr, _) = FirstMatch(n, styles, "reuseItems", IsFalse);
            if (reuseAttr != null)
                yield return new LintIssue(
                    VirtualReuseCode, n.Tag, n.Id,
                    $"<ScrollList id='{n.Id}'>: {reuseAttr}=\"false\" is ignored on a virtual list — recycling rows " +
                    "is what it does. Fix: drop reuseItems, and have the bind callback write every property the row " +
                    "shows.");

            var (hugAttr, hugValue) = FirstMatch(n, styles, "height", HugRules.IsOpenEndedHug);
            if (hugAttr != null)
                yield return new LintIssue(
                    VirtualHugCode, n.Tag, n.Id,
                    $"<ScrollList id='{n.Id}'>: {hugAttr}=\"{hugValue}\" grows the list with its rows and no upper " +
                    "bound, so the viewport always shows all of them and the virtual list realizes every row. " +
                    "Fix: a fixed height, stretch, or a capped clamp(_, hug, N).");

            if (!styles.Declares(n, "itemTemplate"))
                yield return new LintIssue(
                    VirtualTemplateCode, n.Tag, n.Id,
                    $"<ScrollList id='{n.Id}'>: virtualize without an itemTemplate — a virtual list builds its rows " +
                    "from it as they scroll into view, and BindItems throws without one. Fix: add itemTemplate=\"…\".");
        }

        // The first place — base value, then each variant — where attr's value passes the test: its spelling
        // ("reorder" / "reorder.edit") and value, or (null, null).
        private static (string Attr, string Value) FirstMatch(
            ElementNode n, StyleAttributeView styles, string attr, System.Func<string, bool> test)
        {
            styles.Resolve(n, attr, out var baseValue, out var variants);
            if (baseValue != null && test(baseValue)) return (attr, baseValue);
            foreach (var (variant, value) in variants)
                if (value != null && test(value)) return ($"{attr}.{variant}", value);
            return (null, null);
        }

        private static bool IsTrue(string value) => bool.TryParse(value.Trim(), out var b) && b;

        private static bool IsFalse(string value) => bool.TryParse(value.Trim(), out var b) && !b;

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
