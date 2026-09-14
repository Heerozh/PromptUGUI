using System.Collections.Generic;
using System.Linq;
using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    [InitializeOnLoad]
    internal static class PromptUGUISettingsAutoMaintainer
    {
        static PromptUGUISettingsAutoMaintainer()
        {
            EditorApplication.delayCall += Sync;
        }

        internal static void Sync()
        {
            // settings 资产被导入 / 删除 / 移动：Instance 的缓存可能指着旧对象，丢掉重找
            PromptUGUISettings.ResetInstanceCache();
            var guids = AssetDatabase.FindAssets("t:PromptUGUISettings");
            if (guids.Length == 0) return;
            if (guids.Length > 1)
            {
                Debug.LogError(
                    "[PromptUGUI] Multiple PromptUGUISettings assets found; " +
                    "keep exactly one. preloadedAssets not updated.");
                return;
            }
            var path = AssetDatabase.GUIDToAssetPath(guids[0]);
            var settings = AssetDatabase.LoadAssetAtPath<PromptUGUISettings>(path);
            if (settings == null) return;

            var preloaded = PlayerSettings.GetPreloadedAssets()?.ToList() ?? new List<Object>();
            // Drop dead refs + duplicates of our type, then add the canonical one.
            preloaded.RemoveAll(a => a == null || a is PromptUGUISettings);
            preloaded.Add(settings);
            PlayerSettings.SetPreloadedAssets(preloaded.ToArray());
        }

        private sealed class Postprocessor : AssetPostprocessor
        {
            private static void OnPostprocessAllAssets(
                string[] importedAssets, string[] deletedAssets,
                string[] movedAssets, string[] movedFromAssetPaths)
            {
                var touched =
                    importedAssets.Any(IsSettings)
                    || deletedAssets.Any(IsSettings)
                    || movedAssets.Any(IsSettings)
                    || movedFromAssetPaths.Any(IsSettings);
                if (touched) Sync();
            }
            private static bool IsSettings(string p) =>
                p.EndsWith(".asset") &&
                AssetDatabase.GetMainAssetTypeAtPath(p) == typeof(PromptUGUISettings);
        }
    }
}
