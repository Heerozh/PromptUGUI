using System.Collections.Generic;
using System.IO;
using System.Linq;
using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor.I18n
{
    internal static class StringExtractor
    {
        private const string OutputRoot = "Assets/Resources/PromptUGUI/i18n";

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

            var filesWritten = 0;
            foreach (var lc in settings.locales)
            {
                if (string.IsNullOrEmpty(lc.locale)) continue;
                foreach (var kv in byPartition)
                {
                    var path = Path.Combine(OutputRoot, lc.locale, kv.Key + ".po")
                        .Replace('\\', '/');
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

        private static IEnumerable<ExtractedString> ScanAllXml()
        {
            var guids = AssetDatabase.FindAssets("t:TextAsset");
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".ui.xml")) continue;
                if (path.StartsWith("Packages/")) continue;
                var text = File.ReadAllText(path);
                var partition = PathToPartition(path);
                foreach (var es in XmlStringScanner.Scan(text, partition))
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
