using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;

namespace PromptUGUI.Application
{
    public static class IconResolverHelpers
    {
        private const string CloneSuffix = "(Clone)";

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
                if (set.Atlas == null) continue;

                var sprites = new Sprite[set.Atlas.spriteCount];
                set.Atlas.GetSprites(sprites);
                foreach (var s in sprites)
                {
                    if (s == null) continue;
                    var name = s.name;
                    if (name.EndsWith(CloneSuffix, StringComparison.Ordinal))
                        name = name.Substring(0, name.Length - CloneSuffix.Length);
                    map[$"{set.SetName}:{name}"] = s;
                }
            }
            return map;
        }
    }
}
