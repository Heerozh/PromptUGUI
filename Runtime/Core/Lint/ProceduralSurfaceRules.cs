using System.Collections.Generic;
using PromptUGUI.IR;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Controls that can draw their primary surface procedurally instead of with an <c>Image</c>,
    /// and what may not be said about that surface at the same time (procedural-surface spec §7/§8).
    ///
    /// <para>Both codes catch a <b>contradiction</b>, not an omission: the author asked for two
    /// mutually exclusive things and the runtime silently picks one. The alternative to reporting is
    /// an author staring at a button wondering why the sprite they set has no effect.</para>
    /// </summary>
    public static class ProceduralSurfaceRules
    {
        public const string SpriteConflictCode = "PUI-PROC-SPRITE-CONFLICT";
        public const string StateSpriteConflictCode = "PUI-PROC-STATE-SPRITE-CONFLICT";

        /// <summary>
        /// The controls wired to <c>ProceduralControl</c>. <c>&lt;Frame&gt;</c> is deliberately absent:
        /// it draws procedurally too, but it has no <c>Image</c> at all, so its <c>sprite=</c> is
        /// already reported by <see cref="PureContainerVisualAttrRules"/> as simply ignored — a
        /// different diagnosis with a different fix.
        ///
        /// <para>A hand-kept mirror of the Unity-side registry, because <c>Core/Lint</c> is the
        /// pure-C# subset the CLI compiles outside Unity. <c>ProceduralAttrNamesTests</c> checks it
        /// against the live registry in both directions, so a control added here without being wired
        /// up — or wired up without being added here — fails the build rather than quietly changing
        /// what the linter says.</para>
        /// </summary>
        internal static readonly HashSet<string> SurfaceTags = new()
        {
            "Btn", "Tab", "Toggle", "Slider", "Dropdown", "InputField", "ScrollList", "Progress",
        };

        // Image.overrideSprite swaps. On an SDF face there is no sprite to override.
        private static readonly string[] StateSprites =
        {
            "pressedSprite", "disabledSprite", "selectedSprite",
        };

        public static bool AppliesTo(string tag) => SurfaceTags.Contains(tag);

        public static IEnumerable<LintIssue> Check(ElementNode n) => Check(n, StyleAttributeView.Empty);

        public static IEnumerable<LintIssue> Check(ElementNode n, StyleAttributeView styles)
        {
            styles ??= StyleAttributeView.Empty;
            if (n == null || !SurfaceTags.Contains(n.Tag)) yield break;
            if (styles.IsUncertain(n)) yield break;
            if (!DeclaresProcedural(n, styles)) yield break;

            styles.Resolve(n, "sprite", out var sprite, out _);
            if (IsRealSprite(sprite))
            {
                yield return new LintIssue(
                    SpriteConflictCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: sprite=\"{sprite}\" and a procedural surface are two " +
                    "different ways to draw the same layer, and the procedural one wins. A bitmap " +
                    "under an SDF face is a mess, and Image.type's sliced/tiled inference means " +
                    "nothing there. Drop one — sprite=\"none\" is the spelling for 'no bitmap'.");
            }

            foreach (var attr in StateSprites)
            {
                styles.Resolve(n, attr, out var value, out _);
                if (!IsRealSprite(value)) continue;
                yield return new LintIssue(
                    StateSpriteConflictCode, n.Tag, n.Id,
                    $"<{n.Tag} id='{n.Id}'>: {attr}=\"{value}\" swaps Image.overrideSprite, which a " +
                    "procedural surface does not have. Use the matching colour attribute " +
                    $"({attr.Replace("Sprite", "Color")}) or <Show on=\"state-*\"> instead.");
            }
        }

        /// <summary>
        /// Whether the node goes procedural in its BASE state — a variant-only entry
        /// (<c>radius.glass="10"</c> with no plain <c>radius</c>) deliberately does not count.
        ///
        /// <para>Because that is precisely how a skin turns the shape on for one variant and leaves
        /// the sprite alone for the others; the shipped CommonControls sample is built that way. The
        /// two never apply at once, so reporting them as a contradiction would flag the idiomatic
        /// form as broken. The cost is that a genuine per-variant clash goes unreported — the
        /// direction this repo has consistently chosen, since a false positive turns the CLI's
        /// non-zero exit into a wall for correct XML.</para>
        /// </summary>
        private static bool DeclaresProcedural(ElementNode n, StyleAttributeView styles)
        {
            foreach (var attr in ProceduralAttrNames.NeedsPanel)
            {
                if (attr == "weld") continue;   // §13.2 — weld does not cross into controls
                styles.Resolve(n, attr, out var baseValue, out _);
                if (baseValue != null) return true;
            }
            return false;
        }

        /// <summary>
        /// <c>""</c> and <c>"none"</c> mean "clear the bitmap", which agrees with going procedural
        /// rather than contradicting it — and skin packs are full of that spelling, so treating it as
        /// a conflict would report the idiomatic form as broken.
        /// </summary>
        private static bool IsRealSprite(string value)
            => !string.IsNullOrWhiteSpace(value) && value != "none";
    }
}
