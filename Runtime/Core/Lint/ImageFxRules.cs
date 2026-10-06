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
    /// own: <c>glow</c> was already in that list, and one grammar deserves one message. How LARGE a
    /// radius may be is not judged here at all: it depends on the texture's mip chain and on the
    /// drawn size, and lint sees neither, so any static threshold would flag a mipmapped atlas as
    /// loudly as a bare texture. <c>FxImage.WarnIfKernelLeavesGaps</c> owns that verdict alone —
    /// per texture, in texels, only when the fragment really stays on the lod-0 kernel
    /// (spec §14.5).</para>
    /// </summary>
    public static class ImageFxRules
    {
        public const string TagCode = "PUI-FX-TAG";
        public const string TypeCode = "PUI-FX-TYPE";
        public const string AttrCode = "PUI-FX-ATTR";
        public const string MaskCode = "PUI-FX-MASK";
        public const string ValueCode = "PUI-FX-VALUE";

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
            "glow", "glowColor", "intensity",
        };

        /// <summary>The Image <c>type</c> values that draw the single quad the sampling needs.</summary>
        private static readonly HashSet<string> QuadTypes = new()
        {
            "simple", "contain", "cover",
        };

        /// <summary>
        /// CLI, raw pass: <c>blur</c> or <c>grayscale</c> on a tag that has no <c>FxImage</c> under
        /// it — the runtime drops both there without a word. Only those two: <c>glow</c> /
        /// <c>glowColor</c> exist on <c>&lt;Frame&gt;</c> and <c>&lt;Decor&gt;</c> with their own
        /// meaning, and on the remaining tags <see cref="PureContainerVisualAttrRules"/> already
        /// reports them.
        /// </summary>
        public static IEnumerable<LintIssue> CheckTag(ElementNode n, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;
            if (n == null || FxTags.Contains(n.Tag)) yield break;
            // A template invocation seen before expansion: its attributes can only be <Param>s
            // (TemplateExpander throws on anything else), and the expanded pass judges the real
            // node in place — the same gate as RaycastRules.
            if (!BuiltinTags.IsBuiltin(n.Tag)) yield break;

            if (DeclaresJudged(n, styles, "blur"))
            {
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

            if (DeclaresJudged(n, styles, GrayscaleAttr))
            {
                yield return new LintIssue(
                    TagCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: grayscale= is only supported on <Image> / <Icon> — it is a " +
                    "switch in their own material. " + GrayscaleElsewhere(n.Tag));
            }
        }

        private const string GrayscaleAttr = "grayscale";

        /// <summary>What to write instead, for the tags that have a way of their own (spec 2026-10-06 §3.5).</summary>
        private static string GrayscaleElsewhere(string tag) => tag switch
        {
            "RawImage" => "<RawImage> is not wired up for it: its material slot belongs to tint=. Show the " +
                          "picture through an <Image> sprite, or grey the texture itself.",
            "Text" => "<Text> draws through TMP's own shader; give it a grey color= instead.",
            "Btn" or "Tab" or "Toggle" or "Collapsible" =>
                $"A disabled <{tag}> (interactable=\"false\") is greyed by default; to grey one picture " +
                "inside it, put grayscale= on that <Image> / <Icon>.",
            _ => "Put it on the <Image> / <Icon> that draws the picture.",
        };

        /// <summary>Declared — inline, through a class or in a variant — and not a template parameter,
        /// whose value only the expansion decides.</summary>
        private static bool DeclaresJudged(ElementNode n, StyleAttributeView styles, string attr)
        {
            if (!styles.Declares(n, attr)) return false;
            styles.Resolve(n, attr, out var value, out _);
            return value == null || !value.Contains("{{");
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

            // Same acceptance as ControlMeta's bool.Parse, which refuses anything else at runtime —
            // with a hard error, so this is the place to hear about it first (spec 2026-10-06 §3.5).
            if (styles.Declares(n, GrayscaleAttr))
            {
                styles.Resolve(n, GrayscaleAttr, out var baseValue, out var variants);
                foreach (var value in Values(baseValue, variants))
                {
                    if (value == null || value.Contains("{{")) continue;
                    if (bool.TryParse(value, out _)) continue;
                    yield return new LintIssue(
                        ValueCode, n.Tag, n.Id,
                        $"<{n.Tag} id='{n.Id}'>: grayscale=\"{value}\" is not a bool — write true or false" +
                        (value.Trim().Length == 0
                            ? ". Unlike intensity / glow, \"\" does not mean off here: a variant goes back " +
                              "with grayscale.<variant>=\"false\"."
                            : "."));
                }
            }

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

        private static IEnumerable<string> Values(
            string baseValue, IReadOnlyList<(string Variant, string Value)> variants)
        {
            yield return baseValue;
            if (variants == null) yield break;
            foreach (var (_, value) in variants) yield return value;
        }

        private static float Parse(string value)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Contains("{{")) return 0f;
            return float.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                ? v
                : 0f;   // not a number: PUI-PROCEDURAL-VALUE owns that message
        }
    }
}
