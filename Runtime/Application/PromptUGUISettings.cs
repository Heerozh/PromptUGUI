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
            public TMP_FontAsset font;
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

        public TMP_FontAsset ResolveFont(string locale, string type)
        {
            if (string.IsNullOrEmpty(locale)) return null;
            foreach (var lc in locales)
            {
                if (lc.locale != locale) continue;
                FontEntry fallback = null;
                foreach (var fe in lc.fonts)
                {
                    if (fe.type == type) return fe.font;
                    if (fe.type == "default") fallback = fe;
                }
                return fallback?.font;
            }
            return null;
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
                    var byType = new Dictionary<string, TMP_FontAsset>();
                    foreach (var fe in lc.fonts)
                    {
                        if (fe == null || string.IsNullOrEmpty(fe.type)) continue;
                        byType[fe.type] = fe.font;
                    }
                    lc.fonts.Clear();
                    foreach (var t in canonical)
                    {
                        byType.TryGetValue(t, out var f);
                        lc.fonts.Add(new FontEntry { type = t, font = f });
                    }
                }
            }
        }
    }
}
