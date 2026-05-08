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

            if (!UI.HotReload.Enabled || UI.HotReload.AssetPathToSrc == null) return;

            foreach (var p in importedAssets.Concat(movedAssets)) {
                if (!p.EndsWith(".ui.xml")) continue;
                try { UI.HotReload.NotifyAssetChanged(p); }
                catch (System.Exception e) {
                    UnityEngine.Debug.LogError(
                        $"[PromptUGUI] hot reload failed for {p}: {e.Message}");
                }
            }
        }
    }
}
