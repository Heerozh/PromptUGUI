using System;
using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static class IconResolverHelpers
    {
        public static void UseSpriteAtlasIconResolver(string resourcesSubpath = "IconSets")
        {
            void Rebuild()
            {
                var sets = Resources.LoadAll<IconSet>(resourcesSubpath);
                var map = BuildLookup(sets);
                UI.IconResolver = key => map.TryGetValue(key, out var sp) ? sp : null;
            }
            Rebuild();
#if UNITY_EDITOR
            UI.HotReload.IconResolverRebuilder = Rebuild;
#endif
        }

        public static void UseSpriteAtlasIconResolver(IEnumerable<IconSet> sets)
        {
            var snapshot = new List<IconSet>(sets);
            void Rebuild()
            {
                var map = BuildLookup(snapshot);
                UI.IconResolver = key => map.TryGetValue(key, out var sp) ? sp : null;
            }
            Rebuild();
#if UNITY_EDITOR
            UI.HotReload.IconResolverRebuilder = Rebuild;
#endif
        }

        private static Dictionary<string, Sprite> BuildLookup(IEnumerable<IconSet> sets)
        {
            // Reads IconSet.Entries (filled by the Editor sync tool) instead of
            // iterating the SpriteAtlas directly. The atlas's per-sprite .name can
            // collide when two PNGs in different subfolders share a basename;
            // Entries carry the canonical pathKey + bare alias the syncer chose.
            var map = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var seenSet = new HashSet<string>(StringComparer.Ordinal);
            foreach (var set in sets)
            {
                if (set == null) continue;
                if (string.IsNullOrEmpty(set.SetName))
                {
                    Debug.LogWarning("[PromptUGUI] IconSet with empty setName, skipping");
                    continue;
                }
                if (!seenSet.Add(set.SetName))
                    throw new InvalidOperationException(
                        $"Duplicate IconSet name '{set.SetName}'");

                foreach (var (key, sprite) in set.Entries)
                {
                    if (sprite == null) continue;
                    map[$"{set.SetName}:{key}"] = sprite;
                }
            }
            return map;
        }
    }
}
