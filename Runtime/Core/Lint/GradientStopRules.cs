using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// A gradient stop position (<c>color="A 70%,B"</c>, spec 2026-08-30) on a colour that will be
    /// painted by vertex colours rather than the procedural shader.
    ///
    /// <para>A stop only exists per fragment. The vertex path has nothing but the graphic's corner
    /// vertices to hang it on, so evaluating the ramp there yields the two end colours and the
    /// hardware interpolates straight between them — the author's position vanishes and the
    /// gradient comes out spanning the full height. Nothing throws, nothing looks broken; it just
    /// silently ignores what they wrote, which makes the CLI the place to say so.</para>
    ///
    /// <para><b>Bias.</b> Every gate below asks "is the surface declared ANYWHERE" — inline, through
    /// a class, or in a variant — and stays quiet if so. That is the opposite of
    /// <see cref="ProceduralSurfaceRules"/>, deliberately: there a missing declaration means silence,
    /// here it means a hard CLI error, so the permissive answer is the safe one in each case. The
    /// cost is that a stop that only works in one variant goes unreported.</para>
    /// </summary>
    public static class GradientStopRules
    {
        public const string NoSurfaceCode = "PUI-GRADIENT-STOP-NO-SURFACE";

        /// <summary>Tags with no procedural surface at all — their <c>color</c> is always vertex work.</summary>
        private static readonly Dictionary<string, string> AlwaysVertexTags = new()
        {
            ["Image"] = "an <Image> paints a sprite through vertex colours, which has no place for a stop",
            ["Icon"] = "an <Icon> paints a sprite through vertex colours, which has no place for a stop",
            ["RawImage"] = "a <RawImage> paints a texture through vertex colours, which has no place for a stop",
            ["Text"] = "TMP paints a <Text> gradient per character, and four glyph corners have nowhere to put a stop",
        };

        /// <summary>
        /// Colour attributes that reach the control's PRIMARY surface — the one
        /// <c>radius</c> / <c>glass</c> / <c>border*</c> / <c>glow*</c> turn on. The absolute state
        /// colours ride the same layer, so they are gated the same way.
        /// </summary>
        private static readonly Dictionary<string, string[]> MainSurfaceAttrs = new()
        {
            ["Btn"] = new[] { "color", "hoverColor", "pressedColor", "selectedColor", "disabledColor" },
            ["Tab"] = new[] { "color", "hoverColor", "pressedColor", "selectedColor", "disabledColor" },
            ["Toggle"] = new[] { "color", "hoverColor", "pressedColor", "selectedColor", "disabledColor" },
            ["TabMenu"] = new[] { "color" },
            ["Slider"] = new[] { "color" },
            ["Dropdown"] = new[] { "color" },
            ["InputField"] = new[] { "color" },
            ["ScrollList"] = new[] { "color" },
            ["Progress"] = new[] { "bgColor" },
        };

        /// <summary>
        /// Colour attributes painting a layer INSIDE a control, each with the one
        /// <c>&lt;layer&gt;Radius</c> that gives that layer a surface of its own (spec 2026-08-23 §6).
        /// The control's own <c>radius</c> shapes a different layer and does not count.
        /// </summary>
        private static readonly Dictionary<string, (string Attr, string Gate)[]> InnerSurfaceAttrs = new()
        {
            ["Slider"] = new[] { ("fillColor", "fillRadius"), ("handleColor", "handleRadius") },
            ["Progress"] = new[] { ("fillColor", "fillRadius"), ("frameColor", "frameRadius") },
        };

        /// <summary>
        /// Colour attributes on a surface-capable control that STILL have no procedural layer under
        /// any spelling — a checkmark, an arrow, a popup background, a scrollbar, a label. There is
        /// no attribute that would make these work, so the only fix is dropping the position.
        /// </summary>
        private static readonly Dictionary<string, string[]> NeverSurfaceAttrs = new()
        {
            ["Btn"] = new[] { "textColor" },
            ["Tab"] = new[] { "textColor" },
            ["TabMenu"] = new[] { "textColor", "arrowColor" },
            ["Toggle"] = new[] { "textColor", "checkmarkColor" },
            ["InputField"] = new[] { "textColor" },
            ["ScrollList"] = new[] { "frameColor", "scrollbarColor", "scrollbarHandleColor" },
            ["Dropdown"] = new[]
            {
                "textColor", "itemTextColor", "popupColor", "itemColor", "arrowColor",
                "checkmarkColor", "scrollbarColor", "scrollbarHandleColor",
            },
        };

        public static IEnumerable<LintIssue> Check(ElementNode n) => Check(n, StyleAttributeView.Empty);

        public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles)
        {
            styles ??= StyleAttributeView.Empty;
            if (n == null) yield break;
            // A class this document cannot resolve may carry the very shape attribute that would
            // make the stop work; nothing here is provable, so say nothing.
            if (styles.IsUncertain(n)) yield break;

            if (AlwaysVertexTags.TryGetValue(n.Tag, out var why) && HasStop(n, styles, "color"))
                yield return Issue(n, "color", $"{why}. Drop the position, or draw the shape with a " +
                                              "procedural <Frame> behind it.");

            if (MainSurfaceAttrs.TryGetValue(n.Tag, out var mainAttrs) && !DeclaresSurface(n, styles))
            {
                foreach (var attr in mainAttrs)
                {
                    if (!HasStop(n, styles, attr)) continue;
                    yield return Issue(n, attr,
                        "this control is still drawing its Image, so the fill goes through vertex " +
                        "colours. Give it a procedural shape (radius / glass / borderWidth / glow) " +
                        "and the stop works, or drop the position.");
                }
            }

            if (InnerSurfaceAttrs.TryGetValue(n.Tag, out var innerAttrs))
            {
                foreach (var (attr, gate) in innerAttrs)
                {
                    if (styles.Declares(n, gate)) continue;
                    if (!HasStop(n, styles, attr)) continue;
                    yield return Issue(n, attr,
                        $"'{gate}' is what gives this layer a procedural surface of its own — " +
                        $"without it '{attr}' is a vertex tint. Add '{gate}', or drop the position.");
                }
            }

            if (NeverSurfaceAttrs.TryGetValue(n.Tag, out var neverAttrs))
            {
                foreach (var attr in neverAttrs)
                {
                    if (!HasStop(n, styles, attr)) continue;
                    yield return Issue(n, attr,
                        $"'{attr}' paints a plain Graphic that has no procedural surface under any " +
                        "spelling. Drop the position — the gradient still runs top to bottom.");
                }
            }
        }

        private static LintIssue Issue(ElementNode n, string attr, string advice)
            => new LintIssue(NoSurfaceCode, n.Tag, n.Id,
                $"<{n.Tag} id='{n.Id}'>: '{attr}' carries a gradient stop position, which only a " +
                $"procedural surface can draw — {advice}");

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

        /// <summary>
        /// The permissive twin of <c>ProceduralSurfaceRules.DeclaresProcedural</c>: a variant-only
        /// <c>radius.mobile</c> counts here, because the stop genuinely works in that variant and a
        /// hard CLI error over it would be a false positive. <c>weld</c> is excluded for the same
        /// reason it is there — it builds a group panel on a child, not on this node.
        /// </summary>
        private static bool DeclaresSurface(ElementNode n, StyleAttributeView styles)
        {
            foreach (var attr in ProceduralAttrNames.NeedsPanel)
            {
                if (attr == "weld") continue;
                if (styles.Declares(n, attr)) return true;
            }
            return false;
        }
    }
}
