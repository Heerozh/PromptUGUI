using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Pure containers (`Frame` / `VStack` / `HStack` / `Grid` / `SafeArea`) have no `Graphic`
    /// on their root GameObject — `sprite=` / `color=` would be silently dropped by
    /// <c>ControlAttributeApplier</c>. We surface that at lint time so authors don't end up
    /// staring at an invisible background.
    ///
    /// CLI-only: dispatched from <see cref="IRWalker"/>, intentionally NOT from
    /// <c>ScreenInstantiator</c> — silent-drop is correctness-safe (just author confusion),
    /// so runtime `Debug.LogWarning` would be noise. The mask rules in
    /// <see cref="MaskAttributeRules"/> ARE dual-emitted because their failure modes (e.g.
    /// `mask="self"` on `Frame`) silently break visible clipping.
    /// </summary>
    public static class PureContainerVisualAttrRules
    {
        public const string VisualAttrCode = "PUI-CONTAINER-VISUAL-ATTR";

        // 容器 tag 白名单：根上没有 Graphic，sprite/color 被 ControlAttributeApplier 丢弃。
        // 调整这份列表前先确认对应 Control 是否真的不挂 Image —— e.g. <Btn> 根上挂 Image,
        // 加进白名单会误伤。
        private static readonly HashSet<string> PureContainerTags = new()
        {
            "Frame", "VStack", "HStack", "Grid", "SafeArea",
        };

        // sprite / color 都暗示作者期望一个可见 Graphic; 见 SKILL.md "纯容器没有 Image" 那段。
        private static readonly string[] VisualAttrs = { "sprite", "color" };

        public static bool AppliesTo(string tag) => PureContainerTags.Contains(tag);

        public static IEnumerable<LintIssue> Check(ElementNode n)
        {
            if (!AppliesTo(n.Tag)) yield break;

            foreach (var attr in VisualAttrs)
            {
                var inBase = n.Attributes.ContainsKey(attr);
                var inVariant = n.VariantOverrides.ContainsKey(attr);
                if (!inBase && !inVariant) continue;

                yield return new LintIssue(
                    VisualAttrCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: '{attr}' is silently ignored — " +
                    $"{n.Tag} has no Image on its root. " +
                    $"For a background, nest <Image anchor=\"stretch\" {attr}=\"...\"/> inside; " +
                    "for a tinted clickable region, use <Btn> instead.");
            }
        }
    }
}
