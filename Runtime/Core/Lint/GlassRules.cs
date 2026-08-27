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
    ///
    /// <para>All of it goes through a <see cref="StyleAttributeView"/> rather than reading
    /// <see cref="ElementNode.Attributes"/> directly: glass is carried by <c>&lt;Style&gt;</c> /
    /// <c>class=</c> at least as often as inline, and a rule that cannot see styles reports working
    /// layouts as broken.</para>
    /// </summary>
    public static class GlassRules
    {
        public const string ParamWithoutGlassCode = "PUI-GLASS-PARAM-NO-GLASS";
        public const string WeldSelfCode = "PUI-GLASS-WELD-SELF";
        public const string WeldMembersCode = "PUI-GLASS-WELD-MEMBERS";
        public const string WeldParamPlacementCode = "PUI-GLASS-WELD-PARAM-PLACEMENT";
        public const string WeldCornerCode = "PUI-WELD-CORNER";

        /// <summary>
        /// Shader uniform arrays are fixed size; the group shader carries eight slots. Kept in sync
        /// with <c>GlassGroupPanel.MaxMembers</c>.
        /// </summary>
        public const int MaxWeldMembers = 8;

        /// <summary>
        /// Parameters that describe the glass sheet and the light on it, plus the outline drawn
        /// around it. Physically these have to agree across a welded group — two halves of one
        /// continuous pane cannot be frosted differently, and a per-block border would draw exactly
        /// the dividing line the weld exists to remove — so the group container owns them.
        /// </summary>
        private static readonly string[] GroupAttrs =
        {
            GlassAttrParser.Frost, GlassAttrParser.Dispersion, GlassAttrParser.LightAngle,
            GlassAttrParser.LightIntensity, GlassAttrParser.Saturation, GlassAttrParser.Noise,
            "borderWidth", "borderColor", "glow", "glowColor",
        };

        /// <summary>
        /// Parameters that belong to an individual block. <c>depth</c> is the thickness step that
        /// replaces a dividing line; <c>color</c> and <c>radius</c> describe a shape, and the weld
        /// container has none of its own — the fused outline comes from its children.
        /// </summary>
        private static readonly string[] MemberAttrs = { GlassAttrParser.Depth, "color", "radius" };

        public static IEnumerable<LintIssue> Check(ElementNode n)
            => Check(n, StyleAttributeView.Empty);

        public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles)
        {
            styles ??= StyleAttributeView.Empty;

            // A class this document cannot resolve may carry any of these attributes; nothing here
            // can be proven, so say nothing rather than guess.
            if (styles.IsUncertain(n)) yield break;

            var isWeldGroup = styles.Declares(n, GlassAttrParser.Weld);
            var declaresGlassFlag = styles.Declares(n, GlassAttrParser.Glass);

            if (isWeldGroup && IsGlassTrue(n, styles))
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
                    if (!styles.Declares(n, attr)) continue;
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
                if (!styles.Declares(n, attr)) continue;
                yield return new LintIssue(
                    ParamWithoutGlassCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: '{attr}' is ignored without glass=\"true\" — " +
                    "the glass parameters only reach the shader in glass mode. " +
                    $"Add glass=\"true\", or drop '{attr}'.");
            }
        }

        public static IEnumerable<LintIssue> CheckWeldGroup(ElementNode n)
            => CheckWeldGroup(n, StyleAttributeView.Empty);

        /// <summary>
        /// Parent-relative checks for a node carrying <c>weld</c>. Dispatched separately because the
        /// members are its direct children, which a per-node rule cannot see.
        /// </summary>
        public static IEnumerable<LintIssue> CheckWeldGroup(ElementNode n, StyleAttributeView styles)
        {
            styles ??= StyleAttributeView.Empty;
            if (styles.IsUncertain(n)) yield break;
            if (!styles.Declares(n, GlassAttrParser.Weld)) yield break;

            var members = 0;
            // A child whose classes cannot be resolved might or might not be glass, which makes the
            // member count unknowable — placement issues on the children that ARE definitely glass
            // are still worth reporting, so only the count check is dropped.
            var countIsKnowable = true;

            foreach (var child in n.Children)
            {
                if (styles.IsUncertain(child)) { countIsKnowable = false; continue; }
                if (!IsGlassTrue(child, styles)) continue;
                members++;

                foreach (var attr in GroupAttrs)
                {
                    if (!styles.Declares(child, attr)) continue;
                    yield return new LintIssue(
                        WeldParamPlacementCode, child.Tag, child.Id,
                        $"<{child.Tag} id='{child.Id}'>: '{attr}' is a group-level parameter and is " +
                        "ignored on a welded block — the blocks are one continuous pane, so they " +
                        $"share it. Move '{attr}' onto the <{n.Tag}> that carries 'weld'.");
                }

                if (HasCornerTreatment(child, styles))
                    yield return new LintIssue(
                        WeldCornerCode, child.Tag, child.Id,
                        $"<{child.Tag} id='{child.Id}'>: corner treatments do not survive a weld. " +
                        "The group fuses its members' shapes with a smooth union, which rounds " +
                        "every corner back off, so this block draws with plain round corners of " +
                        "the same reach. Drop 'weld' to keep the shape, or write a round radius.");
            }

            if (!countIsKnowable) yield break;

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

        /// <summary>
        /// True when this node's <c>radius</c> asks for anything the weld shader cannot draw —
        /// a per-corner <c>cut</c> / <c>notch</c>, or the <c>hexagon</c> sentinel.
        /// </summary>
        /// <remarks>
        /// Variant values count: shape and weld can arrive from two different theme packs, so the
        /// pairing is at least as likely to show up in one layout only. A value that does not parse
        /// is left alone — <see cref="StyleRules"/> already reports the syntax error, and saying it
        /// twice in two vocabularies helps nobody.
        /// </remarks>
        private static bool HasCornerTreatment(ElementNode n, StyleAttributeView styles)
        {
            styles.Resolve(n, "radius", out var baseValue, out var variants);

            if (IsTreated(baseValue)) return true;
            foreach (var (_, value) in variants)
                if (IsTreated(value))
                    return true;
            return false;

            static bool IsTreated(string value)
            {
                if (!RadiusParser.TryParse(value, out var spec, out _)) return false;
                return spec.Shape == PanelShape.Hexagon
                       || spec.TopLeftCorner.Kind != CornerKind.Round
                       || spec.TopRightCorner.Kind != CornerKind.Round
                       || spec.BottomRightCorner.Kind != CornerKind.Round
                       || spec.BottomLeftCorner.Kind != CornerKind.Round;
            }
        }

        private static bool IsGlassTrue(ElementNode n, StyleAttributeView styles)
        {
            styles.Resolve(n, GlassAttrParser.Glass, out var baseValue, out var variants);

            if (GlassAttrParser.TryParseFlag(GlassAttrParser.Glass, baseValue, out var on, out _)
                && on)
                return true;

            foreach (var (_, value) in variants)
                if (GlassAttrParser.TryParseFlag(GlassAttrParser.Glass, value, out var vOn, out _)
                    && vOn)
                    return true;

            return false;
        }
    }
}
