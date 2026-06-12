using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class SpriteResolverHelpers
    {
        public static void UseSpriteSetResolver(string resourcesSubpath = "SpriteSets")
        {
            void Rebuild()
            {
                var sets = Resources.LoadAll<SpriteSet>(resourcesSubpath);
                var map = BuildLookup(sets);
                UI.SpriteResolver = key => map.TryGetValue(key, out var sp) ? sp : null;
            }
            Rebuild();
#if UNITY_EDITOR
            UI.HotReload.SpriteResolverRebuilder = Rebuild;
#endif
        }

        public static void UseSpriteSetResolver(IEnumerable<SpriteSet> sets)
        {
            var snapshot = new List<SpriteSet>(sets);
            void Rebuild()
            {
                var map = BuildLookup(snapshot);
                UI.SpriteResolver = key => map.TryGetValue(key, out var sp) ? sp : null;
            }
            Rebuild();
#if UNITY_EDITOR
            UI.HotReload.SpriteResolverRebuilder = Rebuild;
#endif
        }

        private static Dictionary<string, Sprite> BuildLookup(IEnumerable<SpriteSet> sets)
        {
            // Reads SpriteSet.EntriesWithMeta (filled by the Editor sync tool) instead of
            // iterating the SpriteAtlas directly. The atlas's per-sprite .name can
            // collide when two PNGs in different subfolders share a basename;
            // entries carry the canonical pathKey + bare alias the syncer chose, plus
            // the tiled hint to register into SpriteRenderHints.
            var map = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var seenSet = new HashSet<string>(StringComparer.Ordinal);
            UI.LoadedSpriteSetNames.Clear();
            foreach (var set in sets)
            {
                if (set == null) continue;
                if (string.IsNullOrEmpty(set.SetName))
                {
                    Debug.LogWarning("[PromptUGUI] SpriteSet with empty setName, skipping");
                    continue;
                }
                if (!seenSet.Add(set.SetName))
                    throw new InvalidOperationException(
                        $"Duplicate SpriteSet name '{set.SetName}'");
                UI.LoadedSpriteSetNames.Add(set.SetName);

                foreach (var (key, sprite, tiled) in set.EntriesWithMeta)
                {
                    if (sprite == null) continue;
                    if (tiled) Internal.SpriteRenderHints.Register(sprite);
                    map[$"{set.SetName}:{key}"] = sprite;
                }
            }
            return map;
        }
    }
}
