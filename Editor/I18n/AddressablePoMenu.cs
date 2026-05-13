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

            AddressablePoLabelSyncer.MakeLocalePoFilesAddressable(poPaths, locales);
            AssetDatabase.SaveAssets();
            Debug.Log(
                "[PromptUGUI] Setup Addressables for Locale PO Files: " +
                $"processed {poPaths.Count} .po file(s) across {locales.Count} locale(s). " +
                "Files under a known-locale folder are now Addressable and labelled " +
                $"`{AddressablePoLabelSyncer.LabelPrefix}<locale>`.");
            Debug.Log("[PromptUGUI] Addressable setup complete. " +
                      "Please go Window → Asset Management → Addressable → Groups " +
                      "to verify.");
        }
    }
}
#endif
