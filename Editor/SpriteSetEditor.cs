using PromptUGUI.Application;
using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    [CustomEditor(typeof(SpriteSet))]
    public sealed class SpriteSetEditor : UnityEditor.Editor
    {
        private UnityEditor.Editor _importerEditor;
        private string _templateTexturePath;

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
            _templateTexturePath = null;
        }

        private void EnsureImporterEditor(string folder)
        {
            var first = SpriteAtlasSyncer.FindFirstTexture(folder);
            if (first == _templateTexturePath && _importerEditor != null) return;
            DestroyImporterEditor();
            if (string.IsNullOrEmpty(first)) return;
            var importer = AssetImporter.GetAtPath(first);
            if (importer == null) return;
            _importerEditor = CreateEditor(importer);
            _templateTexturePath = first;
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
            var sourceCount = SpriteAtlasSyncer.CountSpriteSources(folder);
            EditorGUILayout.LabelField("Source sprites", sourceCount.ToString());
            EditorGUILayout.LabelField("Atlas",
                set.Atlas == null ? "(not yet generated)" : AssetDatabase.GetAssetPath(set.Atlas));
            if (GUILayout.Button("Sync This Set"))
            {
                SpriteAtlasSyncer.SyncAll(new[] { set });
                InlineSpriteAssetBuilder.RegenerateFromProject();
            }

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Texture Import Settings", EditorStyles.boldLabel);
            DrawImportSettingsSection(folder, sourceCount);
        }

        private void DrawImportSettingsSection(string folder, int sourceCount)
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
                if (sourceCount > 0)
                {
                    // Folder has sprite sources but none of them is a TextureImporter
                    // asset (eg. all Aseprite or other non-TextureImporter formats).
                    // The template / apply-to-all / reset flows are TextureImporter-only.
                    EditorGUILayout.HelpBox(
                        $"No regular texture (PNG / JPG / TGA / PSD / ...) found under " +
                        $"'{folder}'. Aseprite and other non-TextureImporter formats " +
                        $"manage their own per-file import settings — edit each file " +
                        $"directly in the Project window. Add a regular texture to " +
                        $"unlock the template / apply-to-all / reset flow.",
                        MessageType.Info);
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        $"No texture found under '{folder}'. " +
                        "Add a texture to define import settings.",
                        MessageType.Info);
                }
                // Reset button is TextureImporter-only too; nothing to reset here.
                return;
            }

            EditorGUILayout.LabelField("Template", _templateTexturePath);
            EditorGUILayout.HelpBox(
                "Edit the import settings below — they'll be applied to every texture in " +
                "the folder when you click 'Apply to All'.",
                MessageType.None);
            using (new EditorGUI.IndentLevelScope())
            {
                _importerEditor.OnInspectorGUI();
            }
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(sourceCount <= 1))
            {
                var label = sourceCount <= 1
                    ? "Apply Settings to All Textures in Folder (template is the only texture)"
                    : $"Apply Settings to All {sourceCount} Textures in Folder";
                if (GUILayout.Button(label))
                {
                    if (EditorUtility.DisplayDialog(
                        "Apply Import Settings",
                        $"Copy import settings from\n  {_templateTexturePath}\n" +
                        $"to every texture under\n  {folder}?\n\n" +
                        "This overrides any per-texture manual TextureImporter tweaks.",
                        "Apply", "Cancel"))
                    {
                        FlushTemplatePendingEdits();
                        var n = SpriteAtlasSyncer.ApplyImportSettingsToFolder(
                            _templateTexturePath, folder, showProgress: true);
                        Debug.Log(
                            $"[SpriteSync] copied import settings to {n} texture(s) " +
                            $"under '{folder}'");
                    }
                }
            }
            EditorGUILayout.Space();
            DrawCanonicalResetButton(folder);
        }

        private static void DrawCanonicalResetButton(string folder)
        {
            if (!GUILayout.Button("Reset All Textures Format")) return;
            if (string.IsNullOrEmpty(folder))
            {
                EditorUtility.DisplayDialog(
                    "Reset Texture Import Format",
                    "Source folder is not set on this SpriteSet.", "OK");
                return;
            }
            if (EditorUtility.DisplayDialog(
                "Reset Texture Import Format",
                $"Force re-import every texture under '{folder}' as:\n\n" +
                "  • Texture Type: Sprite\n" +
                "  • Sprite Mode: Single\n" +
                "  • Compression: Uncompressed\n\n" +
                "This overrides any manual TextureImporter tweaks on these textures.",
                "Reset", "Cancel"))
            {
                var n = SpriteAtlasSyncer.ResetTextureImportSettings(folder, showProgress: true);
                Debug.Log($"[SpriteSync] reset {n} texture(s) under '{folder}'");
            }
        }
    }
}
