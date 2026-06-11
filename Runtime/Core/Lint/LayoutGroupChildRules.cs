using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Rules that apply to children of a layout-group container (VStack / HStack / Grid).
    /// Caller decides whether parent is a layout group; this function only validates the child.
    /// Used by both <c>ScreenInstantiator</c> (Unity runtime, emits warnings) and the <c>UIXmlLint</c>
    /// CLI tool (build-time, emits errors). Single source of truth for the rule text & predicates.
    /// </summary>
    public static class LayoutGroupChildRules
    {
        public const string AnchorCode = "PUI-LAYOUT-ANCHOR";
        public const string MarginCode = "PUI-LAYOUT-MARGIN";
        public const string FlowOutsideCode = "PUI-FLOW-OUTSIDE-GROUP";

        /// <summary>
        /// True when this child opts out of the layout flow in at least one configuration:
        /// base <c>flow="false"</c>, or any variant override on <c>flow</c> (the author has
        /// explicitly taken over in/out-of-flow switching — anchor/margin may serve the
        /// out-of-flow shape, so static checks stay conservative and let them pass).
        /// </summary>
        public static bool MightBeOutOfFlow(ElementNode child) =>
            (child.Attributes.TryGetValue("flow", out var f) && f == "false")
            || child.VariantOverrides.ContainsKey("flow");

        /// <summary>
        /// True when this child is out of flow in every configuration: base
        /// <c>flow="false"</c> with no variant override that could flip it back.
        /// Used by ScreenInstantiator to skip in-flow-only plumbing (scale-host wrapper).
        /// </summary>
        public static bool AlwaysOutOfFlow(ElementNode child) =>
            child.Attributes.TryGetValue("flow", out var f) && f == "false"
            && !child.VariantOverrides.ContainsKey("flow");

        /// <summary>
        /// Child of a NON-layout-group parent: <c>flow</c> is inert there (free-positioning
        /// parents have no layout flow to opt out of) — flag any usage.
        /// </summary>
        public static IEnumerable<LintIssue> CheckNonLayoutChild(ElementNode child)
        {
            if (child.Attributes.ContainsKey("flow")
                || child.VariantOverrides.ContainsKey("flow"))
                yield return new LintIssue(
                    FlowOutsideCode, child.Tag, child.Id,
                    $"<{child.Tag} id='{child.Id}'>: 'flow' has no effect because the parent is not a layout group (VStack/HStack/Grid) — free-positioning parents have no layout flow to opt out of. " +
                    $"Fix: remove the 'flow' attribute; anchor/margin already position this element freely.");
        }

        public static IEnumerable<LintIssue> CheckChild(ElementNode child)
        {
            // flow="false"（或任一 variant 接管 flow）：子节点（可能）退出排版流，
            // anchor / margin 恢复自由定位语义 —— 不再是误用。
            if (MightBeOutOfFlow(child)) yield break;

            if (child.Attributes.ContainsKey("anchor")
                || child.VariantOverrides.ContainsKey("anchor"))
                yield return new LintIssue(
                    AnchorCode, child.Tag, child.Id,
                    $"<{child.Tag} id='{child.Id}'>: 'anchor' is ignored because the parent is a layout group (VStack/HStack/Grid), which positions children automatically. " +
                    $"Fix: remove the 'anchor' attribute and use 'size' / 'width' / 'height' to control this child's size; " +
                    $"or, if you need anchor-based positioning, move this element out of the layout group (e.g. into a <Frame>).");

            if (child.Attributes.ContainsKey("margin")
                || child.VariantOverrides.ContainsKey("margin"))
                yield return new LintIssue(
                    MarginCode, child.Tag, child.Id,
                    $"<{child.Tag} id='{child.Id}'>: 'margin' is ignored because the parent is a layout group (VStack/HStack/Grid), which spaces children automatically. " +
                    $"Fix: remove the 'margin' attribute and use the parent stack's 'padding' / 'spacing' for gaps; " +
                    $"or, if you need margin-based offsets, move this element out of the layout group (e.g. into a <Frame>).");
        }
    }
}
