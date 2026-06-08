using System.Collections.Generic;
using PromptUGUI.Application;
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

        /// <summary>Flatten flagged sets into (set, bareName, sprite) candidates. Only
        /// unambiguous bare basenames become inline glyph names — path-only keys (those
        /// containing '/') and bare names that collide <i>within</i> a set are dropped by the
        /// reused BuildLookup promotion rule.</summary>
        public static List<(string set, string name, Sprite sprite)> CollectCandidates(
            IReadOnlyList<SpriteSet> flaggedSets)
        {
            var result = new List<(string, string, Sprite)>();
            foreach (var set in flaggedSets)
            {
                if (set == null || string.IsNullOrEmpty(set.SetName)) continue;
                var entries = SpriteAtlasSyncer.EnumerateSpriteSources(set.SourceFolderPath);
                var lookup = SpriteAtlasSyncer.BuildLookup(entries, out _);
                foreach (var kv in lookup)
                {
                    if (kv.Key.IndexOf('/') >= 0) continue;    // path-only → not inline-addressable
                    result.Add((set.SetName, kv.Key, kv.Value));
                }
            }
            return result;
        }
    }
}
