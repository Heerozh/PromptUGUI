using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Lint rules for the &lt;Progress&gt; control's attribute family.
    /// Consumed by both <c>IRWalker</c> (UIXmlLint CLI) and <c>ScreenInstantiator</c>
    /// (runtime warnings). Single source of truth — mirrors MaskAttributeRules.
    /// </summary>
    public static class ProgressAttributeRules
    {
        public const string ValueRangeCode = "PUI-PROG-VALUE-RANGE";
        public const string ModeCode = "PUI-PROG-MODE";
        public const string DirectionCode = "PUI-PROG-DIRECTION";
        public const string ChildrenCode = "PUI-PROG-CHILDREN";
        public const string MaskVariantCode = "PUI-PROG-MASK-VARIANT";
        public const string NoFillCode = "PUI-PROG-NO-FILL";
        public const string MaskRadiusConflictCode = "PUI-PROG-MASK-RADIUS-CONFLICT";
        /// <summary>The attributes <c>&lt;Progress&gt;</c> no longer takes (spec 2026-09-18 §4.1).</summary>
        public const string RetiredAttrCode = "PUI-PROG-RETIRED-ATTR";
        /// <summary>An explicit clip mask over a procedural fill that glows — the stencil eats the glow (§5.5).</summary>
        public const string MaskClipsGlowCode = "PUI-PROG-MASK-CLIPS-GLOW";

        private static readonly HashSet<string> ValidModes = new HashSet<string> { "scale", "fill" };
        private static readonly HashSet<string> ValidDirections = new HashSet<string>
        {
            "horizontal", "vertical", "reverse-horizontal", "reverse-vertical"
        };

        public static IEnumerable<LintIssue> CheckProgress(ElementNode n)
            => CheckProgress(n, StyleAttributeView.Empty);

        public static IEnumerable<LintIssue> CheckProgress(ElementNode n, StyleAttributeView styles)
        {
            styles ??= StyleAttributeView.Empty;
            // value range (literal only — dynamic bindings parse as non-numeric and skip)
            if (n.Attributes.TryGetValue("value", out var rawValue)
                && float.TryParse(rawValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
                && (v < 0f || v > 1f))
            {
                yield return new LintIssue(
                    ValueRangeCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: value='{rawValue}' is outside [0..1] and will be clamped. " +
                    "Adjust the literal, or ignore this if you bind value dynamically.");
            }

            // mode
            if (n.Attributes.TryGetValue("mode", out var mode) && !ValidModes.Contains(mode))
            {
                yield return new LintIssue(
                    ModeCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: mode='{mode}' is invalid. Valid: scale, fill.");
            }

            // direction
            if (n.Attributes.TryGetValue("direction", out var dir) && !ValidDirections.Contains(dir))
            {
                yield return new LintIssue(
                    DirectionCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: direction='{dir}' is invalid. " +
                    "Valid: horizontal, vertical, reverse-horizontal, reverse-vertical.");
            }

            // fillRadius retired (spec 2026-09-18): the fill IS the primary surface, so `radius` is
            // its corner. Reported on the node whether it wrote the attribute itself or wears a
            // skin pack that carries it — the pack is the case that bites, since <Slider> still
            // takes fillRadius and a shared pack looks perfectly valid. The runtime drops unknown
            // attributes silently, so without this the bar would just quietly stay square.
            if (n.Attributes.ContainsKey("fillRadius") || n.VariantOverrides.ContainsKey("fillRadius")
                || (!styles.IsUncertain(n) && styles.Declares(n, "fillRadius")))
            {
                yield return new LintIssue(
                    RetiredAttrCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: fillRadius is retired — the fill is the bar's primary " +
                    "surface, so write radius (it rounds the fill, the colour bg, and clips a bitmap " +
                    "fill). No tag forwards the old name, so it is silently dropped.");
            }

            // A procedural fill rounds itself and is cut in the shader, so radius never auto-tracks
            // a mask over it — but an explicit mask= / maskRadius= is honoured, and a stencil clips
            // everything outside the shape, the outer glow first of all. A bitmap fill is not
            // reported here: a glow on it is already PUI-PROC-SPRITE-CONFLICT.
            if (!styles.IsUncertain(n) && styles.Declares(n, "glow"))
            {
                styles.Resolve(n, "mask", out var maskSprite, out _);
                styles.Resolve(n, "maskRadius", out var maskRadius, out _);
                styles.Resolve(n, "fill", out var fillSprite, out _);
                // mask="" and maskRadius="" are opt-outs, not clips.
                var clips = (!string.IsNullOrWhiteSpace(maskSprite) && maskSprite != "none")
                            || !string.IsNullOrWhiteSpace(maskRadius);
                if (clips && (string.IsNullOrWhiteSpace(fillSprite) || fillSprite == "none"))
                {
                    yield return new LintIssue(
                        MaskClipsGlowCode, n.Tag, n.Id,
                        $"<Progress id='{n.Id}'>: mask / maskRadius clips the fill's glow — a stencil " +
                        "discards everything outside its shape, and the glow lives there. A procedural " +
                        "fill rounds itself through radius and needs no mask; drop the mask, or drop glow.");
                }
            }

            // Two mask sources, one GameObject. Graphic is [DisallowMultipleComponent], so the
            // sprite wins and the radius is silently dropped.
            if (!styles.IsUncertain(n) && styles.Declares(n, "maskRadius"))
            {
                styles.Resolve(n, "mask", out var maskSprite, out _);
                if (!string.IsNullOrWhiteSpace(maskSprite) && maskSprite != "none")
                {
                    yield return new LintIssue(
                        MaskRadiusConflictCode, n.Tag, n.Id,
                        $"<Progress id='{n.Id}'>: mask=\"{maskSprite}\" and maskRadius are two clip " +
                        "shapes for one layer, and the sprite wins — only one Graphic can live on the " +
                        "mask node. Drop whichever you did not mean.");
                }
            }

            // children
            if (n.Children.Count > 0)
            {
                yield return new LintIssue(
                    ChildrenCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: Progress is a leaf control and does not accept child elements. " +
                    "Use the frame / mask / bg / fill attributes to compose the visual layers.");
            }

            // mask variant override
            if (n.VariantOverrides.ContainsKey("mask"))
            {
                yield return new LintIssue(
                    MaskVariantCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: mask cannot be overridden per Variant (would require " +
                    "AddComponent/Destroy at runtime). Fix mask in the base declaration; other attrs " +
                    "(value / fill / bg / mode / direction) are safe in variants.");
            }

            // No fill: a warning about an attribute that is ABSENT, so it has to see through class=
            // — a skin that carries fillColor in a <Style> is the idiomatic form, and reading only
            // the node would report it as broken (the CLI turns that into a non-zero exit code).
            // A class this document cannot resolve makes the answer unknowable; stay quiet.
            if (n.Attributes.ContainsKey("value")
                && !styles.IsUncertain(n)
                && !styles.Declares(n, "fill")
                && !styles.Declares(n, "fillColor"))
            {
                yield return new LintIssue(
                    NoFillCode, n.Tag, n.Id,
                    $"<Progress id='{n.Id}'>: value is set but neither fill nor fillColor — " +
                    "nothing will be visibly filled.");
            }
        }
    }
}
