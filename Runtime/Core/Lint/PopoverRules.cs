using System;
using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>&lt;Popover&gt;</c> — a floating panel anchored to any control (spec 2026-10-19 §7). Self-checks on
    /// the node (<see cref="Check"/>), and the placement rule that needs the parent (<see cref="CheckParent"/>).
    /// <c>ScreenInstantiator</c> mirrors only <see cref="LayoutAttrCode"/> at runtime: the setters throw or
    /// warn for the rest themselves.
    /// </summary>
    public static class PopoverRules
    {
        public const string Tag = "Popover";

        public const string OnCode = "PUI-POPOVER-ON";
        public const string EnumCode = "PUI-POPOVER-ENUM";
        public const string ValueCode = "PUI-POPOVER-VALUE";
        public const string SizeCode = "PUI-POPOVER-SIZE";
        public const string LayoutAttrCode = "PUI-POPOVER-LAYOUT-ATTR";
        public const string ParentCode = "PUI-POPOVER-PARENT";
        public const string HugWidthCode = "PUI-POPOVER-HUG-WIDTH";
        public const string GapCode = "PUI-POPOVER-GAP";

        /// <summary>The popover places itself beside its anchor; these say nothing to it.</summary>
        private static readonly string[] LayoutAttrs = { "anchor", "margin", "pivot", "flow", "scale", "hidden" };

        /// <summary>Parents that take every direct child for a row, a tab, a card or a page.</summary>
        private static readonly HashSet<string> ClaimingParents = new HashSet<string>(StringComparer.Ordinal)
        {
            "ScrollList", "TabBar", "TabMenu", "Carousel", "Pages",
        };

        private static readonly string[] Sides = { "top", "bottom", "left", "right", "over" };
        private static readonly string[] Aligns = { "start", "center", "end" };

        public static bool AppliesTo(string tag) => tag == Tag;

        /// <summary>
        /// Self-checks. <paramref name="insideStateSource"/>: a <c>&lt;Btn&gt;</c> / <c>&lt;Tab&gt;</c> /
        /// <c>&lt;Toggle&gt;</c> is an ancestor — the panel would take its hover / press / disabled looks.
        /// </summary>
        public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles = null,
                                                   bool insideStateSource = false)
        {
            styles ??= StyleAttributeView.Empty;
            if (n == null || n.Tag != Tag) yield break;

            foreach (var attr in LayoutAttrs)
            {
                if (!n.Attributes.ContainsKey(attr) && !n.VariantOverrides.ContainsKey(attr)) continue;
                yield return new LintIssue(
                    LayoutAttrCode, n.Tag, n.Id,
                    $"<Popover id='{n.Id}'>: '{attr}' has no effect — a popover is placed beside its anchor " +
                    "(placement / gap), sized by width / height, and shown only while open. Drop it.");
            }

            if (insideStateSource)
                yield return new LintIssue(
                    ParentCode, n.Tag, n.Id,
                    $"<Popover id='{n.Id}'> inside a <Btn> / <Tab> / <Toggle>: the panel takes that control's " +
                    "hover, press and disabled looks. Declare it beside the control and point at it with on=\"click@<id>\".");

            // A node whose classes the CLI cannot see may be getting its values from one of them.
            if (styles.IsUncertain(n)) yield break;

            foreach (var on in ValuesOf(n, styles, "on"))
            {
                if (on == null || IsPlaceholder(on) || IsValidOn(on)) continue;
                yield return new LintIssue(
                    OnCode, n.Tag, n.Id,
                    $"<Popover id='{n.Id}'> on='{on}': a popover opens on 'click@<id>' (a <Btn> outside it, which " +
                    "also becomes its anchor) or 'manual' (from code). The runtime throws.");
            }

            foreach (var value in ValuesOf(n, styles, "placement"))
            {
                if (string.IsNullOrEmpty(value) || IsPlaceholder(value) || IsPlacement(value)) continue;
                yield return new LintIssue(
                    EnumCode, n.Tag, n.Id,
                    $"<Popover id='{n.Id}'> placement='{value}': expected top | bottom | left | right | over, " +
                    "optionally followed by -start | -center | -end (e.g. 'top-end', 'over-end'). The runtime throws.");
            }

            if (GapUnused(n, styles))
                yield return new LintIssue(
                    GapCode, n.Tag, n.Id,
                    $"<Popover id='{n.Id}'>: gap has no effect — every placement is over-*, which covers the anchor " +
                    "(centred on it) instead of keeping a distance from it. Drop it.");

            foreach (var value in ValuesOf(n, styles, "dismiss"))
            {
                if (value is null or "" or "pass" or "block" || IsPlaceholder(value)) continue;
                yield return new LintIssue(
                    EnumCode, n.Tag, n.Id,
                    $"<Popover id='{n.Id}'> dismiss='{value}': expected pass | block. The runtime throws.");
            }

            foreach (var value in ValuesOf(n, styles, "gap"))
            {
                if (value == null || IsPlaceholder(value)) continue;
                if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f >= 0f) continue;
                yield return new LintIssue(
                    ValueCode, n.Tag, n.Id,
                    $"<Popover id='{n.Id}'> gap='{value}' must be a number, 0 or more; the runtime warns and uses 4.");
            }

            foreach (var value in ValuesOf(n, styles, "transition"))
            {
                if (value == null || IsPlaceholder(value) || ScrollListRules.IsDuration(value, allowAuto: false)) continue;
                yield return new LintIssue(
                    ValueCode, n.Tag, n.Id,
                    $"<Popover id='{n.Id}'> transition='{value}' is not a duration — write seconds (0.15 / 0.15s) or " +
                    "milliseconds (150ms). The runtime warns and uses 0.15s.");
            }

            foreach (var attr in new[] { "width", "height", "size" })
                foreach (var value in ValuesOf(n, styles, attr))
                {
                    if (value == null || IsPlaceholder(value) || !IsBadSize(attr, value)) continue;
                    yield return new LintIssue(
                        SizeCode, n.Tag, n.Id,
                        $"<Popover id='{n.Id}'> {attr}='{value}': a popover has no parent box to stretch into or take " +
                        "a share of, and no native size. Use a number, hug, or clamp(min, hug, max). The runtime throws.");
                    break;
                }

            foreach (var issue in CheckHugWidth(n, styles))
                yield return issue;
        }

        /// <summary>A popover as a direct child of a parent that would take it for a row, a tab, a card or a page.</summary>
        public static IEnumerable<LintIssue> CheckParent(ElementNode parent, ElementNode child)
        {
            if (child.Tag != Tag || !ClaimingParents.Contains(parent.Tag)) yield break;
            yield return new LintIssue(
                ParentCode, child.Tag, child.Id,
                $"<Popover id='{child.Id}'> directly inside <{parent.Tag}>: it would be taken for one of its rows, " +
                "tabs, cards or pages. Declare it outside and point at its trigger with on=\"click@<id>\".");
        }

        /// <summary>
        /// A hugging width (no width is a hug) whose children all stretch across: the column asks them how wide
        /// they want to be, and a stretch child answers 0 — the panel is 0 wide.
        /// </summary>
        private static IEnumerable<LintIssue> CheckHugWidth(ElementNode n, StyleAttributeView styles)
        {
            styles.Resolve(n, "size", out var size, out var sizeVariants);
            if (size != null || sizeVariants.Count > 0) yield break;
            styles.Resolve(n, "width", out var width, out _);
            if (width != null && !HugRules.IsHugValue(width)) yield break;

            var any = false;
            foreach (var c in n.Children)
            {
                if (c.Tag == "Decor") continue;   // not in the column
                if (c.Namespace != null || !BuiltinTags.IsBuiltin(c.Tag)) yield break;   // a template: the expanded pass knows
                if (LayoutGroupChildRules.AlwaysOutOfFlow(c)) continue;
                styles.Resolve(c, "width", out var cw, out _);
                if (cw == null || !cw.Trim().StartsWith("stretch", StringComparison.Ordinal)) yield break;
                any = true;
            }
            if (!any) yield break;
            yield return new LintIssue(
                HugWidthCode, n.Tag, n.Id,
                $"<Popover id='{n.Id}'>: its width hugs its children, and every child is width=\"stretch\" — a " +
                "stretch child asks for nothing, so the panel is 0 wide. Give the popover a width, or one child a width.");
        }

        private static bool IsValidOn(string on)
        {
            var v = on.Trim();
            if (v.Length == 0 || v == "manual") return true;
            const string click = "click@";
            return v.StartsWith(click, StringComparison.Ordinal) && v.Length > click.Length
                   && v.IndexOf('@', click.Length) < 0;
        }

        // A gap is written (base or a variant), and the placement — base and every variant — covers the
        // anchor. An unset placement is bottom-start; one value beside the anchor still uses the gap.
        private static bool GapUnused(ElementNode n, StyleAttributeView styles)
        {
            var gap = false;
            foreach (var value in ValuesOf(n, styles, "gap")) gap |= value != null;
            if (!gap) return false;
            foreach (var value in ValuesOf(n, styles, "placement"))
                if (value == null || IsPlaceholder(value) || !IsPlacement(value)
                    || !value.Trim().StartsWith("over", StringComparison.Ordinal))
                    return false;
            return true;
        }

        private static bool IsPlacement(string value)
        {
            var v = value.Trim();
            var dash = v.IndexOf('-');
            var side = dash < 0 ? v : v.Substring(0, dash);
            var align = dash < 0 ? "start" : v.Substring(dash + 1);
            return Array.IndexOf(Sides, side) >= 0 && Array.IndexOf(Aligns, align) >= 0;
        }

        // stretch / % / native — also inside a clamp, and in either half of a size="WxH".
        private static bool IsBadSize(string attr, string value)
        {
            var v = value.Trim();
            if (v.IndexOf('%') >= 0 || v == "native") return true;
            if (attr == "size" && !v.StartsWith("clamp(", StringComparison.Ordinal))
            {
                foreach (var part in v.Split('x'))
                    if (IsFlexible(part)) return true;
                return false;
            }
            if (v.StartsWith("clamp(", StringComparison.Ordinal) && v.EndsWith(")", StringComparison.Ordinal))
            {
                var parts = v.Substring(6, v.Length - 7).Split(',');
                return parts.Length == 3 && IsFlexible(parts[1]);
            }
            return IsFlexible(v);
        }

        private static bool IsFlexible(string part)
        {
            var p = part.Trim();
            return p.StartsWith("stretch", StringComparison.Ordinal) || p == "native";
        }

        private static bool IsPlaceholder(string value) => value != null && value.Contains("{{");

        // The base value (inline or through a class) and every variant value.
        private static IEnumerable<string> ValuesOf(ElementNode n, StyleAttributeView styles, string attr)
        {
            styles.Resolve(n, attr, out var baseValue, out var variants);
            yield return baseValue;
            foreach (var (_, value) in variants) yield return value;
        }
    }
}
