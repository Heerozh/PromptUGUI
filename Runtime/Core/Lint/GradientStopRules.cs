using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// A gradient stop position (<c>color="A 70%,B"</c>, spec 2026-08-30) on a colour that lands on
    /// TMP text, which is the one place left that cannot draw one.
    ///
    /// <para>A stop needs somewhere to change over: the procedural shader has every fragment, and
    /// <c>GradientTint</c> cuts the mesh at the stop so every other Graphic has a vertex row there
    /// (spec 2026-09-01 VGS). TMP has neither — its gradient is four corner colours per glyph, and a
    /// glyph is wherever the line-breaker put it — so the position silently vanishes and the ramp
    /// spans the full height. Nothing throws, nothing looks broken, which makes the CLI the place to
    /// say so.</para>
    ///
    /// <para><b>Bias.</b> A node whose <c>class=</c> this document cannot resolve stays quiet: the
    /// unseen commons may not even set the attribute. That is the opposite of
    /// <see cref="ProceduralSurfaceRules"/>, deliberately — there a missing declaration means
    /// silence, here it would mean a hard CLI error, so the permissive answer is the safe one in
    /// each case.</para>
    /// </summary>
    public static class GradientStopRules
    {
        public const string NoSurfaceCode = "PUI-GRADIENT-STOP-NO-SURFACE";

        /// <summary>Tags whose <c>color</c> is TMP text outright.</summary>
        private static readonly Dictionary<string, string> AlwaysVertexTags = new()
        {
            ["Text"] = "TMP paints a <Text> gradient per character, and four glyph corners have nowhere to put a stop",
        };

        /// <summary>
        /// Colour attributes on a control that reach a TMP label inside it. Every other colour a
        /// control exposes — its fill, a checkmark, an arrow, a popup, a scrollbar — is a Graphic,
        /// and those draw stops now whether or not they have a procedural surface.
        /// </summary>
        private static readonly Dictionary<string, string[]> TextAttrs = new()
        {
            ["Btn"] = new[] { "textColor" },
            ["Tab"] = new[] { "textColor" },
            ["TabMenu"] = new[] { "textColor" },
            ["Collapsible"] = new[] { "textColor" },
            ["Toggle"] = new[] { "textColor" },
            ["InputField"] = new[] { "textColor" },
            ["Dropdown"] = new[] { "textColor", "itemTextColor" },
        };

        public static IEnumerable<LintIssue> Check(ElementNode n) => Check(n, StyleAttributeView.Empty);

        public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles)
        {
            styles ??= StyleAttributeView.Empty;
            if (n == null) yield break;
            // A class this document cannot resolve may not set the attribute at all; nothing here is
            // provable, so say nothing.
            if (styles.IsUncertain(n)) yield break;

            if (AlwaysVertexTags.TryGetValue(n.Tag, out var why) && HasStop(n, styles, "color"))
                yield return Issue(n, "color", $"{why}. Drop the position, or put the shaped ramp on " +
                                              "a graphic behind the text.");

            if (TextAttrs.TryGetValue(n.Tag, out var textAttrs))
            {
                foreach (var attr in textAttrs)
                {
                    if (!HasStop(n, styles, attr)) continue;
                    yield return Issue(n, attr,
                        $"'{attr}' paints a TMP label, where the gradient is placed per glyph and a " +
                        "stop has nowhere to live. Drop the position, or put the shaped ramp on a " +
                        "graphic behind the text.");
                }
            }
        }

        private static LintIssue Issue(ElementNode n, string attr, string advice)
            => new LintIssue(NoSurfaceCode, n.Tag, n.Id,
                $"<{n.Tag} id='{n.Id}'>: '{attr}' carries a gradient stop position, which TMP text " +
                $"cannot draw — {advice}");

        /// <summary>
        /// The attribute — base value or any variant override, inline or from a class — names a stop
        /// position. A value that will not parse at all is skipped: that is
        /// <c>PUI-COLOR-GRADIENT-MALFORMED</c>'s to report, and an unparseable stop has no position
        /// to be wrong about.
        /// </summary>
        private static bool HasStop(ElementNode n, StyleAttributeView styles, string attr)
        {
            styles.Resolve(n, attr, out var baseValue, out var variants);
            if (Declares(baseValue)) return true;
            if (variants != null)
                for (var i = 0; i < variants.Count; i++)
                    if (Declares(variants[i].Value)) return true;
            return false;
        }

        private static bool Declares(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            // Still a placeholder at this point in expansion — resolve it and it may well be fine.
            if (value.Contains("{{")) return false;
            if (value.IndexOf('%') < 0) return false;
            if (!ColorParser.TrySplitGradient(value, out var parts, out _)) return false;
            return parts.TopStop.HasValue || parts.BottomStop.HasValue || parts.Hint.HasValue;
        }
    }
}
