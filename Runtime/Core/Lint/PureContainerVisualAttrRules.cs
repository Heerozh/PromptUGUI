using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Flags visual attributes that the target tag will silently drop. Three tiers, and the middle
    /// one is the whole reason this class is not just a list of layout containers:
    ///
    /// <list type="number">
    /// <item><c>Frame</c> grew a procedural visual layer (fill / radius / border / glow, drawn by
    /// <c>ProceduralPanel</c>), so those attributes are legitimate on it — but it still has no
    /// <c>Image</c>, so <c>sprite=</c> is as dead there as it always was.</item>
    ///
    /// <item><b>A built-in control that has NOT been given a procedural surface yet</b> carries an
    /// <c>Image</c> somewhere, so <c>color</c> and <c>sprite</c> work — but it has no
    /// <c>ProceduralPanel</c>, so <c>radius</c> / <c>borderWidth</c> / <c>glow</c> / <c>glass</c> …
    /// are dropped. Such an attribute passes the parser, passes <c>ControlAttributeApplier</c>
    /// (unknown names are skipped, not reported) and reaches nothing. Found the hard way while
    /// skinning the farm/glass sample. This tier <b>shrinks</b> as controls are wired up — see
    /// <see cref="ProceduralSurfaceRules.SurfaceTags"/>, which is what it defers to.</item>
    ///
    /// <item><c>VStack</c> / <c>HStack</c> / <c>Grid</c> / <c>SafeArea</c> stay pure layout: no
    /// Graphic, no panel, so every visual attribute is dropped by
    /// <c>ControlAttributeApplier</c>.</item>
    /// </list>
    ///
    /// <para>Tier 2 is keyed off <see cref="BuiltinTags"/> minus
    /// <see cref="ProceduralSurfaceRules.SurfaceTags"/>, rather than a hand-kept list of Image-backed
    /// tags: the two facts underneath are single ones — which tags exist, and which of them draw
    /// procedurally — and a third list would rot against both.
    /// <c>ProceduralAttrNamesTests</c> checks the second against the live registry in both
    /// directions, so a control wired up without being listed (this rule keeps reporting a working
    /// attribute) and a control listed without being wired up (this rule goes quiet about a broken
    /// one) both fail the build.</para>
    ///
    /// <para>A tag that is not a built-in is a Template invocation whose body the CLI cannot see
    /// before expansion, so nothing is claimed about it.</para>
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
        private static string[] ProceduralAttrs => ProceduralAttrNames.All;

        public static bool AppliesTo(string tag) => BuiltinTags.IsBuiltin(tag);

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

            // <Decor> is the third shape of tag: it draws procedurally, but only the glow half of
            // the set — its shape comes from kind=, not from a radius. Everything else on the list
            // really is dropped here, so it stays reported, just with a fix that fits a leaf tag.
            if (n.Tag == DecorRules.Tag)
            {
                foreach (var attr in ProceduralAttrNames.NeedsPanel)
                {
                    if (DecorRules.SupportedProceduralAttrs.Contains(attr)) continue;
                    if (!Declares(n, attr)) continue;
                    yield return new LintIssue(
                        VisualAttrCode, n.Tag, n.Id,
                        $"<Decor id='{n.Id}'>: '{attr}' is silently ignored — a decoration's shape " +
                        "is whatever its kind= names, and it has no surface to put a border, a " +
                        "glass fill or a weld on. Only glow / glowColor carry over. For a shaped " +
                        $"panel, put the {attr} on the host <Frame> instead.");
                }
                yield break;
            }

            if (!LayoutOnlyTags.Contains(n.Tag))
            {
                if (!BuiltinTags.IsBuiltin(n.Tag)) yield break;

                // A control with a procedural surface of its own keeps all of these EXCEPT weld:
                // weld fuses a Frame's direct glass children into one pane and has no meaning on a
                // control (spec §13.2), so it stays silently dropped and stays reported.
                var hasSurface = ProceduralSurfaceRules.AppliesTo(n.Tag);

                // Tier 2: color / sprite land on the control's Image; the procedural group has
                // nowhere to land.
                foreach (var attr in ProceduralAttrNames.NeedsPanel)
                {
                    if (hasSurface && attr != "weld") continue;
                    if (!Declares(n, attr)) continue;
                    yield return new LintIssue(
                        VisualAttrCode, n.Tag, n.Id,
                        hasSurface
                            ? $"<{n.Tag} id='{n.Id}'>: 'weld' is silently ignored — it fuses a " +
                              "<Frame>'s direct glass children into one continuous pane, and a " +
                              "control has no such children. Wrap the controls in a " +
                              "<Frame weld=\"...\"> if you want them to share one pane."
                            : $"<{n.Tag} id='{n.Id}'>: '{attr}' is silently ignored — this control " +
                              "has no procedural surface; only <Frame> (and the controls already " +
                              "wired up) draw rounded rect / border / glow / glass. " +
                              $"For a shaped background here, put a <Frame anchor=\"stretch\" " +
                              $"{attr}=\"...\"/> INSIDE the control — a Frame never blocks clicks " +
                              "— or wrap the control in one.");
                }
                yield break;
            }

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
