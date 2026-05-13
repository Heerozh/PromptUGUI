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
        private const string DefaultOutputRoot = "Assets/Resources/PromptUGUI/i18n";

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
            var labelledByLocale = CollectAddressablePoPathsByLocale();

            var filesWritten = 0;
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
                    filesWritten++;
                }
            }
            AssetDatabase.Refresh();
            Debug.Log($"[PromptUGUI] Extract Strings: {allExtracted.Count} msgids → {filesWritten} .po files across {settings.locales.Count} locales.");
        }

        private static Dictionary<string, List<string>> CollectAddressablePoPathsByLocale()
        {
            var result = new Dictionary<string, List<string>>();
#if PROMPTUGUI_HAS_ADDRESSABLES
            var aa = AddressableAssetSettingsDefaultObject.Settings;
            if (aa == null) return result;
            foreach (var group in aa.groups)
            {
                if (group == null) continue;
                foreach (var entry in group.entries)
                {
                    if (entry == null) continue;
                    var path = entry.AssetPath;
                    if (string.IsNullOrEmpty(path) ||
                        !path.EndsWith(".po", System.StringComparison.OrdinalIgnoreCase)) continue;
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
