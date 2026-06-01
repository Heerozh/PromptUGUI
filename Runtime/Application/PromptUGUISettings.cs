using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Application
{
    [CreateAssetMenu(menuName = "PromptUGUI/Settings", fileName = "PromptUGUI_Settings")]
    public sealed class PromptUGUISettings : ScriptableObject
    {
        [Serializable]
        public sealed class FontEntry
        {
            public string type;          // "default" | "title" | "damage" | ...
            public TMP_FontAsset font;   // leave empty to inherit this locale's "default" font
            public Material material;    // optional TMP material preset (e.g. outline); null = font's default
        }
        [Serializable]
        public sealed class LocaleConfig
        {
            [LocalePresets] public string locale;        // BCP-47 e.g. "zh-Hans" / "en"
            public List<FontEntry> fonts = new();
        }
        [Tooltip("Logical font roles used across locales (e.g. 'default', 'title', 'damage'). " +
                 "Each locale gets exactly one font slot per role; slots are auto-synced from this list.")]
        public List<string> fontTypes = new() { "default" };
        public List<LocaleConfig> locales = new();

        /// <summary>Resolved font + optional material preset for a (locale, type) pair.</summary>
        internal readonly struct FontResolution
        {
            public readonly TMP_FontAsset Font;
            public readonly Material Material;
            public FontResolution(TMP_FontAsset font, Material material)
            {
                Font = font;
                Material = material;
            }
        }

        public TMP_FontAsset ResolveFont(string locale, string type) =>
            ResolveFontEntry(locale, type).Font;

        /// <summary>
        /// Resolves the font and material preset for a logical type within a locale.
        /// A matched entry with an empty font slot inherits the locale's "default"
        /// font; an unknown type falls back to the "default" entry entirely.
        /// </summary>
        internal FontResolution ResolveFontEntry(string locale, string type)
        {
            if (string.IsNullOrEmpty(locale)) return default;
            foreach (var lc in locales)
            {
                if (lc.locale != locale) continue;
                FontEntry match = null;
                FontEntry fallback = null;
                foreach (var fe in lc.fonts)
                {
                    if (fe.type == type) match = fe;
                    if (fe.type == "default") fallback = fe;
                }
                var entry = match ?? fallback;
                if (entry == null) return default;
                var font = entry.font != null ? entry.font : fallback?.font;
                return new FontResolution(font, entry.material);
            }
            return default;
        }

        // Returns first loaded instance via preloadedAssets, null if none.
        public static PromptUGUISettings Instance
        {
            get
            {
                var loaded = Resources.FindObjectsOfTypeAll<PromptUGUISettings>();
                return loaded.Length > 0 ? loaded[0] : null;
            }
        }

        private void OnValidate()
        {
            if (locales != null)
            {
                var seenLocale = new Dictionary<string, int>();
                for (var i = 0; i < locales.Count; i++)
                {
                    var lc = locales[i];
                    if (lc == null || string.IsNullOrEmpty(lc.locale)) continue;
                    if (seenLocale.TryGetValue(lc.locale, out var first))
                    {
                        Debug.LogError(
                            $"[PromptUGUI] Duplicate locale '{lc.locale}' at index {i} " +
                            $"(first defined at index {first}); only the first will be used at runtime.",
                            this);
                    }
                    else
                    {
                        seenLocale[lc.locale] = i;
                    }
                }
            }

            fontTypes ??= new List<string>();
            var canonical = new List<string>();
            var seenType = new HashSet<string>();
            for (var i = 0; i < fontTypes.Count; i++)
            {
                var t = fontTypes[i];
                if (string.IsNullOrEmpty(t)) continue;
                if (!seenType.Add(t))
                {
                    Debug.LogError(
                        $"[PromptUGUI] Duplicate font type '{t}' at fontTypes[{i}]; ignored.",
                        this);
                    continue;
                }
                canonical.Add(t);
            }

            if (locales != null)
            {
                foreach (var lc in locales)
                {
                    if (lc == null) continue;
                    lc.fonts ??= new List<FontEntry>();
                    var byType = new Dictionary<string, FontEntry>();
                    foreach (var fe in lc.fonts)
                    {
                        if (fe == null || string.IsNullOrEmpty(fe.type)) continue;
                        byType[fe.type] = fe;
                    }
                    lc.fonts.Clear();
                    foreach (var t in canonical)
                    {
                        byType.TryGetValue(t, out var prev);
                        lc.fonts.Add(new FontEntry { type = t, font = prev?.font, material = prev?.material });
                    }
                }
            }
        }
    }
}
