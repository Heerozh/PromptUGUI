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

        public static IEnumerable<LintIssue> CheckChild(ElementNode child)
        {
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
