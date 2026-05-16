using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Mask-family lint rules for `<Frame>` and `<Image>`.
    /// Consumed by both <c>IRWalker</c> (UIXmlLint CLI, build-time errors) and
    /// <c>ScreenInstantiator</c> (runtime warnings). Single source of truth.
    /// </summary>
    public static class MaskAttributeRules
    {
        public const string FrameSelfCode = "PUI-MASK-FRAME-SELF";
        public const string ValueCode = "PUI-MASK-VALUE";
        public const string PaddingNoRectCode = "PUI-MASK-PADDING-NO-RECT";
        public const string ShowMaskNoSelfCode = "PUI-MASK-SHOWMASK-NO-SELF";
        public const string VariantCode = "PUI-MASK-VARIANT";
        public const string SelfNoSpriteCode = "PUI-MASK-SELF-NO-SPRITE";

        public static IEnumerable<LintIssue> CheckFrame(ElementNode n)
        {
            foreach (var issue in CheckVariantOverrides(n)) yield return issue;

            n.Attributes.TryGetValue("mask", out var mask);
            var hasPadding = n.Attributes.ContainsKey("maskPadding");

            if (!string.IsNullOrEmpty(mask))
            {
                if (mask == "self")
                {
                    yield return new LintIssue(
                        FrameSelfCode, n.Tag, n.Id,
                        $"<Frame id='{n.Id}'>: mask=\"self\" requires an Image graphic on the same GameObject, " +
                        "but Frame has none. Use <Image mask=\"self\"> for stencil masking, " +
                        "or <Frame mask=\"rect\"> for rectangular clipping.");
                }
                else if (mask != "rect")
                {
                    yield return new LintIssue(
                        ValueCode, n.Tag, n.Id,
                        $"<Frame id='{n.Id}'>: mask=\"{mask}\" is invalid. Frame allows only mask=\"rect\".");
                }
            }

            if (hasPadding && mask != "rect")
            {
                yield return new LintIssue(
                    PaddingNoRectCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: maskPadding only takes effect with mask=\"rect\" (RectMask2D); " +
                    "stencil masks have no padding concept. " +
                    "Add mask=\"rect\" or remove maskPadding.");
            }
        }

        public static IEnumerable<LintIssue> CheckImage(ElementNode n)
        {
            yield break; // Task 3 fills this in
        }

        private static IEnumerable<LintIssue> CheckVariantOverrides(ElementNode n)
        {
            if (n.VariantOverrides.ContainsKey("mask")
                || n.VariantOverrides.ContainsKey("showMask")
                || n.VariantOverrides.ContainsKey("maskPadding"))
            {
                yield return new LintIssue(
                    VariantCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: variant overrides on mask / showMask / maskPadding are not supported in v1 " +
                    "(switching mask mode requires AddComponent / Destroy which has performance / lifetime issues). " +
                    "Pick a single mask config; if you need per-variant clipping, split into two Screens or use <Add into=...>.");
            }
        }
    }
}
