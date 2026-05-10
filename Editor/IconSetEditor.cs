using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    [CustomEditor(typeof(IconSet))]
    public sealed class IconSetEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            var set = (IconSet)target;
            var pngCount = IconAtlasSyncer.CountPngs(set.SourceFolderPath);
            EditorGUILayout.LabelField("Source PNGs", pngCount.ToString());
            EditorGUILayout.LabelField("Atlas",
                set.Atlas == null ? "(not yet generated)" : AssetDatabase.GetAssetPath(set.Atlas));
            if (GUILayout.Button("Sync This Set"))
            {
                IconAtlasSyncer.SyncAll(new[] { set });
            }
            if (GUILayout.Button("Reset All PNGs Format"))
            {
                var folder = set.SourceFolderPath;
                if (string.IsNullOrEmpty(folder))
                {
                    EditorUtility.DisplayDialog(
                        "Reset PNG Import Format",
                        "Source folder is not set on this IconSet.", "OK");
                }
                else if (EditorUtility.DisplayDialog(
                    "Reset PNG Import Format",
                    $"Force re-import every PNG under '{folder}' as:\n\n" +
                    "  • Texture Type: Sprite\n" +
                    "  • Sprite Mode: Single\n" +
                    "  • Compression: Uncompressed\n\n" +
                    "This overrides any manual TextureImporter tweaks on these PNGs.",
                    "Reset", "Cancel"))
                {
                    var n = IconAtlasSyncer.ResetPngImportSettings(folder, showProgress: true);
                    Debug.Log($"[IconSync] reset {n} PNG(s) under '{folder}'");
                }
            }
        }
    }
}
