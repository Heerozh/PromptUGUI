using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>&lt;Scrollbar&gt;</c> — the part element a scrolling host takes (spec
    /// 2026-09-12-scrollbar-part-element-design §4.5). Placement rules need the parent and so live
    /// in the child loop (<see cref="CheckOutside"/> / <see cref="CheckDuplicate"/> /
    /// <see cref="CheckInAdd"/>); the rest are self-checks on the node.
    /// </summary>
    public static class ScrollbarRules
    {
        public const string OutsideCode = "PUI-SCROLLBAR-OUTSIDE";
        public const string DuplicateCode = "PUI-SCROLLBAR-DUPLICATE";
        public const string InAddCode = "PUI-SCROLLBAR-IN-ADD";
        public const string LayoutAttrCode = "PUI-SCROLLBAR-LAYOUT-ATTR";
        public const string ChildCode = "PUI-SCROLLBAR-CHILD";
        public const string ValueCode = "PUI-SCROLLBAR-VALUE";
        public const string OverlaySpacingCode = "PUI-SCROLLBAR-OVERLAY-SPACING";
        public const string RetiredAttrCode = "PUI-SCROLLBAR-RETIRED-ATTR";

        public const string Tag = "Scrollbar";

        /// <summary>The tags that implement <c>IScrollbarHost</c> — a hand-kept mirror (pure C#).</summary>
        public static readonly HashSet<string> HostTags = new() { "ScrollList", "Dropdown" };

        /// <summary>
        /// The bar writes its own rect from (orientation, thickness); <c>hidden</c> would fight
        /// uGUI's AutoHide, which re-activates the bar every frame.
        /// </summary>
        private static readonly string[] LayoutAttrs =
        {
            "anchor", "size", "width", "height", "margin", "pivot", "flow", "scale", "hidden",
        };

        /// <summary>
        /// The host-side attributes the part element replaced. No built-in tag accepts them any
        /// more, and the runtime drops an unknown attribute silently — so the CLI has to say so.
        /// </summary>
        public static readonly string[] RetiredHostAttrs =
        {
            "scrollbar", "scrollbarColor", "scrollbarHandle", "scrollbarHandleColor",
            "scrollbarWidth", "scrollbarOverlay",
        };

        public static bool AppliesTo(string tag) => tag == Tag;

        public static IEnumerable<LintIssue> Check(ElementNode n) => Check(n, StyleAttributeView.Empty);

        /// <summary>Self-checks: rejected attributes, children, value grammar, overlay + spacing.</summary>
        public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles)
        {
            styles ??= StyleAttributeView.Empty;
            if (n == null || n.Tag != Tag) yield break;

            foreach (var attr in LayoutAttrs)
            {
                if (!Declares(n, attr)) continue;
                var hint = attr switch
                {
                    "width" or "height" or "size" =>
                        "The bar's only dimension is its thickness — write thickness= " +
                        "(width/height/size are the common layout attributes and never reach this tag).",
                    "hidden" =>
                        "uGUI's AutoHide toggles the bar's active state every frame and would fight it — " +
                        "write thickness=\"0\" for no visible bar.",
                    _ => "Drop it.",
                };
                yield return new LintIssue(
                    LayoutAttrCode, n.Tag, n.Id,
                    $"<Scrollbar id='{n.Id}'>: '{attr}' has no effect — a scrollbar's rect is written by " +
                    $"the control from its orientation and thickness. {hint}");
            }

            if (n.Children.Count > 0)
                yield return new LintIssue(
                    ChildCode, n.Tag, n.Id,
                    $"<Scrollbar id='{n.Id}'>: takes no children (found <{n.Children[0].Tag}>); they are " +
                    "not instantiated. Shape and skin are attributes on the bar itself.");

            // A node whose classes the CLI cannot see may be getting its values from one of them.
            if (styles.IsUncertain(n)) yield break;

            var thickness = ScrollbarAttrParser.DefaultThickness;
            var thicknessKnown = true;
            styles.Resolve(n, ScrollbarAttrParser.ThicknessName, out var thicknessValue, out _);
            if (IsPlaceholder(thicknessValue)) thicknessKnown = false;
            else if (!ScrollbarAttrParser.TryParseThickness(thicknessValue, out thickness, out var thicknessError))
            {
                thicknessKnown = false;
                yield return new LintIssue(ValueCode, n.Tag, n.Id, $"<Scrollbar id='{n.Id}'>: {thicknessError}");
            }

            styles.Resolve(n, ScrollbarAttrParser.SpacingName, out var spacingValue, out _);
            if (!IsPlaceholder(spacingValue)
                && !ScrollbarAttrParser.TryParseSpacing(spacingValue, out _, out var spacingError))
                yield return new LintIssue(ValueCode, n.Tag, n.Id, $"<Scrollbar id='{n.Id}'>: {spacingError}");

            styles.Resolve(n, ScrollbarAttrParser.PaddingName, out var paddingValue, out _);
            if (!IsPlaceholder(paddingValue))
            {
                if (!ScrollbarAttrParser.TryParsePadding(paddingValue, out _, out var across, out var paddingError))
                    yield return new LintIssue(ValueCode, n.Tag, n.Id, $"<Scrollbar id='{n.Id}'>: {paddingError}");
                else if (thicknessKnown)
                {
                    ScrollbarAttrParser.HandleThickness(thickness, across, out var clamped);
                    if (clamped)
                        yield return new LintIssue(
                            ValueCode, n.Tag, n.Id,
                            $"<Scrollbar id='{n.Id}'>: padding=\"{paddingValue}\" leaves no handle — " +
                            $"thickness ({thickness.ToString(System.Globalization.CultureInfo.InvariantCulture)}) " +
                            "minus twice the across inset is ≤ 0. The runtime clamps the handle to 1 unit; " +
                            "reduce the second padding value or thicken the bar.");
                }
            }

            // Per-attribute variant values share the base grammar.
            foreach (var (name, value) in VariantValues(n))
            {
                if (IsPlaceholder(value)) continue;
                string error = null;
                var ok = name switch
                {
                    ScrollbarAttrParser.ThicknessName => ScrollbarAttrParser.TryParseThickness(value, out _, out error),
                    ScrollbarAttrParser.SpacingName => ScrollbarAttrParser.TryParseSpacing(value, out _, out error),
                    ScrollbarAttrParser.PaddingName => ScrollbarAttrParser.TryParsePadding(value, out _, out _, out error),
                    _ => true,
                };
                if (!ok) yield return new LintIssue(ValueCode, n.Tag, n.Id, $"<Scrollbar id='{n.Id}'>: {error}");
            }

            styles.Resolve(n, "overlay", out var overlay, out _);
            if (PromptUGUI.Template.Truthy.Eval(overlay) && styles.Declares(n, ScrollbarAttrParser.SpacingName))
                yield return new LintIssue(
                    OverlaySpacingCode, n.Tag, n.Id,
                    $"<Scrollbar id='{n.Id}'>: spacing= has no effect with overlay=\"true\" — uGUI only reads " +
                    "the scrollbar spacing when the viewport is being shrunk for the bar (overlay=\"false\"). " +
                    "An overlaid bar is kept off the content with the content's own padding.");
        }

        /// <summary>A <c>&lt;Scrollbar&gt;</c> anywhere but directly under a host.</summary>
        /// <remarks>
        /// A parent the CLI cannot name — a Template invocation — is skipped: its <c>&lt;Slot/&gt;</c>
        /// may sit right inside a host, and the expanded pass sees the truth.
        /// </remarks>
        public static IEnumerable<LintIssue> CheckOutside(ElementNode parent, ElementNode child)
        {
            if (child.Tag != Tag || HostTags.Contains(parent.Tag)) yield break;
            if (!BuiltinTags.IsBuiltin(parent.Tag)) yield break;
            yield return new LintIssue(
                OutsideCode, child.Tag, child.Id,
                $"<Scrollbar id='{child.Id}'> is only meaningful as a direct child of <ScrollList> / " +
                $"<Dropdown>; found under <{parent.Tag}>, where nothing scrolls it — the bar is drawn " +
                "but wired to nothing.");
        }

        /// <summary>Two bars under one host: the first is the bar, the rest are parked.</summary>
        public static IEnumerable<LintIssue> CheckDuplicate(ElementNode host)
        {
            if (!HostTags.Contains(host.Tag)) yield break;
            var seen = false;
            foreach (var c in host.Children)
            {
                if (c.Tag != Tag) continue;
                if (!seen) { seen = true; continue; }
                yield return new LintIssue(
                    DuplicateCode, c.Tag, c.Id,
                    $"<{host.Tag} id='{host.Id}'>: a second <Scrollbar> (id='{c.Id}') — a host takes one " +
                    "bar; the first in document order is used and this one is parked inactive.")
                    .WithSource(c.OriginSrc, c.Line, c.InvokedAt);
            }
        }

        /// <summary>
        /// A <c>&lt;Scrollbar&gt;</c> as a direct child of a Variant <c>&lt;Add&gt;</c>: the host has
        /// already built its default bar by the time the block activates, and Strategy C's
        /// SetActive toggling fights uGUI's AutoHide. Override the one bar's attributes per
        /// variant instead (<c>thickness.mobile=</c>).
        /// </summary>
        public static IEnumerable<LintIssue> CheckInAdd(ElementNode addChild)
        {
            if (addChild.Tag != Tag) yield break;
            yield return new LintIssue(
                InAddCode, addChild.Tag, addChild.Id,
                $"<Scrollbar id='{addChild.Id}'> inside a Variant <Add> block is not supported — the host " +
                "already has its bar when the block activates. Put one <Scrollbar> in the host and vary " +
                "its attributes (thickness.mobile=\"4\") instead.");
        }

        /// <summary>The retired host-side <c>scrollbar*</c> attributes on a node.</summary>
        public static IEnumerable<LintIssue> CheckRetired(ElementNode n)
        {
            if (n == null || n.Tag == Tag) yield break;
            foreach (var attr in RetiredHostAttrs)
            {
                if (!Declares(n, attr)) continue;
                yield return new LintIssue(
                    RetiredAttrCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: '{attr}' no longer exists — the scrollbar is a <Scrollbar> child " +
                    $"element now ({Replacement(attr)}). The runtime ignores unknown attributes silently.");
            }
        }

        /// <summary>The same, for a <c>&lt;Style&gt;</c> pack (global or theme-scoped).</summary>
        public static IEnumerable<LintIssue> CheckRetired(StyleDef style)
        {
            if (style == null) yield break;
            foreach (var attr in RetiredHostAttrs)
            {
                if (!style.Attributes.ContainsKey(attr) && !style.VariantOverrides.ContainsKey(attr)) continue;
                yield return new LintIssue(
                    RetiredAttrCode, "Style", style.Name,
                    $"<Style name='{style.Name}'>: '{attr}' no longer exists on any tag — the scrollbar is a " +
                    $"<Scrollbar> child element now ({Replacement(attr)}); put the pack on it via class=.");
            }
        }

        private static string Replacement(string attr) => attr switch
        {
            "scrollbar" => "<Scrollbar sprite=>",
            "scrollbarColor" => "<Scrollbar color=>",
            "scrollbarHandle" => "<Scrollbar handle=>",
            "scrollbarHandleColor" => "<Scrollbar handleColor=>",
            "scrollbarWidth" => "<Scrollbar thickness=>",
            "scrollbarOverlay" => "<Scrollbar overlay=>",
            _ => "<Scrollbar>",
        };

        private static bool Declares(ElementNode n, string attr)
            => n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr);

        private static bool IsPlaceholder(string value) => value != null && value.Contains("{{");

        private static IEnumerable<(string Name, string Value)> VariantValues(ElementNode n)
        {
            foreach (var kv in n.VariantOverrides)
                foreach (var (_, value) in kv.Value)
                    yield return (kv.Key, value);
        }
    }
}
