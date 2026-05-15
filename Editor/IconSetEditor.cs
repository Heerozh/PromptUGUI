using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    [CustomEditor(typeof(SpriteSet))]
    public sealed class IconSetEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor _importerEditor;
        private string _templatePngPath;

        private void OnDisable()
        {
            DestroyImporterEditor();
        }

        private void DestroyImporterEditor()
        {
            if (_importerEditor != null)
            {
                DestroyImmediate(_importerEditor);
                _importerEditor = null;
            }
            _templatePngPath = null;
        }

        private void EnsureImporterEditor(string folder)
        {
            var first = IconAtlasSyncer.FindFirstPng(folder);
            if (first == _templatePngPath && _importerEditor != null) return;
            DestroyImporterEditor();
            if (string.IsNullOrEmpty(first)) return;
            var importer = AssetImporter.GetAtPath(first);
            if (importer == null) return;
            _importerEditor = CreateEditor(importer);
            _templatePngPath = first;
        }

        // Commit any pending SerializedObject edits in the embedded TextureImporterInspector
        // to the template asset, so a subsequent EditorUtility.CopySerialized sees the
        // user's latest tweaks rather than the on-disk snapshot. Mirrors what Unity's
        // AssetImporterEditor.ApplyAndImport does internally.
        private void FlushTemplatePendingEdits()
        {
            if (_importerEditor == null) return;
            if (_importerEditor.serializedObject != null)
                _importerEditor.serializedObject.ApplyModifiedPropertiesWithoutUndo();
            if (_importerEditor.target is AssetImporter ai)
                ai.SaveAndReimport();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.Space();
            var set = (SpriteSet)target;
            var folder = set.SourceFolderPath;
            var pngCount = IconAtlasSyncer.CountPngs(folder);
            EditorGUILayout.LabelField("Source PNGs", pngCount.ToString());
            EditorGUILayout.LabelField("Atlas",
                set.Atlas == null ? "(not yet generated)" : AssetDatabase.GetAssetPath(set.Atlas));
            if (GUILayout.Button("Sync This Set"))
            {
                IconAtlasSyncer.SyncAll(new[] { set });
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("PNG Import Settings", EditorStyles.boldLabel);
            DrawImportSettingsSection(folder, pngCount);
        }

        private void DrawImportSettingsSection(string folder, int pngCount)
        {
            if (string.IsNullOrEmpty(folder))
            {
                EditorGUILayout.HelpBox(
                    "Source folder is not set on this SpriteSet.", MessageType.Info);
                return;
            }
            EnsureImporterEditor(folder);
            if (_importerEditor == null)
            {
                EditorGUILayout.HelpBox(
                    $"No PNG found under '{folder}'. " +
                    "Add a PNG to define import settings.",
                    MessageType.Info);
                DrawCanonicalResetButton(folder);
                return;
            }

            EditorGUILayout.LabelField("Template", _templatePngPath);
            EditorGUILayout.HelpBox(
                "Edit the import settings below — they'll be applied to every PNG in " +
                "the folder when you click 'Apply to All'.",
                MessageType.None);
            using (new EditorGUI.IndentLevelScope())
            {
                _importerEditor.OnInspectorGUI();
            }
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(pngCount <= 1))
            {
                var label = pngCount <= 1
                    ? "Apply Settings to All PNGs in Folder (template is the only PNG)"
                    : $"Apply Settings to All {pngCount} PNGs in Folder";
                if (GUILayout.Button(label))
                {
                    if (EditorUtility.DisplayDialog(
                        "Apply Import Settings",
                        $"Copy import settings from\n  {_templatePngPath}\n" +
                        $"to every PNG under\n  {folder}?\n\n" +
                        "This overrides any per-PNG manual TextureImporter tweaks.",
                        "Apply", "Cancel"))
                    {
                        FlushTemplatePendingEdits();
                        var n = IconAtlasSyncer.ApplyImportSettingsToFolder(
                            _templatePngPath, folder, showProgress: true);
                        Debug.Log(
                            $"[IconSync] copied import settings to {n} PNG(s) " +
                            $"under '{folder}'");
                    }
                }
            }
            EditorGUILayout.Space();
            DrawCanonicalResetButton(folder);
        }

        private static void DrawCanonicalResetButton(string folder)
        {
            if (!GUILayout.Button("Reset All PNGs Format")) return;
            if (string.IsNullOrEmpty(folder))
            {
                EditorUtility.DisplayDialog(
                    "Reset PNG Import Format",
                    "Source folder is not set on this SpriteSet.", "OK");
                return;
            }
            if (EditorUtility.DisplayDialog(
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
