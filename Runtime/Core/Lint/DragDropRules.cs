using System;
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// The value grammar of the five drag-and-drop attributes (spec 2026-11-16-drag-drop §5.1) and the
    /// lint codes that report them. Pure C#: the runtime setters parse through these same functions, so
    /// the CLI and a running Screen accept and refuse exactly the same spellings.
    /// </summary>
    public static class DragDropRules
    {
        public const string ValueCode = "PUI-DRAG-VALUE";
        public const string HandleCode = "PUI-DRAG-HANDLE-ID";
        public const string ReorderCode = "PUI-DRAG-REORDER";
        public const string VirtualCode = "PUI-DRAG-VIRTUAL";
        public const string CarouselCardCode = "PUI-DRAG-CAROUSEL-CARD";
        public const string NoTargetCode = "PUI-DROP-NO-SOURCE";

        /// <summary>The attributes every element takes, and a template invocation carries onto its root.</summary>
        public static readonly string[] Attributes = { "drag", "drop", "dragHold", "dragHandle", "dragDuration" };

        /// <summary>
        /// Whether <paramref name="n"/> writes <paramref name="attr"/> at all — base or any variant.
        /// Declared, not resolved: a source switched on only in portrait still has to exist from the start,
        /// because the hooks inside it resolve while the tree is being built.
        /// </summary>
        public static bool Declares(ElementNode n, string attr)
            => n != null && (n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr));

        /// <summary>
        /// <c>drag=</c>: a kind name, or <c>none</c> / empty for off (<paramref name="kind"/> null). False
        /// with an <paramref name="error"/> for anything else — <c>true</c> / <c>false</c> included: the
        /// attribute names what is being carried, it is not a switch.
        /// </summary>
        public static bool TryParseKind(string value, out string kind, out string error)
        {
            kind = null;
            error = null;
            if (value == null) return true;
            var v = value.Trim();
            if (v.Length == 0 || v == "none") return true;
            if (v == "true" || v == "false")
            {
                error = "takes the kind of thing being dragged (drag=\"unit\"), not a switch — \"none\" turns it off";
                return false;
            }
            if (!IsKind(v))
            {
                error = "is not a kind name — letters, digits, '_' and '-', starting with a letter or '_'";
                return false;
            }
            kind = v;
            return true;
        }

        /// <summary>
        /// <c>drop=</c>: kind names separated by spaces or commas, <c>*</c> for any kind, or <c>none</c> /
        /// empty for off (<paramref name="kinds"/> null and <paramref name="any"/> false).
        /// </summary>
        public static bool TryParseDropKinds(string value, out string[] kinds, out bool any, out string error)
        {
            kinds = null;
            any = false;
            error = null;
            if (value == null) return true;
            var v = value.Trim();
            if (v.Length == 0 || v == "none") return true;
            var list = new List<string>();
            foreach (var part in v.Split(new[] { ' ', ',', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == "*")
                {
                    any = true;
                    continue;
                }
                if (!IsKind(part) || part == "true" || part == "false" || part == "none")
                {
                    error = $"'{part}' is not a kind name — list kinds (drop=\"unit item\"), or '*' for any";
                    return false;
                }
                if (!list.Contains(part)) list.Add(part);
            }
            kinds = list.Count > 0 ? list.ToArray() : null;
            return true;
        }

        /// <summary><c>dragHold</c> (<c>auto</c> or a duration) / <c>dragDuration</c> (a duration).</summary>
        public static bool IsDuration(string value, bool allowAuto) => ScrollListRules.IsDuration(value, allowAuto);

        /// <summary>A drag source in some configuration: <c>drag=</c> with a kind, in the base or any variant.</summary>
        public static bool DeclaresSource(ElementNode n, StyleAttributeView styles = null)
        {
            foreach (var (_, value) in Values(n, styles ?? StyleAttributeView.Empty, "drag"))
                if (TryParseKind(value, out var kind, out _) && kind != null) return true;
            return false;
        }

        /// <summary>A drop target in some configuration: <c>drop=</c> taking any kind, in the base or any variant.</summary>
        public static bool DeclaresTarget(ElementNode n, StyleAttributeView styles = null)
        {
            foreach (var (_, value) in Values(n, styles ?? StyleAttributeView.Empty, "drop"))
                if (TryParseDropKinds(value, out var kinds, out var any, out _) && (kinds != null || any)) return true;
            return false;
        }

        // ───── lint ─────

        /// <summary>
        /// <c>PUI-DRAG-VALUE</c>: the four valued attributes, base and every variant, through <c>class=</c> too. The
        /// runtime warns and keeps the previous value; here it is an error where the value was written.
        /// </summary>
        public static IEnumerable<LintIssue> CheckValues(ElementNode n, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            foreach (var (attr, value) in Values(n, styles, "drag"))
                if (!TryParseKind(value, out _, out var error))
                    yield return BadValue(n, attr, value, error);
            foreach (var (attr, value) in Values(n, styles, "drop"))
                if (!TryParseDropKinds(value, out _, out _, out var error))
                    yield return BadValue(n, attr, value, error);
            foreach (var (attr, value) in Values(n, styles, "dragHold"))
                if (!IsDuration(value, allowAuto: true))
                    yield return BadValue(n, attr, value, "is not 'auto' or a duration (0.4s / 400ms / 0.4)");
            foreach (var (attr, value) in Values(n, styles, "dragDuration"))
                if (!IsDuration(value, allowAuto: false))
                    yield return BadValue(n, attr, value, "is not a duration (0.18s / 180ms / 0.18)");
        }

        private static LintIssue BadValue(ElementNode n, string attr, string value, string error) =>
            new LintIssue(ValueCode, n.Tag, n.Id,
                $"<{n.Tag} id='{n.Id}'>: {attr}=\"{value}\" {error}. At runtime the value is ignored and the previous " +
                "one kept.");

        /// <summary>
        /// <c>PUI-DRAG-HANDLE-ID</c>: <c>dragHandle="x"</c> on a drag source whose subtree holds no <c>id="x"</c> — at
        /// runtime the whole source lifts (warned once). A subtree that still holds a template invocation (the
        /// document as written) cannot be judged: the handle may be inside its body, which the expanded pass sees.
        /// </summary>
        public static IEnumerable<LintIssue> CheckHandle(ElementNode n, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            if (!BuiltinTags.IsBuiltin(n.Tag) || !DeclaresSource(n, styles)) yield break;
            styles.Resolve(n, "dragHandle", out var handle, out _);
            if (string.IsNullOrWhiteSpace(handle) || IsPlaceholder(handle)) yield break;
            handle = handle.Trim();
            var unknown = false;
            if (HoldsId(n, handle, ref unknown) || unknown) yield break;
            yield return new LintIssue(HandleCode, n.Tag, n.Id,
                $"<{n.Tag} id='{n.Id}'>: dragHandle=\"{handle}\" names no node inside this drag source. At runtime the " +
                "whole source lifts instead.");
        }

        private static bool HoldsId(ElementNode n, string id, ref bool unknown)
        {
            if (n.Id == id) return true;
            foreach (var child in n.Children)
            {
                if (child.Id == id) return true;
                if (!BuiltinTags.IsBuiltin(child.Tag)) unknown = true;
                if (HoldsId(child, id, ref unknown)) return true;
            }
            return false;
        }

        /// <summary>
        /// Screen-wide, with the document's templates (a list's rows are its <c>itemTemplate</c>'s body):
        /// <c>PUI-DRAG-REORDER</c> — a source in a row of a list that reorders (two gestures over one drag);
        /// <c>PUI-DRAG-VIRTUAL</c> — a source in a row of a virtual list (rows are recycled while it scrolls);
        /// <c>PUI-DRAG-CAROUSEL-CARD</c> — a source that IS a carousel card (the carousel repositions its cards).
        /// Reported on the source.
        /// </summary>
        public static IEnumerable<LintIssue> CheckContainers(
            ElementNode node, IReadOnlyDictionary<string, TemplateDef> templates, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            if (node.Tag == "ScrollList")
            {
                var reorder = DeclaresTrue(node, styles, "reorder");
                var virtualize = DeclaresTrue(node, styles, "virtualize");
                if (reorder || virtualize)
                    foreach (var row in RowsOf(node, templates, styles))
                        foreach (var source in SourcesIn(row, styles))
                            yield return Located(source, reorder
                                ? new LintIssue(ReorderCode, source.Tag, source.Id,
                                    $"<{source.Tag} id='{source.Id}'>: drag= inside a row of <ScrollList id='{node.Id}' " +
                                    "reorder=\"true\"> — two gestures would fight over one drag. Dragging a row out of a " +
                                    "sortable list is not supported yet; turn one of them off.")
                                : new LintIssue(VirtualCode, source.Tag, source.Id,
                                    $"<{source.Tag} id='{source.Id}'>: drag= inside a row of <ScrollList id='{node.Id}' " +
                                    "virtualize=\"true\"> — a virtual list recycles its rows while it scrolls, so the dragged " +
                                    "row could become another item. Not supported yet."));
            }
            else if (node.Tag == "Carousel")
            {
                foreach (var card in RowsOf(node, templates, styles))
                    if (DeclaresSource(card, styles))
                        yield return Located(card, new LintIssue(CarouselCardCode, card.Tag, card.Id,
                            $"<{card.Tag} id='{card.Id}'>: drag= on a <Carousel> card itself — the carousel positions its " +
                            "cards and would pull it back. Put drag= on a node inside the card."));
            }
            foreach (var child in node.Children)
                foreach (var issue in CheckContainers(child, templates, styles))
                    yield return issue;
        }

        private static LintIssue Located(ElementNode n, LintIssue issue) => issue.WithSource(n.OriginSrc, n.Line, n.InvokedAt);

        // A list's rows: its static children, and its itemTemplate's body when this document declares it.
        private static IEnumerable<ElementNode> RowsOf(
            ElementNode list, IReadOnlyDictionary<string, TemplateDef> templates, StyleAttributeView styles)
        {
            foreach (var child in list.Children)
                if (child.Tag != "Scrollbar") yield return child;
            styles.Resolve(list, "itemTemplate", out var itemTemplate, out _);
            if (!string.IsNullOrEmpty(itemTemplate) && templates != null
                && templates.TryGetValue(itemTemplate, out var tpl) && tpl.Body != null)
                yield return tpl.Body;
        }

        private static IEnumerable<ElementNode> SourcesIn(ElementNode n, StyleAttributeView styles)
        {
            if (DeclaresSource(n, styles)) yield return n;
            foreach (var child in n.Children)
                foreach (var s in SourcesIn(child, styles))
                    yield return s;
        }

        private static bool DeclaresTrue(ElementNode n, StyleAttributeView styles, string attr)
        {
            foreach (var (_, value) in Values(n, styles, attr))
                if (value.Trim() == "true") return true;
            return false;
        }

        private static readonly HashSet<string> BareTargetValues = new()
        {
            "drop-armed", "drop-disarmed", "drop-over", "drop-deny", "drop-out", "drop-pending", "receive",
        };

        /// <summary>
        /// <c>PUI-DROP-NO-SOURCE</c>: a bare target value (<c>drop-armed</c> … <c>receive</c>, on <c>on=</c> or
        /// <c>reverse-on=</c>) with no <c>drop=</c> on the node or above it — the runtime throws. Template bodies, their
        /// instance roots and <c>@id</c> forms are left to the runtime, like the other upward rules.
        /// </summary>
        public static IEnumerable<LintIssue> CheckTargetHook(ElementNode n, bool hasTargetAround)
        {
            if (hasTargetAround) yield break;
            if (n.Tag is not ("Trigger" or "Animation" or "Show")) yield break;
            foreach (var attr in new[] { "on", "reverse-on" })
            {
                if (!n.Attributes.TryGetValue(attr, out var value) || !BareTargetValues.Contains(value)) continue;
                yield return new LintIssue(NoTargetCode, n.Tag, n.Id,
                    $"<{n.Tag} {attr}=\"{value}\">: no drop= target on or around it. {value} is a state of a drop target " +
                    "and resolves upward to one — place it inside a control with drop=, or use " +
                    $"{value}@<id>.");
                yield break;
            }
        }

        private static IEnumerable<(string Attr, string Value)> Values(ElementNode n, StyleAttributeView styles, string attr)
        {
            styles.Resolve(n, attr, out var baseValue, out var variants);
            if (baseValue != null && !IsPlaceholder(baseValue)) yield return (attr, baseValue);
            foreach (var (variant, value) in variants)
                if (value != null && !IsPlaceholder(value)) yield return ($"{attr}.{variant}", value);
        }

        // A template body as written still carries its {{param}} placeholders.
        private static bool IsPlaceholder(string value) => value.Contains("{{");

        internal static bool IsKind(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            var c0 = s[0];
            if (!(char.IsLetter(c0) || c0 == '_')) return false;
            for (var i = 1; i < s.Length; i++)
            {
                var c = s[i];
                if (!(char.IsLetterOrDigit(c) || c == '_' || c == '-')) return false;
            }
            return true;
        }
    }
}
