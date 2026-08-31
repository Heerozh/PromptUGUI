using System;
using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>width="hug"</c> / <c>height="hug"</c> and <c>clamp(min, hug, max)</c> (spec
    /// 2026-08-31-hug-reveal-flip-checked-design §1.3 / §1.4.4). String-level detection only —
    /// <c>SizeSpec</c> (Core/Layout) is outside the CLI compile set, and "does this node declare a
    /// hug" needs no parse.
    /// </summary>
    public static class HugRules
    {
        public const string TagCode = "PUI-HUG-TAG";
        public const string ScaleCode = "PUI-HUG-SCALE";
        public const string StretchChildCode = "PUI-HUG-STRETCH-CHILD";

        private static readonly string[] SizeAxes = { "width", "height" };

        /// <summary>
        /// The tags that HAVE a content size to hug: the layout-group containers plus
        /// <c>&lt;ScrollList&gt;</c>. Everything else has no such thing — a <c>&lt;Frame&gt;</c>'s
        /// children are free-positioned, and a leaf's "content size" is what <c>native</c> means.
        /// </summary>
        public static readonly HashSet<string> HugTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "VStack", "HStack", "Grid", "ScrollList",
        };

        /// <summary>True when the raw attribute value is a bare hug or a clamp whose middle is hug.</summary>
        public static bool IsHugValue(string value)
        {
            if (value == null) return false;
            var v = value.Trim();
            if (v == "hug") return true;
            if (!v.StartsWith("clamp(", StringComparison.Ordinal) || !v.EndsWith(")", StringComparison.Ordinal))
                return false;
            var inner = v.Substring("clamp(".Length, v.Length - "clamp(".Length - 1);
            var parts = inner.Split(',');
            return parts.Length == 3 && parts[1].Trim() == "hug";
        }

        /// <summary>
        /// True when <paramref name="axis"/> carries a hug in its base value or in ANY variant
        /// override — declared, not resolved, so the CLI and the runtime see the same documents.
        /// Reads through <c>class=</c> as well, so a style pack cannot smuggle one in.
        /// </summary>
        public static bool HasHug(ElementNode n, string axis, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            styles.Resolve(n, axis, out var baseValue, out var variants);
            if (IsHugValue(baseValue)) return true;
            foreach (var (_, value) in variants)
                if (IsHugValue(value)) return true;
            return false;
        }

        /// <summary>True when either axis declares a hug.</summary>
        public static bool HasHug(ElementNode n, StyleAttributeView styles = null)
        {
            foreach (var axis in SizeAxes)
                if (HasHug(n, axis, styles)) return true;
            return false;
        }

        /// <summary>
        /// <c>PUI-HUG-TAG</c>: hug on a tag that has no content size. The message names the way out
        /// per tag, because there always is one: wrap free-positioned content in a stack, or ask a
        /// leaf for its <c>native</c> size.
        /// </summary>
        public static IEnumerable<LintIssue> CheckHugTag(ElementNode n, StyleAttributeView styles = null)
        {
            if (HugTags.Contains(n.Tag)) yield break;
            foreach (var axis in SizeAxes)
            {
                if (!HasHug(n, axis, styles)) continue;
                yield return new LintIssue(
                    TagCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: {axis}=\"hug\" needs a control with a content size " +
                    "(<VStack> / <HStack> / <Grid> / <ScrollList>). " + WayOut(n.Tag));
                yield break;
            }
        }

        private static string WayOut(string tag) => tag switch
        {
            "Frame" or "Screen" or "SafeArea" or "Btn" or "Tab" or "Toggle" or "TabMenu" or "Carousel"
                or "InputField" or "Dropdown" or "Slider" or "Progress" or "Markdown" =>
                "Fix: wrap the content in a <VStack> and hug that instead — this control's children are "
                + "free-positioned, so it has no content size to take.",
            _ =>
                "Fix: use size=\"native\" (or width=\"native\") — a leaf's content size is its sprite / "
                + "text measurement, which is what native means.",
        };

        /// <summary>
        /// <c>PUI-HUG-SCALE</c>: a hug axis together with <c>scale</c> on the same node. Same reason
        /// as <c>PUI-CLAMP-SCALE</c> — the fitter is the last writer on that axis and would drop the
        /// box-preserving inflation. CLI error; runtime <c>ParseException</c>.
        /// </summary>
        public static IEnumerable<LintIssue> CheckHugScale(ElementNode n, StyleAttributeView styles = null)
        {
            if (!HasHug(n, styles)) yield break;
            if (!n.Attributes.ContainsKey("scale") && !n.VariantOverrides.ContainsKey("scale")) yield break;
            yield return new LintIssue(
                ScaleCode, n.Tag, n.Id,
                $"<{n.Tag} id='{n.Id}'>: width/height=\"hug\" and scale=\"...\" on the same node — " +
                "a hug axis is owned by the layout pass, which would drop the box-preserving inflation " +
                "that scale applies. Fix: move scale to a child (wrap the content) or drop the hug.");
        }

        /// <summary>
        /// <c>PUI-HUG-STRETCH-CHILD</c>: a <c>stretch</c> child on the parent's hug axis. The parent
        /// asks its children how big they want to be and a stretch child answers "nothing, plus
        /// whatever is left over" — of which there is none. Always a typo; the child renders at 0.
        /// </summary>
        public static IEnumerable<LintIssue> CheckHugStretchChild(
            ElementNode parent, ElementNode child, StyleAttributeView styles = null)
        {
            // The hug axis is the parent's MAIN axis only when the stack runs that way; on the cross
            // axis a stretch child is fine (it fills a size the parent got from elsewhere).
            var axis = parent.Tag switch
            {
                "VStack" => "height",
                "HStack" => "width",
                _ => null,
            };
            if (axis == null || !HasHug(parent, axis, styles)) yield break;

            styles ??= StyleAttributeView.Empty;
            styles.Resolve(child, axis, out var baseValue, out var variants);
            if (!IsStretchValue(baseValue))
            {
                var any = false;
                foreach (var (_, value) in variants)
                    if (IsStretchValue(value)) { any = true; break; }
                if (!any) yield break;
            }

            yield return new LintIssue(
                StretchChildCode, child.Tag, child.Id,
                $"<{child.Tag} id='{child.Id}'>: {axis}=\"stretch\" inside a <{parent.Tag} {axis}=\"hug\"> " +
                "collapses to 0 — the parent sizes itself to its children, so there is no leftover space " +
                "to stretch into. Fix: give the child a size, or drop the parent's hug.");
        }

        private static bool IsStretchValue(string value)
        {
            if (value == null) return false;
            var v = value.Trim();
            if (v.StartsWith("stretch", StringComparison.Ordinal)) return true;
            // clamp(min, stretch, _) is a stretch too — it can grow but its preferred is still the floor.
            if (!v.StartsWith("clamp(", StringComparison.Ordinal) || !v.EndsWith(")", StringComparison.Ordinal))
                return false;
            var inner = v.Substring("clamp(".Length, v.Length - "clamp(".Length - 1);
            var parts = inner.Split(',');
            return parts.Length == 3 && parts[1].Trim().StartsWith("stretch", StringComparison.Ordinal);
        }
    }
}
