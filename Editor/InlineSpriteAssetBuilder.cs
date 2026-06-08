using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>
    /// Editor-only: bakes the sprites of every SpriteSet flagged
    /// <c>generateTmpSpriteAsset</c> into a single global <see cref="TMPro.TMP_SpriteAsset"/>
    /// so authors can use native TMP <c>&lt;sprite name="..."&gt;</c> inline markup.
    /// The pure <see cref="BuildInlineGlyphTable"/> is unit-tested; the asset I/O lives in
    /// <see cref="Generate"/> / <see cref="RegenerateFromProject"/>.
    /// </summary>
    public static partial class InlineSpriteAssetBuilder
    {
        public struct Glyph
        {
            public string name;
            public Sprite sprite;
        }

        /// <summary>Merge flat (set, name, sprite) candidates into a unique-by-name glyph
        /// list. A name present in more than one set is a hard collision: it is added to
        /// <paramref name="collisions"/> and the merge returns an EMPTY list (caller aborts,
        /// matching the atlas syncer's no-silent-overwrite contract).</summary>
        public static List<Glyph> BuildInlineGlyphTable(
            IReadOnlyList<(string set, string name, Sprite sprite)> candidates,
            out List<string> collisions)
        {
            collisions = new List<string>();
            var owner = new Dictionary<string, string>(System.StringComparer.Ordinal);   // name -> setName
            var ordered = new List<Glyph>();
            var seen = new HashSet<string>(System.StringComparer.Ordinal);

            foreach (var (set, name, sprite) in candidates)
            {
                if (string.IsNullOrEmpty(name) || sprite == null) continue;
                if (owner.TryGetValue(name, out var prevSet))
                {
                    if (prevSet != set && !collisions.Contains(name)) collisions.Add(name);
                    continue;
                }
                owner[name] = set;
                if (seen.Add(name)) ordered.Add(new Glyph { name = name, sprite = sprite });
            }

            if (collisions.Count > 0)
            {
                Debug.LogError(
                    "[InlineSprite] glyph name collision across flagged SpriteSets: " +
                    string.Join(", ", collisions) + ". Rename so every inline glyph is unique.");
                return new List<Glyph>();
            }
            return ordered;
        }
    }
}
