using System;
using System.Collections.Generic;
using PromptUGUI.IR;
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

        [Tooltip("Project-relative folders holding .po files produced by external tools " +
                 "(e.g. a game server exporting runtime-provided strings). Extraction never " +
                 "writes to or reports on them; labelling and translation still include them. " +
                 "Files must still sit under a '<locale>' folder: <root>/<locale>/*.po")]
        public List<string> externalPoRoots = new();

        [Tooltip("Common libraries: the <Import> every document implicitly has (shared <Template> / " +
                 "<Style> / <Theme>). 'src' is a resolver key in the same shape as <Import src> — " +
                 "e.g. 'UI/Templates/Theme.ui' for UseResourcesResolver(\"UI\"), the Address for " +
                 "Addressables — never a file path. 'as' is an optional namespace (<ns.Name/>, " +
                 "class=\"ns:name\"). Loaded in this order by UI.EnsureCommonLibrariesAsync, which " +
                 "LoadDocumentAsync calls for you; the lint tools read the same list.")]
        public List<CommonLibraryEntry> commonLibraries = new();

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

        private static PromptUGUISettings s_instance;

        // 测试缝：替换掉 Resources.FindObjectsOfTypeAll（null = 用真的）。
        internal static Func<PromptUGUISettings[]> FinderForTests;

        /// <summary>
        /// 首个已加载的实例（随 preloadedAssets 进内存），没有则 null。
        /// <para><b>缓存过的。</b><c>Resources.FindObjectsOfTypeAll</c> 是全内存对象扫描——宿主的星图加载后
        /// 每次 ~0.4 ms，而每个 <c>&lt;Text&gt;</c> 的字体应用（<c>FontApplier</c>）都要拿一次 settings：
        /// 一张 300 控件的面板 243 次，占打开耗时的 28%。缓存的实例被销毁 / 卸载后是 Unity 假 null，
        /// 下次访问自动重扫；没找到不做负缓存（资产晚点才加载的场合下次还能找到）；编辑器里 settings
        /// 资产被导入 / 删除 / 移动时 <c>PromptUGUISettingsAutoMaintainer</c> 调 <see cref="ResetInstanceCache"/>，
        /// <c>UI.ResetForTests</c> 也清。</para>
        /// </summary>
        public static PromptUGUISettings Instance
        {
            get
            {
                if (s_instance != null) return s_instance;
                var loaded = FinderForTests != null
                    ? FinderForTests()
                    : Resources.FindObjectsOfTypeAll<PromptUGUISettings>();
                s_instance = loaded != null && loaded.Length > 0 ? loaded[0] : null;
#if UNITY_EDITOR
                // The Player has the asset in memory from preloadedAssets; the Editor only once
                // something loaded it (PromptUGUISettingsAutoMaintainer does, on a delayCall after
                // each domain reload). A BeforeSceneLoad caller — the host's boot, or
                // UI.EnsureCommonLibrariesAsync from it — can run before that, so ask the
                // AssetDatabase directly rather than answer "no settings" for one frame.
                if (s_instance == null && FinderForTests == null) s_instance = FindInAssetDatabase();
#endif
                return s_instance;
            }
        }

#if UNITY_EDITOR
        private static PromptUGUISettings FindInAssetDatabase()
        {
            var guids = UnityEditor.AssetDatabase.FindAssets("t:PromptUGUISettings");
            if (guids.Length == 0) return null;   // more than one: the maintainer already logs it
            return UnityEditor.AssetDatabase.LoadAssetAtPath<PromptUGUISettings>(
                UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
        }
#endif

        /// <summary>丢掉 <see cref="Instance"/> 的缓存，下次访问重扫。</summary>
        internal static void ResetInstanceCache() => s_instance = null;

        /// <summary>
        /// The two mistakes a row can carry, reported where they are made. A duplicate src loads
        /// once (the second row is skipped as already loaded); a dotted namespace is what the
        /// parser rejects on <c>&lt;Import as&gt;</c>, and would throw at the first load. Blank
        /// rows are fine — inert everywhere.
        /// </summary>
        private void ValidateCommonLibraries()
        {
            commonLibraries ??= new List<CommonLibraryEntry>();
            var seen = new HashSet<string>();
            for (var i = 0; i < commonLibraries.Count; i++)
            {
                var row = commonLibraries[i];
                if (row == null || row.IsBlank) continue;
                var src = row.src.Trim();
                if (!seen.Add(src))
                    Debug.LogError(
                        $"[PromptUGUI] Duplicate common library src '{src}' at commonLibraries[{i}]; " +
                        "only the first row is loaded.", this);
                if (!string.IsNullOrWhiteSpace(row.@as) && row.@as.Contains('.'))
                    Debug.LogError(
                        $"[PromptUGUI] commonLibraries[{i}]: as='{row.@as}' must not contain '.' " +
                        "(the same rule as <Import as>; templates are invoked as <ns.Name/>).", this);
            }
        }

        private void OnValidate()
        {
            ValidateCommonLibraries();

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
