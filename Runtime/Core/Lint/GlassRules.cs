using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Structural checks for the glass fill mode and its <c>weld</c> groups. Value syntax (ranges,
    /// numeric form) lives in <see cref="StyleRules"/> via <see cref="GlassAttrParser"/>; what is
    /// checked here is whether the attributes are on a node that will actually read them.
    ///
    /// Pure C#, dispatched from <see cref="IRWalker"/> — every failure mode below is silent at
    /// runtime (an attribute nothing reads), so the CLI is the only place an author finds out.
    /// </summary>
    public static class GlassRules
    {
        public const string ParamWithoutGlassCode = "PUI-GLASS-PARAM-NO-GLASS";
        public const string WeldSelfCode = "PUI-GLASS-WELD-SELF";
        public const string WeldMembersCode = "PUI-GLASS-WELD-MEMBERS";
        public const string WeldParamPlacementCode = "PUI-GLASS-WELD-PARAM-PLACEMENT";

        /// <summary>
        /// Shader uniform arrays are fixed size; the group shader carries eight slots. Kept in sync
        /// with <c>GlassGroupPanel.MaxMembers</c>.
        /// </summary>
        public const int MaxWeldMembers = 8;

        /// <summary>
        /// Parameters that describe the glass sheet and the light on it. Physically these have to
        /// agree across a welded group — two halves of one continuous pane cannot be frosted
        /// differently — so the group container owns them.
        /// </summary>
        private static readonly string[] GroupAttrs =
        {
            GlassAttrParser.Frost, GlassAttrParser.Dispersion, GlassAttrParser.LightAngle,
            GlassAttrParser.LightIntensity, GlassAttrParser.Saturation, GlassAttrParser.Noise,
        };

        /// <summary>
        /// Parameters that belong to an individual block. <c>depth</c> is the thickness step that
        /// replaces a dividing line; <c>color</c> and <c>radius</c> describe a shape, and the weld
        /// container has none of its own — the fused outline comes from its children.
        /// </summary>
        private static readonly string[] MemberAttrs = { GlassAttrParser.Depth, "color", "radius" };

        public static IEnumerable<LintIssue> Check(ElementNode n)
        {
            var isWeldGroup = Declares(n, GlassAttrParser.Weld);
            var declaresGlassFlag = Declares(n, GlassAttrParser.Glass);

            if (isWeldGroup && IsGlassTrue(n))
                yield return new LintIssue(
                    WeldSelfCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: 'weld' and glass=\"true\" on the same node. " +
                    "A weld group is the container that fuses its glass children — it draws the " +
                    "merged shape itself and has none of its own. Move glass=\"true\" to the " +
                    "children, or drop 'weld'.");

            if (isWeldGroup)
            {
                foreach (var attr in MemberAttrs)
                {
                    if (!Declares(n, attr)) continue;
                    yield return new LintIssue(
                        WeldParamPlacementCode, n.Tag, n.Id,
                        $"<{n.Tag} id='{n.Id}'>: '{attr}' is a per-block parameter and is ignored " +
                        "on a weld container — the thickness step between blocks is exactly what " +
                        $"distinguishes them. Move '{attr}' onto each glass child.");
                }
                yield break;
            }

            // Nothing to say when the author declared `glass` at all: a variant-only
            // glass.mobile="true" is a legitimate way to make a panel glass in one layout.
            if (declaresGlassFlag) yield break;

            foreach (var attr in GlassAttrParser.NumericAttrs)
            {
                if (attr == GlassAttrParser.Weld) continue;
                if (!Declares(n, attr)) continue;
                yield return new LintIssue(
                    ParamWithoutGlassCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: '{attr}' is ignored without glass=\"true\" — " +
                    "the glass parameters only reach the shader in glass mode. " +
                    $"Add glass=\"true\", or drop '{attr}'.");
            }
        }

        /// <summary>
        /// Parent-relative checks for a node carrying <c>weld</c>. Dispatched separately because the
        /// members are its direct children, which a per-node rule cannot see.
        /// </summary>
        public static IEnumerable<LintIssue> CheckWeldGroup(ElementNode n)
        {
            if (!Declares(n, GlassAttrParser.Weld)) yield break;

            var members = 0;
            foreach (var child in n.Children)
            {
                if (!IsGlassTrue(child)) continue;
                members++;

                foreach (var attr in GroupAttrs)
                {
                    if (!Declares(child, attr)) continue;
                    yield return new LintIssue(
                        WeldParamPlacementCode, child.Tag, child.Id,
                        $"<{child.Tag} id='{child.Id}'>: '{attr}' is a group-level parameter and is " +
                        "ignored on a welded block — the blocks are one continuous pane, so they " +
                        $"share it. Move '{attr}' onto the <{n.Tag}> that carries 'weld'.");
                }
            }

            if (members > MaxWeldMembers)
                yield return new LintIssue(
                    WeldMembersCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: weld group has {members} glass children, " +
                    $"more than the {MaxWeldMembers} the group shader can fuse. " +
                    "Split it into two groups.");
            else if (members < 2)
                yield return new LintIssue(
                    WeldMembersCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: weld group has {members} direct glass " +
                    $"{(members == 1 ? "child" : "children")} — 'weld' fuses two or more and does " +
                    "nothing here. Give the container more glass children, or drop 'weld' and let " +
                    "the child draw itself.");
        }

        private static bool Declares(ElementNode n, string attr)
            => n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr);

        private static bool IsGlassTrue(ElementNode n)
        {
            if (n.Attributes.TryGetValue(GlassAttrParser.Glass, out var v)
                && GlassAttrParser.TryParseFlag(GlassAttrParser.Glass, v, out var on, out _) && on)
                return true;
            if (!n.VariantOverrides.TryGetValue(GlassAttrParser.Glass, out var overrides)) return false;
            foreach (var (_, value) in overrides)
                if (GlassAttrParser.TryParseFlag(GlassAttrParser.Glass, value, out var vOn, out _) && vOn)
                    return true;
            return false;
        }
    }
}
