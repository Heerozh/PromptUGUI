using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>&lt;Suggestions&gt;</c> — the suggestion list an <c>&lt;InputField&gt;</c> takes as a part
    /// element (spec 2026-10-18 §7). Placement rules need the parent and so live in the child loop
    /// (<see cref="CheckOutside"/> / <see cref="CheckDuplicate"/>); the host rule runs on the field
    /// (<see cref="CheckHost"/>); the row rule needs the expanded templates (<see cref="CheckRows"/>);
    /// the rest are self-checks on the node. <c>ScreenInstantiator</c> warns with the same issues at
    /// runtime where the control itself does not already say so.
    /// </summary>
    public static class SuggestionsRules
    {
        public const string OutsideCode = "PUI-SUGGEST-OUTSIDE";
        public const string DuplicateCode = "PUI-SUGGEST-DUPLICATE";
        public const string TemplateCode = "PUI-SUGGEST-TEMPLATE";
        public const string LayoutAttrCode = "PUI-SUGGEST-LAYOUT-ATTR";
        public const string ChildCode = "PUI-SUGGEST-CHILD";
        public const string EnumCode = "PUI-SUGGEST-ENUM";
        public const string ValueCode = "PUI-SUGGEST-VALUE";
        public const string MultilineCode = "PUI-SUGGEST-MULTILINE";
        public const string RowSelectableCode = "PUI-SUGGEST-ROW-SELECTABLE";

        public const string Tag = "Suggestions";

        /// <summary>The one tag that adopts it.</summary>
        public const string HostTag = "InputField";

        /// <summary>The field places the panel; the same list as <c>PUI-SCROLLBAR-LAYOUT-ATTR</c>.</summary>
        private static readonly string[] LayoutAttrs =
        {
            "anchor", "size", "width", "height", "margin", "pivot", "flow", "scale", "hidden",
        };

        private static readonly HashSet<string> ChildTags = new() { "Decor", "Scrollbar" };

        public static bool AppliesTo(string tag) => tag == Tag;

        /// <summary>Self-checks: ignored layout attributes, children, itemTemplate, placement, values.</summary>
        public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            if (n == null || n.Tag != Tag) yield break;

            foreach (var attr in LayoutAttrs)
            {
                if (!Declares(n, attr)) continue;
                yield return new LintIssue(
                    LayoutAttrCode, n.Tag, n.Id,
                    $"<Suggestions id='{n.Id}'>: '{attr}' has no effect — the list is placed by its field: as wide " +
                    "as the field (popupWidth sets another width), as tall as its rows (capped by maxHeight and the " +
                    "room on its side), shown only while open. Drop it.");
            }

            foreach (var c in n.Children)
            {
                if (ChildTags.Contains(c.Tag)) continue;
                yield return new LintIssue(
                    ChildCode, n.Tag, n.Id,
                    $"<Suggestions id='{n.Id}'>: <{c.Tag}> is not a child it uses — only <Decor> and <Scrollbar>. " +
                    "Rows come from itemTemplate and BindItems.");
                break;
            }

            // A node whose classes the CLI cannot see may be getting its values from one of them.
            if (styles.IsUncertain(n)) yield break;

            styles.Resolve(n, "itemTemplate", out var template, out var templateVariants);
            if (string.IsNullOrWhiteSpace(template) && !AnyFilled(templateVariants))
                yield return new LintIssue(
                    TemplateCode, n.Tag, n.Id,
                    $"<Suggestions id='{n.Id}'>: itemTemplate is required — the Template (or registered tag) each " +
                    "row is built from. BindItems throws without it.");

            foreach (var value in ValuesOf(n, styles, "placement"))
            {
                if (IsPlacement(value) || IsPlaceholder(value)) continue;
                yield return new LintIssue(
                    EnumCode, n.Tag, n.Id,
                    $"<Suggestions id='{n.Id}'>: placement='{value}' — expected below | above, optionally -start | " +
                    "-end (which edges line up with the field's when the list is wider). The runtime throws.");
            }

            foreach (var attr in new[] { "maxHeight", "gap", "popupWidth" })
                foreach (var value in ValuesOf(n, styles, attr))
                {
                    if (value == null || IsPlaceholder(value)) continue;
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var f) && f >= 0f) continue;
                    yield return new LintIssue(
                        ValueCode, n.Tag, n.Id,
                        $"<Suggestions id='{n.Id}'>: {attr}='{value}' must be a number, 0 or more; the runtime warns and " +
                        "uses the default." + (attr == "maxHeight" ? " maxHeight='0' means \"only the room available\"." : ""));
                }
        }

        /// <summary>
        /// A field with a list that is not single-line: ↑ / ↓ belong to the list while it is open, so the
        /// caret cannot move between lines then (spec §6.2).
        /// </summary>
        public static IEnumerable<LintIssue> CheckHost(ElementNode field, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            if (field == null || field.Tag != HostTag) yield break;
            var hasList = false;
            foreach (var c in field.Children)
                if (c.Tag == Tag) { hasList = true; break; }
            if (!hasList || styles.IsUncertain(field)) yield break;

            foreach (var value in ValuesOf(field, styles, "lineType"))
            {
                if (value is null or "" or "single" || IsPlaceholder(value)) continue;
                yield return new LintIssue(
                    MultilineCode, field.Tag, field.Id,
                    $"<InputField id='{field.Id}'> lineType='{value}' with a <Suggestions> list: while the list is " +
                    "open, ↑ / ↓ move its highlight, not the caret between lines. Suggestions are made for a " +
                    "single-line field.");
                yield break;
            }
        }

        /// <summary>
        /// A row template with a Selectable in it (spec §7): pressing that control takes the selection from
        /// the field and ends the edit. Needs the expanded document — <c>itemTemplate</c> names a Template
        /// the expander has prepared per Screen — so the CLI's expanded pass runs it; the control warns
        /// once at runtime when it builds such a row.
        /// </summary>
        public static IEnumerable<LintIssue> CheckRows(UIDocument expanded)
        {
            if (expanded == null) yield break;
            foreach (var screen in expanded.Screens)
            {
                var stack = new Stack<ElementNode>();
                stack.Push(screen.Root);
                while (stack.Count > 0)
                {
                    var n = stack.Pop();
                    foreach (var c in n.Children) stack.Push(c);
                    if (n.Tag != Tag) continue;
                    if (!n.Attributes.TryGetValue("itemTemplate", out var name) || string.IsNullOrEmpty(name)) continue;
                    if (!screen.Templates.TryGetValue(name, out var tpl) || tpl.Body == null) continue;
                    var selectable = FindSelectable(tpl.Body);
                    if (selectable == null) continue;
                    yield return new LintIssue(
                        RowSelectableCode, n.Tag, n.Id,
                        $"<Suggestions id='{n.Id}'> itemTemplate='{name}': the row contains a <{selectable.Tag}>, a " +
                        "Selectable. Pressing it takes the selection from the field and ends the edit — give the " +
                        "row its looks with <Show on=\"state-*\"> instead.")
                        .WithSource(n.OriginSrc, n.Line, n.InvokedAt);
                }
            }
        }

        private static ElementNode FindSelectable(ElementNode body)
        {
            var stack = new Stack<ElementNode>();
            stack.Push(body);
            while (stack.Count > 0)
            {
                var n = stack.Pop();
                if (NavTargetRules.IsSelectableTag(n.Tag)) return n;
                foreach (var c in n.Children) stack.Push(c);
            }
            return null;
        }

        /// <summary>A <c>&lt;Suggestions&gt;</c> anywhere but directly under an <c>&lt;InputField&gt;</c>.</summary>
        /// <remarks>
        /// A parent the CLI cannot name — a Template invocation — is skipped: its <c>&lt;Slot/&gt;</c>
        /// may sit right inside a field, and the expanded pass sees the truth.
        /// </remarks>
        public static IEnumerable<LintIssue> CheckOutside(ElementNode parent, ElementNode child)
        {
            if (child.Tag != Tag || parent.Tag == HostTag) yield break;
            if (!BuiltinTags.IsBuiltin(parent.Tag)) yield break;
            yield return new LintIssue(OutsideCode, child.Tag, child.Id, OutsideMessage(child.Id, parent.Tag));
        }

        /// <summary>Two lists under one field: the first is the list, the rest are parked.</summary>
        public static IEnumerable<LintIssue> CheckDuplicate(ElementNode host)
        {
            if (host.Tag != HostTag) yield break;
            var seen = false;
            foreach (var c in host.Children)
            {
                if (c.Tag != Tag) continue;
                if (!seen) { seen = true; continue; }
                yield return new LintIssue(DuplicateCode, c.Tag, c.Id, DuplicateMessage(host.Id, c.Id))
                    .WithSource(c.OriginSrc, c.Line, c.InvokedAt);
            }
        }

        public static string OutsideMessage(string id, string parentTag) =>
            $"<Suggestions id='{id}'> is only meaningful as a direct child of <InputField>; found under " +
            $"<{parentTag}>, where no field opens it — the list stays closed.";

        public static string DuplicateMessage(string hostId, string id) =>
            $"<InputField id='{hostId}'>: a second <Suggestions> (id='{id}') — a field takes one list; " +
            "the first in document order is used and this one is parked inactive.";

        private static bool Declares(ElementNode n, string attr)
            => n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr);

        private static bool IsPlaceholder(string value) => value != null && value.Contains("{{");

        // below | above, optionally -start | -end — the runtime's Suggestions.Placement.
        private static bool IsPlacement(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            var v = value.Trim();
            var dash = v.IndexOf('-');
            var side = dash < 0 ? v : v.Substring(0, dash);
            var align = dash < 0 ? "start" : v.Substring(dash + 1);
            return (side == "below" || side == "above") && (align == "start" || align == "end");
        }

        private static bool AnyFilled(IEnumerable<(string Variant, string Value)> variants)
        {
            foreach (var (_, value) in variants)
                if (!string.IsNullOrWhiteSpace(value)) return true;
            return false;
        }

        // The base value (inline or through a class) and every variant value.
        private static IEnumerable<string> ValuesOf(ElementNode n, StyleAttributeView styles, string attr)
        {
            styles.Resolve(n, attr, out var baseValue, out var variants);
            yield return baseValue;
            foreach (var (_, value) in variants) yield return value;
        }
    }
}
