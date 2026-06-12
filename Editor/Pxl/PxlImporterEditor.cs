using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEngine;

namespace PromptUGUI.Editor
{
    /// <summary>.pxl 资产 Inspector（spec 2026-06-11-pxl-png-roundtrip §2）：
    /// 只读信息面板（调色板/节表/预览）+ Export PNG + Sync from PNG。
    /// importer 无可序列化设置——所有参数都在 .pxl 文本里，本面板不提供任何可改项。</summary>
    [CustomEditor(typeof(PxlImporter))]
    internal sealed class PxlImporterEditor : ScriptedImporterEditor
    {
        private const string ExportDirPrefPrefix = "PromptUGUI.Pxl.ExportDir.";

        private PxlDocument _doc;          // null = 解析失败
        private string _parseError;
        private GplPalette _palette;       // null = 内联模式或解析失败
        private string _palettePath;

        private string AssetPath => ((AssetImporter)target).assetPath;
        private string BaseName => Path.GetFileNameWithoutExtension(AssetPath);
        private string PrefKey => ExportDirPrefPrefix + AssetDatabase.AssetPathToGUID(AssetPath);

        public override void OnEnable()
        {
            base.OnEnable();
            Reload();
        }

        private void Reload()
        {
            _doc = null; _parseError = null; _palette = null; _palettePath = null;
            try
            {
                var doc = PxlParser.Parse(File.ReadAllText(AssetPath));
                if (doc.PaletteRef != null)
                {
                    _palettePath = PxlImporter.FindPalettePath(doc.PaletteRef, out var error);
                    if (_palettePath == null) { _parseError = error; return; }
                    _palette = GplPalette.Parse(File.ReadAllText(_palettePath));
                }
                PxlColorResolver.Resolve(doc, _palette); // 触发颜色/调色板校验：越板色等让 _doc 保持 null → 按钮禁用（与导入失败态一致）
                _doc = doc;
            }
            catch (PxlParseException ex) { _parseError = ex.Message; }
            catch (System.FormatException ex) { _parseError = ex.Message; }
            catch (IOException ex) { _parseError = ex.Message; }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_parseError != null)
                EditorGUILayout.HelpBox(_parseError, MessageType.Error);
            else if (_doc != null)
                DrawInfoPanel();

            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(_doc == null))
            using (new EditorGUILayout.HorizontalScope())
            {
                // 不能在 IMGUI 布局块内同步弹模态面板：SaveFolderPanel/OpenFolderPanel/
                // DisplayDialog 会泵送事件、打断 GUILayout 的 layout-group 栈，退出 using 时
                // EndLayoutGroup 抛 "BeginLayoutGroup must be called first"。延后到下一个
                // editor tick、完全脱离 OnGUI 后再执行（仓库既有 delayCall 惯例）。
                if (GUILayout.Button("Export PNG...")) EditorApplication.delayCall += ExportPng;
                if (GUILayout.Button("Sync from PNG...")) EditorApplication.delayCall += SyncFromPng;
            }
            EditorGUILayout.HelpBox(
                "All settings (ppu / border / palette / pixels) live in the .pxl text file.",
                MessageType.Info);

            serializedObject.ApplyModifiedProperties();
            ApplyRevertGUI();
        }

        private void DrawInfoPanel()
        {
            EditorGUILayout.LabelField("Palette",
                _doc.PaletteRef == null ? "inline" : $"@{_doc.PaletteRef}  ({_palettePath})");
            if (_palettePath != null && GUILayout.Button("Ping .gpl", GUILayout.Width(80)))
                EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<Object>(_palettePath));

            EditorGUILayout.LabelField("Sections", EditorStyles.boldLabel);
            var subAssets = AssetDatabase.LoadAllAssetsAtPath(AssetPath);
            var sprites = subAssets.OfType<Sprite>().ToDictionary(s => s.name, s => s);
            var hints = subAssets
                .OfType<PromptUGUI.Application.PxlSpriteHints>().FirstOrDefault();
            var tiledNames = hints != null
                ? new System.Collections.Generic.HashSet<string>(
                    hints.TiledSprites.Select(sp => sp.name))
                : null;
            foreach (var s in _doc.Sections)
            {
                var name = s.Name ?? BaseName;
                var border = s.Border == Vector4.zero
                    ? "—"
                    : $"{s.Border.x},{s.Border.y},{s.Border.z},{s.Border.w}";
                var tiledSuffix = tiledNames != null && tiledNames.Contains(name) ? "  tiled" : "";
                using (new EditorGUILayout.HorizontalScope())
                {
                    var rect = GUILayoutUtility.GetRect(32, 32, GUILayout.Width(32));
                    if (sprites.TryGetValue(name, out var sp) && sp.texture != null)
                        GUI.DrawTexture(rect, sp.texture, ScaleMode.ScaleToFit);
                    EditorGUILayout.LabelField($"[{name}]  {s.Width}×{s.Height}  border: {border}{tiledSuffix}");
                }
            }
        }

        private void ExportPng()
        {
            if (this == null || _doc == null) return; // delayCall 触发前 editor 可能已销毁/重载
            var dir = EditorUtility.SaveFolderPanel("Export .pxl sections as PNG",
                EditorPrefs.GetString(PrefKey, ""), "");
            if (string.IsNullOrEmpty(dir)) return;
            EditorPrefs.SetString(PrefKey, dir);

            var assetsRel = AbsoluteToAssetsPath(dir);
            if (assetsRel != null && PxlPngExporter.IsUnderAnySpriteSetSourceFolder(assetsRel))
            {
                if (!EditorUtility.DisplayDialog("Export into a SpriteSet source folder?",
                        "The chosen folder is inside a SpriteSet sourceFolder. Exported PNGs " +
                        "will be picked up as NEW sprite sources (duplicate keys/packing).\n\n" +
                        "Export anyway?", "Export", "Cancel"))
                    return;
            }

            var colors = PxlColorResolver.Resolve(_doc, _palette);
            foreach (var s in _doc.Sections)
                File.WriteAllBytes(Path.Combine(dir, PxlPngExporter.FileNameFor(BaseName, s)),
                    PxlPngExporter.EncodeSection(s, colors));
            if (assetsRel != null) AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(dir);
        }

        private void SyncFromPng()
        {
            if (this == null || _doc == null) return; // delayCall 触发前 editor 可能已销毁/重载
            var dir = EditorUtility.OpenFolderPanel("Sync .pxl from PNG",
                EditorPrefs.GetString(PrefKey, ""), "");
            if (string.IsNullOrEmpty(dir)) return;
            EditorPrefs.SetString(PrefKey, dir);

            var pngs = new Dictionary<string, PxlPngSync.PngImage>();
            foreach (var file in Directory.GetFiles(dir, BaseName + "*.png"))
            {
                // try/finally：单个坏/被锁 PNG 不泄漏 Texture2D 也不中止整批 sync。
                var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false);
                try
                {
                    if (!ImageConversion.LoadImage(tex, File.ReadAllBytes(file))) continue;
                    var bottomUp = tex.GetPixels32();
                    var topDown = new Color32[tex.width * tex.height];
                    for (var y = 0; y < tex.height; y++)
                        System.Array.Copy(bottomUp, (tex.height - 1 - y) * tex.width,
                            topDown, y * tex.width, tex.width);
                    pngs[Path.GetFileName(file)] =
                        new PxlPngSync.PngImage(tex.width, tex.height, topDown);
                }
                catch (IOException ex)
                {
                    Debug.LogWarning($"[Pxl] skipped unreadable PNG '{file}': {ex.Message}");
                }
                finally { DestroyImmediate(tex); }
            }

            var text = File.ReadAllText(AssetPath);
            var plan = PxlPngSync.BuildPlan(text, BaseName, pngs, _palette);

            if (plan.Errors.Count > 0)
            {
                EditorUtility.DisplayDialog("Sync from PNG — errors",
                    string.Join("\n", plan.Errors), "OK");
                return;
            }
            if (plan.Updates.Count == 0)
            {
                EditorUtility.DisplayDialog("Sync from PNG",
                    "No matching PNGs found (naming: <basename>.<section>.png).", "OK");
                return;
            }

            var summary = new System.Text.StringBuilder();
            foreach (var u in plan.Updates)
                summary.AppendLine($"[{u.Section.Name ?? BaseName}] " +
                    $"{u.Section.Width}×{u.Section.Height} → {u.NewWidth}×{u.NewHeight}");
            if (plan.NewChars.Count > 0)
                summary.AppendLine("new chars: " +
                    string.Join(", ", plan.NewChars.Select(c => $"{c.ch}={c.value}")));
            foreach (var m in plan.MissingSections) summary.AppendLine($"skipped (no PNG): [{m}]");
            foreach (var e in plan.ExtraPngs) summary.AppendLine($"unmatched PNG: {e}");

            if (!EditorUtility.DisplayDialog("Sync from PNG?", summary.ToString(), "Sync", "Cancel"))
                return;

            File.WriteAllText(AssetPath, PxlPngSync.Apply(text, plan));
            AssetDatabase.ImportAsset(AssetPath, ImportAssetOptions.ForceUpdate);
            Reload();
        }

        // 绝对路径 → "Assets/..."；不在本工程内返回 null。
        private static string AbsoluteToAssetsPath(string absolute)
        {
            var dataPath = UnityEngine.Application.dataPath.Replace('\\', '/');
            var abs = absolute.Replace('\\', '/');
            if (!abs.StartsWith(dataPath, System.StringComparison.OrdinalIgnoreCase)) return null;
            return "Assets" + abs.Substring(dataPath.Length);
        }
    }
}
