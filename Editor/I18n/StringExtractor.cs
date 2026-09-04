using System.Collections.Generic;
using System.IO;
using System.Linq;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using UnityEditor;
using UnityEngine;
#if PROMPTUGUI_HAS_ADDRESSABLES
using UnityEditor.AddressableAssets;
#endif

namespace PromptUGUI.Editor.I18n
{
    internal static class StringExtractor
    {
        /// <summary>
        /// Where extraction writes when nothing tells it otherwise. Shared with
        /// <c>TranslateLocaleWindow</c> so "where the window looks" can never drift
        /// from "where Extract writes".
        /// </summary>
        internal const string DefaultOutputRoot = "Assets/Resources/PromptUGUI/i18n";

        [MenuItem("Tools/PromptUGUI/I18n/1. Extract Strings")]
        public static void ExtractAll()
        {
            var settings = PromptUGUISettings.Instance;
            if (settings == null || settings.locales.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    "No PromptUGUISettings found, or it has no locales configured.\n\n" +
                    "Create one via 'Assets → Create → PromptUGUI/Settings', " +
                    "then select the asset and add at least one entry under 'Locales' in the Inspector.",
                    "OK");
                return;
            }

            var allExtracted = new List<ExtractedString>();
            allExtracted.AddRange(ScanAllXml());
            allExtracted.AddRange(ScanAllCSharp());

            // Group by partition.
            var byPartition = allExtracted
                .GroupBy(e => e.LocalePartition ?? "_code")
                .ToDictionary(g => g.Key, g => g.ToList());

            // Per-locale: if Addressables has labelled .po files, re-extract follows
            // the user's chosen folder. First-time extracts (no labels yet) and
            // non-Addressables setups land in DefaultOutputRoot. Path remains locked
            // to Resources/ when the runtime resolver is the Resources fallback —
            // that contract is the user's responsibility once they opt in to
            // UseAddressableResolver().
            // Folders owned by external tools (spec 2026-09-04 EPR): excluded from the
            // output-folder election and from orphan reporting, included everywhere else.
            var externalRoots = settings.externalPoRoots;
            var labelledByLocale = CollectAddressablePoPathsByLocale(externalRoots);

            var activePartitions = new HashSet<string>(byPartition.Keys);
            var writtenPaths = new List<string>();
            var orphanCount = 0;
            foreach (var lc in settings.locales)
            {
                if (string.IsNullOrEmpty(lc.locale)) continue;
                labelledByLocale.TryGetValue(lc.locale, out var labelled);
                var localeDir = AddressablePoLabelSyncer.ResolveOutputDirForLocale(
                    lc.locale,
                    labelled ?? (IEnumerable<string>)System.Array.Empty<string>(),
                    DefaultOutputRoot,
                    out var detected);
                if (detected.Count > 1)
                {
                    Debug.LogWarning(
                        $"[PromptUGUI] Multiple '{lc.locale}' folders contain labelled .po " +
                        $"files: {string.Join(", ", detected)}. Writing extraction output to " +
                        $"{localeDir} (Ordinal-sorted first). Consolidate or relabel to silence.");
                }
                foreach (var kv in byPartition)
                {
                    var path = $"{localeDir}/{kv.Key}.po";
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    var existing = File.Exists(path) ? File.ReadAllText(path) : "";
                    var merged = PoFileWriter.Merge(existing, kv.Value);
                    File.WriteAllText(path, merged);
                    writtenPaths.Add(path);
                }
                orphanCount += ReportOrphanPoFiles(localeDir, activePartitions, externalRoots);
            }
            // Targeted import instead of bulk AssetDatabase.Refresh(): unrelated
            // stale assets in the project can otherwise log "File couldn't be read"
            // attributed to this call site.
            AssetDatabase.StartAssetEditing();
            try
            {
                foreach (var p in writtenPaths) AssetDatabase.ImportAsset(p);
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
            }
            Debug.Log($"[PromptUGUI] Extract Strings: {allExtracted.Count} msgids → {writtenPaths.Count} .po files across {settings.locales.Count} locales." +
                      (orphanCount > 0 ? $" {orphanCount} orphan .po file(s) reported as errors — delete manually." : ""));
        }

        // Pure helper: which .po files under <localeDir> no longer correspond to
        // a partition produced by the current scan. Paths are returned as given
        // (caller-supplied paths), only the relative-key lookup normalizes separators.
        // <paramref name="externalRoots"/> (PromptUGUISettings.externalPoRoots) are
        // skipped outright — those files come from tools outside this project and are
        // never expected to match a partition. The check lives here rather than in the
        // caller because ReportOrphanPoFiles scans <localeDir> recursively, so an
        // external root nested inside it (e.g. <localeDir>/_server/) reaches this list.
        internal static IEnumerable<string> FindOrphanPoFiles(
            IEnumerable<string> poFilePaths, string localeDir, ISet<string> activePartitions,
            IEnumerable<string> externalRoots = null)
        {
            var prefixLen = localeDir.Length + 1;
            var roots = externalRoots == null ? null : new List<string>(externalRoots);
            foreach (var poPath in poFilePaths)
            {
                var normalized = poPath.Replace('\\', '/');
                if (AddressablePoLabelSyncer.IsUnderAnyRoot(normalized, roots)) continue;
                if (normalized.Length <= prefixLen) continue;
                var rel = normalized.Substring(prefixLen);
                if (rel.EndsWith(".po")) rel = rel.Substring(0, rel.Length - 3);
                if (!activePartitions.Contains(rel)) yield return poPath;
            }
        }

        private static int ReportOrphanPoFiles(
            string localeDir, ISet<string> activePartitions, IEnumerable<string> externalRoots)
        {
            if (!Directory.Exists(localeDir)) return 0;
            var poFiles = Directory.GetFiles(localeDir, "*.po", SearchOption.AllDirectories);
            var count = 0;
            foreach (var poPath in FindOrphanPoFiles(
                         poFiles, localeDir, activePartitions, externalRoots))
            {
                Debug.LogError(
                    $"[PromptUGUI] Orphan .po file: {poPath.Replace('\\', '/')} — " +
                    "source XML/C# no longer produces this partition. Delete the file (and its .meta) manually.");
                count++;
            }
            return count;
        }

        /// <summary>
        /// Every Addressables-labelled <c>.po</c> path in the project, bucketed by the
        /// locale its <c>Locale:&lt;locale&gt;</c> label names, minus anything under
        /// <paramref name="externalRoots"/>. Shared with <c>TranslateLocaleWindow</c>
        /// so both resolve the same locale folder. Empty when Addressables isn't installed.
        /// </summary>
        internal static Dictionary<string, List<string>> CollectAddressablePoPathsByLocale(
            IEnumerable<string> externalRoots = null)
        {
            var result = new Dictionary<string, List<string>>();
#if PROMPTUGUI_HAS_ADDRESSABLES
            var aa = AddressableAssetSettingsDefaultObject.Settings;
            if (aa == null) return result;
            var roots = externalRoots == null ? null : new List<string>(externalRoots);
            foreach (var group in aa.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;
                    var path = entry.AssetPath;
                    if (string.IsNullOrEmpty(path) ||
                        !path.EndsWith(".po", System.StringComparison.OrdinalIgnoreCase)) continue;
                    // External .po must not join the output-folder election, or a root
                    // whose name sorts earlier would silently redirect extraction output.
                    if (AddressablePoLabelSyncer.IsUnderAnyRoot(path, roots)) continue;
                    foreach (var label in entry.labels)
                    {
                        if (string.IsNullOrEmpty(label) ||
                            !label.StartsWith(AddressablePoLabelSyncer.LabelPrefix)) continue;
                        var locale = label.Substring(AddressablePoLabelSyncer.LabelPrefix.Length);
                        if (string.IsNullOrEmpty(locale)) continue;
                        if (!result.TryGetValue(locale, out var list))
                        {
                            list = new List<string>();
                            result[locale] = list;
                        }
                        list.Add(path);
                    }
                }
            }
#endif
            return result;
        }

        private static IEnumerable<ExtractedString> ScanAllXml()
        {
            // Two pass: collect all <Template> defs across the project so that a
            // Screen invoking a Template defined in a separate (commons) file can
            // still have its parameter values extracted as msgids. Files that fail
            // to parse are silently skipped here (same fallback the per-file scan
            // applies); pure-parse-error reporting belongs elsewhere.
            var paths = new List<string>();
            var pool = new Dictionary<string, TemplateDef>();
            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".ui.xml")) continue;
                if (path.StartsWith("Packages/")) continue;
                paths.Add(path);
                try
                {
                    var doc = UIDocumentParser.Parse(File.ReadAllText(path));
                    foreach (var kv in doc.Templates) pool[kv.Key] = kv.Value;
                }
                catch (ParseException) { /* surface during per-file scan */ }
            }

            foreach (var path in paths)
            {
                var text = File.ReadAllText(path);
                var partition = PathToPartition(path);
                foreach (var es in XmlStringScanner.Scan(text, partition, pool))
                {
                    if (es.References.Count == 0) es.References.Add(path);
                    yield return es;
                }
            }
        }

        private static IEnumerable<ExtractedString> ScanAllCSharp()
        {
            var guids = AssetDatabase.FindAssets("t:Script");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".cs")) continue;
                if (path.StartsWith("Packages/")) continue;
                if (path.Contains("/Tests/")) continue;
                var text = File.ReadAllText(path);
                foreach (var es in CSharpStringScanner.Scan(text, path))
                    yield return es;
            }
        }

        private static string PathToPartition(string assetPath)
        {
            // "Assets/UI/screens/MainMenu.ui.xml" → "screens/MainMenu"
            // "Assets/UI/common/Buttons.ui.xml"   → "common/Buttons"
            const string prefix = "Assets/";
            var p = assetPath.StartsWith(prefix) ? assetPath.Substring(prefix.Length) : assetPath;
            // Drop top-level folder (UI/) for shorter partitions; but only if path is multi-segment.
            var firstSlash = p.IndexOf('/');
            if (firstSlash > 0) p = p.Substring(firstSlash + 1);
            if (p.EndsWith(".ui.xml")) p = p.Substring(0, p.Length - ".ui.xml".Length);
            return p;
        }
    }
}
