using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Flags visual attributes that the target tag will silently drop.
    ///
    /// <para><c>Frame</c> grew a procedural visual layer (fill / radius / border / glow, drawn by
    /// <c>ProceduralPanel</c>), so those attributes are legitimate on it — but it still has no
    /// <c>Image</c>, so <c>sprite=</c> is as dead there as it always was.</para>
    ///
    /// <para><c>VStack</c> / <c>HStack</c> / <c>Grid</c> / <c>SafeArea</c> stay pure layout: no
    /// Graphic, no panel, so every visual attribute is dropped by
    /// <c>ControlAttributeApplier</c>.</para>
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

        // 纯排版容器：根上既没有 Graphic 也没有 ProceduralPanel，所有视觉属性都被丢弃。
        // 调整这份列表前先确认对应 Control 是否真的不挂 Image —— e.g. <Btn> 根上挂 Image,
        // 加进白名单会误伤。
        private static readonly HashSet<string> LayoutOnlyTags = new()
        {
            "VStack", "HStack", "Grid", "SafeArea",
        };

        // Frame 自己能画的那一组（见 Frame.cs 的 [UIAttr]）。在纯排版容器上写这些同样无效。
        private static readonly string[] ProceduralAttrs =
        {
            "color", "radius", "borderWidth", "borderColor", "glow", "glowColor",
        };

        public static bool AppliesTo(string tag) => tag == "Frame" || LayoutOnlyTags.Contains(tag);

        public static IEnumerable<LintIssue> Check(ElementNode n)
        {
            if (n.Tag == "Frame")
            {
                if (Declares(n, "sprite"))
                    yield return new LintIssue(
                        VisualAttrCode, n.Tag, n.Id,
                        $"<Frame id='{n.Id}'>: 'sprite' is silently ignored — " +
                        "Frame has no Image on its root. " +
                        "Use <Image sprite=\"...\"> (it accepts children, so one node covers both " +
                        "background and container), or draw the background procedurally on the Frame " +
                        "itself with color / radius / borderWidth / glow.");
                yield break;
            }

            if (!LayoutOnlyTags.Contains(n.Tag)) yield break;

            if (Declares(n, "sprite"))
                yield return new LintIssue(
                    VisualAttrCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: 'sprite' is silently ignored — " +
                    $"{n.Tag} has no Image on its root. " +
                    $"Prefer replacing <{n.Tag}> with <Image sprite=\"...\"> — Image accepts children, " +
                    "so one node covers both background and container. " +
                    "If the background must cover a different area than the content, add " +
                    "<Image anchor=\"stretch\" sprite=\"...\"/> as a sibling instead. " +
                    "For a tinted clickable region, use <Btn>.");

            foreach (var attr in ProceduralAttrs)
            {
                if (!Declares(n, attr)) continue;
                yield return new LintIssue(
                    VisualAttrCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: '{attr}' is silently ignored — " +
                    $"{n.Tag} is layout-only and draws nothing. " +
                    $"Wrap it in a <Frame {attr}=\"...\"> (Frame draws fill / radius / border / glow " +
                    $"procedurally and takes children), or move the attribute to an <Image> sibling.");
            }
        }

        // Variant 覆盖里写的视觉属性同样会被丢弃，一并检查。
        private static bool Declares(ElementNode n, string attr)
            => n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr);
    }
}
