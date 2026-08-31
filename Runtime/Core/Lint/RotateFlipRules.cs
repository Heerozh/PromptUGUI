using System;
using System.Collections.Generic;
using System.Globalization;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// <c>rotation</c> / <c>flip</c> (spec 2026-08-31-hug-reveal-flip-checked-design §3.3). They are
    /// mesh-level effects, so they only exist on the three leaf graphics; on any other tag the
    /// parser drops them silently, which is exactly the kind of nothing-happens this rule exists to
    /// turn into a message.
    /// </summary>
    public static class RotateFlipRules
    {
        public const string TagCode = "PUI-FLIP-TAG";
        public const string ValueCode = "PUI-FLIP-VALUE";

        public static readonly HashSet<string> AllowedTags = new HashSet<string>(StringComparer.Ordinal)
        {
            "Image", "Icon", "RawImage",
        };

        private static readonly string[] FlipValues = { "x", "y", "xy", "none" };

        public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles = null)
        {
            styles ??= StyleAttributeView.Empty;

            var hasRotation = styles.Declares(n, "rotation");
            var hasFlip = styles.Declares(n, "flip");
            if (!hasRotation && !hasFlip) yield break;

            if (!AllowedTags.Contains(n.Tag))
            {
                var attr = hasRotation ? "rotation" : "flip";
                yield return new LintIssue(
                    TagCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: {attr}= is only supported on <Image> / <Icon> / <RawImage> " +
                    "(it rewrites the generated mesh, and those are the tags that generate one). " +
                    "Fix: rotate the inner <Image> / <Icon> instead of its container.");
                yield break;
            }

            if (hasFlip)
            {
                styles.Resolve(n, "flip", out var baseValue, out var variants);
                foreach (var value in Values(baseValue, variants))
                {
                    if (value == null || value.Contains("{{")) continue;
                    if (Array.IndexOf(FlipValues, value) >= 0 || value.Length == 0) continue;
                    yield return new LintIssue(
                        ValueCode, n.Tag, n.Id,
                        $"<{n.Tag} id='{n.Id}'>: flip=\"{value}\" is not valid. " +
                        "Valid: x (horizontal), y (vertical), xy (both), none.");
                }
            }

            if (hasRotation)
            {
                styles.Resolve(n, "rotation", out var baseValue, out var variants);
                foreach (var value in Values(baseValue, variants))
                {
                    if (value == null || value.Length == 0 || value.Contains("{{")) continue;
                    if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _)) continue;
                    yield return new LintIssue(
                        ValueCode, n.Tag, n.Id,
                        $"<{n.Tag} id='{n.Id}'>: rotation=\"{value}\" is not a number. " +
                        "Write plain clockwise degrees, e.g. rotation=\"90\" or rotation=\"-45\".");
                }
            }
        }

        private static IEnumerable<string> Values(
            string baseValue, IReadOnlyList<(string Variant, string Value)> variants)
        {
            yield return baseValue;
            foreach (var (_, value) in variants) yield return value;
        }
    }
}
