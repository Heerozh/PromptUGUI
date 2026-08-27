using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// What a <c>&lt;Decor&gt;</c> has to say for itself (decor spec §7). Four codes, in two
    /// families:
    ///
    /// <list type="bullet">
    /// <item><b>Omissions the runtime can only answer with silence</b> — no <c>kind</c>, or
    /// <c>kind="sprite"</c> with no picture. Both draw nothing at all, which looks exactly like a
    /// decoration that failed to load.</item>
    /// <item><b>Attributes that land nowhere</b> — the node's layout attributes (a decoration's
    /// node always covers its host; placement is <c>at</c> / <c>inset</c> / <c>offset</c>), and
    /// attributes the chosen kind has no use for.</item>
    /// </list>
    ///
    /// <para>Both <c>kind</c> and <c>sprite</c> are checked against the <b>merged</b> attributes,
    /// because a theme pack legitimately supplies either — a document that writes only
    /// <c>class="tab-tick"</c> is complete once the pack is merged in, and reporting it against the
    /// raw text would flag the idiomatic form as broken.</para>
    /// </summary>
    public static class DecorRules
    {
        public const string KindCode = "PUI-DECOR-KIND";
        public const string SpriteCode = "PUI-DECOR-SPRITE";
        public const string LayoutAttrCode = "PUI-DECOR-LAYOUT-ATTR";
        public const string AttrCode = "PUI-DECOR-ATTR";
        public const string ValueCode = "PUI-DECOR-VALUE";

        public const string Tag = "Decor";

        /// <summary>
        /// The two procedural attributes a decoration really does draw. Everything else on
        /// <see cref="ProceduralAttrNames.NeedsPanel"/> (radius, borders, glass, weld) belongs to a
        /// surface, and a decoration's shape comes from its <c>kind</c> — so those stay reported by
        /// <see cref="PureContainerVisualAttrRules"/>, which defers to this set.
        /// </summary>
        internal static readonly HashSet<string> SupportedProceduralAttrs = new()
        {
            "glow", "glowColor",
        };

        /// <summary>
        /// Placement is <c>at</c> / <c>inset</c> / <c>offset</c>; the node itself always covers the
        /// host. <c>size</c> is in here for a second reason as well — it is a common layout
        /// attribute, so it is consumed before a control's own setter ever sees it, and the
        /// decoration's own dimension had to be called <c>extent</c>.
        /// </summary>
        private static readonly string[] LayoutAttrs =
        {
            "anchor", "size", "width", "height", "margin", "pivot", "flow",
        };

        public static bool AppliesTo(string tag) => tag == Tag;

        public static IEnumerable<LintIssue> Check(ElementNode n) => Check(n, StyleAttributeView.Empty);

        public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles)
        {
            styles ??= StyleAttributeView.Empty;
            if (n == null || n.Tag != Tag) yield break;

            foreach (var attr in LayoutAttrs)
            {
                if (!Declares(n, attr)) continue;
                yield return new LintIssue(
                    LayoutAttrCode, n.Tag, n.Id,
                    $"<Decor id='{n.Id}'>: '{attr}' has no effect — a decoration's node always " +
                    "covers its host, and the instances are placed by at / inset / offset. " +
                    (attr == "size"
                        ? "For the decoration's own dimensions write extent= (size= is the common " +
                          "layout attribute and never reaches this tag)."
                        : "Drop it."));
            }

            // A node whose classes the CLI cannot see may be getting kind / sprite from one of them.
            if (styles.IsUncertain(n)) yield break;

            styles.Resolve(n, "kind", out var kindValue, out _);
            if (!Declared(kindValue))
            {
                yield return new LintIssue(
                    KindCode, n.Tag, n.Id,
                    $"<Decor id='{n.Id}'>: no kind= — the decoration draws nothing. Write " +
                    "kind=\"bracket\" / \"tick\" / \"line\" / \"sprite\", or kind=\"none\" if a " +
                    "theme is meant to switch it on later.");
                yield break;
            }

            if (!DecorParser.TryParseKind(kindValue, out var kind, out var kindError))
            {
                yield return new LintIssue(ValueCode, n.Tag, n.Id, Combine(n, kindError));
                yield break;
            }

            styles.Resolve(n, "at", out var atValue, out _);
            if (!DecorParser.TryParseAt(atValue, out var slots, out var atError))
            {
                yield return new LintIssue(ValueCode, n.Tag, n.Id, Combine(n, atError));
                yield break;
            }

            styles.Resolve(n, "extent", out var extentValue, out _);
            if (!DecorParser.TryParseExtent(extentValue, out var extent, out var extentError))
            {
                yield return new LintIssue(ValueCode, n.Tag, n.Id, Combine(n, extentError));
                yield break;
            }

            if (!DecorParser.TryValidate(kind, slots, extent, out var comboError))
            {
                yield return new LintIssue(ValueCode, n.Tag, n.Id, Combine(n, comboError));
                yield break;
            }

            if (kind == DecorKind.None) yield break;

            var isSprite = kind == DecorKind.Sprite;

            styles.Resolve(n, "sprite", out var spriteValue, out _);
            if (isSprite && !Declared(spriteValue))
            {
                yield return new LintIssue(
                    SpriteCode, n.Tag, n.Id,
                    $"<Decor id='{n.Id}'>: kind=\"sprite\" with no sprite= draws nothing. Name the " +
                    "picture (sprite=\"ui:corner-vine\"), or pick a drawn kind — bracket / tick / line.");
            }

            foreach (var issue in CheckApplicability(n, styles, kind, isSprite))
                yield return issue;
        }

        private static IEnumerable<LintIssue> CheckApplicability(
            ElementNode n, StyleAttributeView styles, DecorKind kind, bool isSprite)
        {
            // A stroke width means something to the two kinds drawn as strokes; a tick and a
            // picture are filled shapes.
            if (kind != DecorKind.Bracket && kind != DecorKind.Line && Declares(n, styles, "thickness"))
                yield return Inapplicable(n, "thickness", kind,
                    "it is the width of a stroke, and this kind is a filled shape");

            // Sliding along an edge needs an edge; a corner has none.
            if (kind == DecorKind.Bracket && Declares(n, styles, "offset"))
                yield return Inapplicable(n, "offset", kind,
                    "it slides a decoration along its edge, and a bracket sits on a corner " +
                    "(use inset= to move it diagonally)");

            if (isSprite)
            {
                foreach (var attr in SupportedProceduralAttrs)
                {
                    if (!Declares(n, styles, attr)) continue;
                    yield return Inapplicable(n, attr, kind,
                        "the glow is cast from a distance field, which a picture does not have " +
                        "(draw the glow into the artwork instead)");
                }
                yield break;
            }

            foreach (var attr in new[] { "sprite", "mirror" })
            {
                if (!Declares(n, styles, attr)) continue;
                yield return Inapplicable(n, attr, kind,
                    "it only means something to kind=\"sprite\"");
            }
        }

        private static LintIssue Inapplicable(ElementNode n, string attr, DecorKind kind, string why)
            => new LintIssue(
                AttrCode, n.Tag, n.Id,
                $"<Decor id='{n.Id}'>: '{attr}' does nothing on kind=\"{KindName(kind)}\" — {why}.");

        /// <summary>
        /// Adds the node's id to a parser message. The parser already names the tag (its messages
        /// also surface at runtime, where nothing else does), so that half is dropped here rather
        /// than printed twice.
        /// </summary>
        private static string Combine(ElementNode n, string error)
        {
            const string tagPrefix = "<Decor> ";
            if (error != null && error.StartsWith(tagPrefix, System.StringComparison.Ordinal))
                error = error.Substring(tagPrefix.Length);
            return $"<Decor id='{n.Id}'>: {error}";
        }

        private static string KindName(DecorKind kind)
        {
            switch (kind)
            {
                case DecorKind.Bracket: return DecorParser.BracketKeyword;
                case DecorKind.Tick: return DecorParser.TickKeyword;
                case DecorKind.Line: return DecorParser.LineKeyword;
                case DecorKind.Sprite: return DecorParser.SpriteKeyword;
                default: return DecorParser.NoneKeyword;
            }
        }

        /// <summary>Empty is not a declaration: it is a Variant's only way back to the default.</summary>
        private static bool Declared(string value) => !string.IsNullOrWhiteSpace(value);

        private static bool Declares(ElementNode n, string attr)
            => n.Attributes.ContainsKey(attr) || n.VariantOverrides.ContainsKey(attr);

        private static bool Declares(ElementNode n, StyleAttributeView styles, string attr)
        {
            styles.Resolve(n, attr, out var value, out var variants);
            return Declared(value) || (variants != null && variants.Count > 0);
        }
    }
}
