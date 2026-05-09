using System.Linq;
using UnityEditor;
using PromptUGUI.Application;

namespace PromptUGUI.Editor {
    internal sealed class UIAssetPostprocessor : AssetPostprocessor {
        static void OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths) {

            if (!UI.HotReload.Enabled) return;

            // .ui.xml branch (existing)
            if (UI.HotReload.AssetPathToSrc != null) {
                foreach (var p in importedAssets.Concat(movedAssets)) {
                    if (!p.EndsWith(".ui.xml")) continue;
                    try { UI.HotReload.NotifyAssetChanged(p); }
                    catch (System.Exception e) {
                        UnityEngine.Debug.LogError(
                            $"[PromptUGUI] hot reload failed for {p}: {e.Message}");
                    }
                }
            }

            // .po branch — reload current locale if any .po under PromptUGUI/i18n[-custom]/<current>/ changed
            var current = UI.Locale.Current;
            if (current != null) {
                bool poChanged = importedAssets.Concat(movedAssets).Concat(deletedAssets)
                    .Any(p => p.EndsWith(".po") && IsForLocale(p, current));
                if (poChanged) {
                    try { UI.Locale.ReloadCurrent(); }
                    catch (System.Exception e) {
                        UnityEngine.Debug.LogError(
                            $"[PromptUGUI] .po hot reload failed: {e.Message}");
                    }
                }
            }

            // Settings.asset branch — if PromptUGUISettings changed and a current locale exists, ReSolve.
            bool settingsChanged = importedAssets
                .Any(p => AssetDatabase.GetMainAssetTypeAtPath(p) == typeof(PromptUGUISettings));
            if (settingsChanged && UI.Locale.Current != null) {
                UI.NotifyVariantChangedForReSolve();
            }
        }

        static bool IsForLocale(string assetPath, string locale) {
            // path shape: Assets/Resources/PromptUGUI/i18n/<locale>/... or i18n-custom/<locale>/...
            return assetPath.Contains($"/PromptUGUI/i18n/{locale}/")
                || assetPath.Contains($"/PromptUGUI/i18n-custom/{locale}/");
        }
    }
}
