using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor {
    [CustomEditor(typeof(IconSet))]
    public sealed class IconSetEditor : UnityEditor.Editor {
        public override void OnInspectorGUI() {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            var set = (IconSet)target;
            int pngCount = 0;
            if (!string.IsNullOrEmpty(set.SourceFolderPath)
                && AssetDatabase.IsValidFolder(set.SourceFolderPath)) {
                var dict = IconAtlasSyncer.EnumeratePngs(set.SourceFolderPath);
                pngCount = dict.Count;
            }
            EditorGUILayout.LabelField("Source PNGs", pngCount.ToString());
            EditorGUILayout.LabelField("Atlas",
                set.Atlas == null ? "(not yet generated)" : AssetDatabase.GetAssetPath(set.Atlas));
            if (GUILayout.Button("Sync This Set")) {
                IconAtlasSyncer.SyncAll(new[] { set });
            }
        }
    }
}
