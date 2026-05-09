using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace PromptUGUI.Application {
    [CreateAssetMenu(menuName = "PromptUGUI/Settings", fileName = "PromptUGUI_Settings")]
    public sealed class PromptUGUISettings : ScriptableObject {
        [Serializable] public sealed class FontEntry {
            public string type;          // "default" | "title" | "damage" | ...
            public TMP_FontAsset font;
        }
        [Serializable] public sealed class LocaleConfig {
            public string locale;        // BCP-47 e.g. "zh-Hans" / "en"
            public List<FontEntry> fonts = new();
        }
        public List<LocaleConfig> locales = new();

        public TMP_FontAsset ResolveFont(string locale, string type) {
            if (string.IsNullOrEmpty(locale)) return null;
            foreach (var lc in locales) {
                if (lc.locale != locale) continue;
                FontEntry fallback = null;
                foreach (var fe in lc.fonts) {
                    if (fe.type == type) return fe.font;
                    if (fe.type == "default") fallback = fe;
                }
                return fallback?.font;
            }
            return null;
        }

        // Returns first loaded instance via preloadedAssets, null if none.
        public static PromptUGUISettings Instance {
            get {
                var loaded = Resources.FindObjectsOfTypeAll<PromptUGUISettings>();
                return loaded.Length > 0 ? loaded[0] : null;
            }
        }
    }
}
