#if PROMPTUGUI_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using PromptUGUI.Application;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEngine;

namespace PromptUGUI.Editor.I18n
{
    internal static class AddressablePoMenu
    {
        [MenuItem("Tools/PromptUGUI/I18n/Setup Addressables for Locale PO Files")]
        public static void SetupAddressablesForLocalePoFiles()
        {
            var settings = AddressableAssetSettingsDefaultObject.Settings
                           ?? AddressableAssetSettingsDefaultObject.GetSettings(true);
            if (settings == null)
            {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    "Addressables is installed but has no settings asset. " +
                    "Open Window → Asset Management → Addressables → Groups " +
                    "and click 'Create Addressables Settings' first.",
                    "OK");
                return;
            }

            var promptSettings = PromptUGUISettings.Instance;
            if (promptSettings == null || promptSettings.locales == null
                                       || promptSettings.locales.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    "No PromptUGUISettings found, or it has no locales configured. " +
                    "Add at least one entry under 'Locales' in PromptUGUISettings " +
                    "before running this menu.",
                    "OK");
                return;
            }

            var locales = new List<string>();
            foreach (var lc in promptSettings.locales)
                if (lc != null && !string.IsNullOrEmpty(lc.locale))
                    locales.Add(lc.locale);

            var poPaths = new List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:TextAsset"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".po", StringComparison.OrdinalIgnoreCase)
                    && !path.StartsWith("Packages/"))
                    poPaths.Add(path);
            }

            if (poPaths.Count == 0)
            {
                EditorUtility.DisplayDialog(
                    "PromptUGUI",
                    "No .po files found in this project. Run " +
                    "'Tools → PromptUGUI → I18n → 1. Extract Strings' first.",
                    "OK");
                return;
            }

            var orphans = AddressablePoLabelSyncer.FindOrphanPoPaths(poPaths, locales);
            foreach (var orphanPath in orphans)
            {
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(orphanPath);
                Debug.LogWarning(
                    $"[PromptUGUI] Skipped (no recognized locale folder in path): {orphanPath}. " +
                    $"Expected one of the parent folders to be: {string.Join(", ", locales)}. " +
                    "Rename the folder to match one of the configured locales, or add the " +
                    "locale to PromptUGUISettings.", asset);
            }

            AddressablePoLabelSyncer.MakeLocalePoFilesAddressable(poPaths, locales);
            AssetDatabase.SaveAssets();
            var labelled = poPaths.Count - orphans.Count;
            Debug.Log(
                "[PromptUGUI] Setup Addressables for Locale PO Files: " +
                $"labelled {labelled} of {poPaths.Count} .po file(s) across {locales.Count} locale(s)" +
                (orphans.Count > 0 ? $" — {orphans.Count} skipped (see warnings above)." : ".") +
                $" Labelled files now carry `{AddressablePoLabelSyncer.LabelPrefix}<locale>`.");
            Debug.Log("[PromptUGUI] Addressable setup complete. " +
                      "Please go Window → Asset Management → Addressable → Groups " +
                      "to verify.");
        }
    }
}
#endif
