using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint for <c>blur</c> / <c>glow</c> / <c>glowColor</c> on the sprite graphics
    /// (spec 2026-09-02 §6).
    ///
    /// <para><c>&lt;Image&gt;</c> and <c>&lt;Icon&gt;</c> are the third shape of tag alongside
    /// <c>&lt;Frame&gt;</c> and <c>&lt;Decor&gt;</c>: they draw a glow without having a procedural
    /// surface, because theirs is cast from the sprite's own silhouette rather than from an SDF. So
    /// they accept exactly the glow pair and nothing else of the procedural set —
    /// <see cref="PureContainerVisualAttrRules"/> defers to <see cref="SupportedProceduralAttrs"/>
    /// for that, the same way it defers to <c>DecorRules</c>.</para>
    ///
    /// <para>The radii themselves are numbers of pixels, and are checked by <c>StyleRules</c>'s
    /// shared pixel-value rule (<c>PUI-PROCEDURAL-VALUE</c>) rather than by a code of this family's
    /// own: <c>glow</c> was already in that list, and one grammar deserves one message.</para>
    /// </summary>
    public static class ImageFxRules
    {
        public const string TagCode = "PUI-FX-TAG";
        public const string TypeCode = "PUI-FX-TYPE";
        public const string AttrCode = "PUI-FX-ATTR";
        public const string MaskCode = "PUI-FX-MASK";
        public const string RadiusCode = "PUI-FX-RADIUS";

        /// <summary>
        /// Past this the 25-tap kernel's taps sit further apart than a lod-0 bilinear sample covers,
        /// so a texture with no mip chain draws ghost copies of thin strokes (spec §14.1). Lint sees
        /// neither the texture nor the drawn size, so this is a reminder to enable mipmaps; the
        /// runtime warns precisely, per texture, when it actually falls back (spec §14.5).
        /// </summary>
        public const float RadiusSoftLimit = 6f;

        /// <summary>The tags built on <c>FxImage</c>, and therefore the only ones where blur / glow
        /// do anything. <c>&lt;RawImage&gt;</c> is deliberately absent — M2.</summary>
        internal static readonly HashSet<string> FxTags = new()
        {
            "Image", "Icon",
        };

        /// <summary>The slice of <c>ProceduralAttrNames.NeedsPanel</c> that a sprite graphic really
        /// does draw. Everything else in that set (radius, borders, glass, weld) still has nowhere to
        /// land here and stays reported.</summary>
        internal static readonly HashSet<string> SupportedProceduralAttrs = new()
        {
            "glow", "glowColor",
        };

        /// <summary>The Image <c>type</c> values that draw the single quad the sampling needs.</summary>
        private static readonly HashSet<string> QuadTypes = new()
        {
            "simple", "contain", "cover",
        };

        private static readonly string[] Radii = { "blur", "glow" };

        /// <summary>
        /// CLI, raw pass: <c>blur</c> on a tag that has no <c>FxImage</c> under it. Only
        /// <c>blur</c> — <c>glow</c> / <c>glowColor</c> exist on <c>&lt;Frame&gt;</c> and
        /// <c>&lt;Decor&gt;</c> with their own meaning, and on the remaining tags
        /// <see cref="PureContainerVisualAttrRules"/> already reports them.
        /// </summary>
        public static IEnumerable<LintIssue> CheckTag(ElementNode n, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            if (n == null || FxTags.Contains(n.Tag)) yield break;
            if (!styles.Declares(n, "blur")) yield break;

            styles.Resolve(n, "blur", out var value, out _);
            if (value != null && value.Contains("{{")) yield break;

            yield return new LintIssue(
                TagCode, n.Tag, n.Id,
                $"<{n.Tag} id='{n.Id}'>: blur= is only supported on <Image> / <Icon> — it resamples " +
                "a sprite's own pixels, and those are the tags that draw one. " +
                (n.Tag == "RawImage"
                    ? "<RawImage> is not wired up for it yet (its texture is not in a sprite atlas, " +
                      "which the sampling relies on). "
                    : "") +
                "Fix: put blur= on the inner <Image> / <Icon>.");
        }

        /// <summary>Runtime: the one rule whose failure is a visual surprise rather than an
        /// authoring nit — a sprite that turns out to be Sliced draws no fx at all.</summary>
        public static IEnumerable<LintIssue> CheckImage(ElementNode n) =>
            CheckImage(n, StyleAttributeView.Empty, typeOnly: true);

        /// <summary>CLI, after <c>class=</c> is merged: everything in §6 that is about the node
        /// itself. Used for both <c>&lt;Image&gt;</c> and <c>&lt;Icon&gt;</c>.</summary>
        public static IEnumerable<LintIssue> CheckImage(ElementNode n, StyleAttributeView styles) =>
            CheckImage(n, styles, typeOnly: false);

        private static IEnumerable<LintIssue> CheckImage(
            ElementNode n, StyleAttributeView styles, bool typeOnly)
        {
            styles ??= StyleAttributeView.Empty;
            if (n == null || !FxTags.Contains(n.Tag)) yield break;

            var blur = Number(n, styles, "blur");
            var glow = Number(n, styles, "glow");
            var hasFx = blur > 0f || glow > 0f;

            if (hasFx)
            {
                styles.Resolve(n, "type", out var type, out _);
                if (!string.IsNullOrEmpty(type) && !type.Contains("{{") && !QuadTypes.Contains(type))
                {
                    yield return new LintIssue(
                        TypeCode, n.Tag, n.Id,
                        $"<{n.Tag} id='{n.Id}'>: blur / glow need type=\"simple\" (contain / cover " +
                        $"count too), but this one is type=\"{type}\" — a {type} sprite is drawn as " +
                        "many quads, and the effect samples one. Drop the type, or drop the effect.");
                }
            }

            if (typeOnly) yield break;

            if (!hasFx && styles.Declares(n, "glowColor"))
            {
                yield return new LintIssue(
                    AttrCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: glowColor= without a glow= draws nothing. " +
                    "Add glow=\"<px>\", or drop the colour.");
            }

            if (hasFx)
            {
                styles.Resolve(n, "mask", out var mask, out _);
                if (mask == "self")
                {
                    yield return new LintIssue(
                        MaskCode, n.Tag, n.Id,
                        $"<{n.Tag} id='{n.Id}'>: blur / glow on the same node as mask=\"self\" — the " +
                        "stencil is written by this graphic's own fragments, so the glow becomes part " +
                        "of the mask and children show through it. Put the mask on a parent <Frame>, " +
                        "or the effect on an inner <Image>.");
                }
            }

            foreach (var attr in Radii)
            {
                var value = Number(n, styles, attr);
                if (value <= RadiusSoftLimit) continue;
                yield return new LintIssue(
                    RadiusCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: {attr}=\"{Format(value)}\" is past the {Format(RadiusSoftLimit)}px " +
                    "the plain kernel samples without gaps — on a texture with no mipmaps, wider radii " +
                    "draw ghost copies of thin strokes. Enable mipmaps on the sprite's texture " +
                    "(SpriteAtlas → Generate Mip Maps; TextureImporter → Generate Mipmaps), or keep it " +
                    $"at or under {Format(RadiusSoftLimit)}. The runtime warns per texture when it " +
                    "actually has to fall back.");
            }
        }

        /// <summary>The largest value the attribute takes across its base and every variant; 0 when
        /// it is absent, empty or a template parameter (nothing to judge before expansion).</summary>
        private static float Number(ElementNode n, StyleAttributeView styles, string attr)
        {
            if (!styles.Declares(n, attr)) return 0f;
            styles.Resolve(n, attr, out var baseValue, out var variants);

            var max = Parse(baseValue);
            if (variants != null)
            {
                foreach (var (_, value) in variants)
                {
                    var v = Parse(value);
                    if (v > max) max = v;
                }
            }
            return max;
        }

        private static float Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains("{{")) return 0f;
            return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : 0f;   // not a number: PUI-PROCEDURAL-VALUE owns that message
        }

        private static string Format(float value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
