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
            => CheckImage(n, StyleAttributeView.Empty);

        public static IEnumerable<LintIssue> CheckImage(ElementNode n, StyleAttributeView styles)
        {
            styles ??= StyleAttributeView.Empty;
            foreach (var issue in CheckVariantOverrides(n)) yield return issue;

            // Read through class=: SelfNoSpriteCode reports an attribute that is ABSENT, and a skin
            // carrying the sprite in a <Style> is the idiomatic form — reading only the node reports
            // a correct document as broken. The other three checks go through the same view so a
            // class-supplied mask is judged the same way an inline one is.
            if (styles.IsUncertain(n)) yield break;
            styles.Resolve(n, "mask", out var mask, out _);
            var hasSprite = styles.Declares(n, "sprite");
            var hasPadding = styles.Declares(n, "maskPadding");
            var hasShowMask = styles.Declares(n, "showMask");

            if (!string.IsNullOrEmpty(mask) && mask != "rect" && mask != "self")
            {
                yield return new LintIssue(
                    ValueCode, n.Tag, n.Id,
                    $"<Image id='{n.Id}'>: mask=\"{mask}\" is invalid. Image allows mask=\"rect\" or mask=\"self\".");
            }

            if (mask == "self" && !hasSprite)
            {
                yield return new LintIssue(
                    SelfNoSpriteCode, n.Tag, n.Id,
                    $"<Image id='{n.Id}'>: mask=\"self\" with no sprite= will not clip anything (stencil Mask " +
                    "needs an Image graphic as the mask source). Add sprite=, or use mask=\"rect\" if you want " +
                    "a rectangular clip without a sprite.");
            }

            if (hasPadding && mask != "rect")
            {
                yield return new LintIssue(
                    PaddingNoRectCode, n.Tag, n.Id,
                    $"<Image id='{n.Id}'>: maskPadding only takes effect with mask=\"rect\" (RectMask2D); " +
                    "stencil Mask (mask=\"self\") has no padding concept. " +
                    "Add mask=\"rect\" or remove maskPadding.");
            }

            if (hasShowMask && mask != "self")
            {
                yield return new LintIssue(
                    ShowMaskNoSelfCode, n.Tag, n.Id,
                    $"<Image id='{n.Id}'>: showMask only takes effect with mask=\"self\" (stencil Mask). " +
                    "RectMask2D has no graphic to show/hide. Add mask=\"self\" or remove showMask.");
            }
        }

        /// <summary>
        /// <c>&lt;RawImage&gt;</c> exposes the same mask family as <c>&lt;Image&gt;</c> with the same
        /// add-only implementation, but its image source is a <c>Texture</c> assigned from C# — there
        /// is no <c>sprite=</c>, so <see cref="SelfNoSpriteCode"/> cannot apply and the two cannot
        /// share <see cref="CheckImage"/>.
        ///
        /// <para>Without this it fell through BOTH guards: <c>IRWalker</c> dispatched the mask rules
        /// only for Frame and Image, while <c>VariantBaseRules</c> skips the mask family outright on
        /// the grounds that <see cref="VariantCode"/> "owns it in ALL cases".</para>
        /// </summary>
        public static IEnumerable<LintIssue> CheckRawImage(ElementNode n)
        {
            foreach (var issue in CheckVariantOverrides(n)) yield return issue;

            n.Attributes.TryGetValue("mask", out var mask);

            if (!string.IsNullOrEmpty(mask) && mask != "rect" && mask != "self")
            {
                yield return new LintIssue(
                    ValueCode, n.Tag, n.Id,
                    $"<RawImage id='{n.Id}'>: mask=\"{mask}\" is invalid. " +
                    "RawImage allows mask=\"rect\" or mask=\"self\".");
            }

            if (n.Attributes.ContainsKey("maskPadding") && mask != "rect")
            {
                yield return new LintIssue(
                    PaddingNoRectCode, n.Tag, n.Id,
                    $"<RawImage id='{n.Id}'>: maskPadding only takes effect with mask=\"rect\" " +
                    "(RectMask2D); stencil Mask (mask=\"self\") has no padding concept. " +
                    "Add mask=\"rect\" or remove maskPadding.");
            }

            if (n.Attributes.ContainsKey("showMask") && mask != "self")
            {
                yield return new LintIssue(
                    ShowMaskNoSelfCode, n.Tag, n.Id,
                    $"<RawImage id='{n.Id}'>: showMask only takes effect with mask=\"self\" " +
                    "(stencil Mask). RectMask2D has no graphic to show/hide. " +
                    "Add mask=\"self\" or remove showMask.");
            }
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
