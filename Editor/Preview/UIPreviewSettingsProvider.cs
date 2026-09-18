using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor.Preview
{
    /// <summary>
    /// Project Settings › PromptUGUI › UI Preview: the scene the preview plays in (a
    /// <see cref="SceneAsset"/> field, stored as a GUID — see <see cref="UIPreviewSettings"/>) and
    /// the two Game view sizes behind the Landscape / Portrait buttons.
    /// </summary>
    internal static class UIPreviewSettingsProvider
    {
        private static readonly GUIContent SceneLabel = new GUIContent("Preview scene",
            "None = the package's built-in empty scene (a camera, nothing else). Pick your own to keep " +
            "its background, lights and bootstrap; the preview overlay is added at Play time and never " +
            "saved into the scene.");

        [SettingsProvider]
        public static SettingsProvider Create() => new SettingsProvider("Project/PromptUGUI/UI Preview", SettingsScope.Project)
        {
            label = "UI Preview",
            guiHandler = _ =>
            {
                var s = UIPreviewSettings.instance;
                EditorGUIUtility.labelWidth = 160;

                var currentPath = string.IsNullOrEmpty(s.sceneGuid) ? null : AssetDatabase.GUIDToAssetPath(s.sceneGuid);
                var current = string.IsNullOrEmpty(currentPath) ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(currentPath);

                EditorGUI.BeginChangeCheck();
                var picked = (SceneAsset)EditorGUILayout.ObjectField(SceneLabel, current, typeof(SceneAsset), false);
                var landscape = EditorGUILayout.Vector2IntField("Landscape (Game view)", s.landscape);
                var portrait = EditorGUILayout.Vector2IntField("Portrait (Game view)", s.portrait);
                if (EditorGUI.EndChangeCheck())
                {
                    s.sceneGuid = picked == null ? "" : AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(picked));
                    s.landscape = landscape;
                    s.portrait = portrait;
                    s.SaveNow();
                }

                if (!string.IsNullOrEmpty(s.sceneGuid) && current == null)
                    EditorGUILayout.HelpBox("Scene not found (moved or deleted?) — the built-in scene will be used.", MessageType.Warning);

                EditorGUILayout.Space();
                EditorGUILayout.HelpBox(
                    "F8 (Tools › PromptUGUI › UI Preview) enters Play in this scene and overlays a browser of every " +
                    ".ui.xml in the project: click a file to see it, save it to see the change.\n\n" +
                    "Your [RuntimeInitializeOnLoadMethod] boot runs in the preview scene too — resolvers, sprite sets, " +
                    "theme and locale come from it. Common libraries come from PromptUGUI Settings › Common Libraries.",
                    MessageType.Info);
            },
            keywords = new HashSet<string> { "PromptUGUI", "UI Preview", "preview", "ui.xml", "scene" },
        };
    }
}
