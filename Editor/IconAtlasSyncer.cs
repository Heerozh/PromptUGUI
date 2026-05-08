using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace PromptUGUI.Editor {
    public static class IconAtlasSyncer {
        const string DynamicMarker = "{{";

        /// <summary>(setName, iconName) pairs found across all .ui.xml in the project.</summary>
        public static HashSet<(string set, string name)> ScanXmlReferences() {
            var refs = new HashSet<(string, string)>();
            var guids = AssetDatabase.FindAssets("t:TextAsset");
            foreach (var guid in guids) {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".ui.xml", StringComparison.Ordinal)) continue;
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException ex) {
                    Debug.LogWarning($"[IconSync] cannot read {path}: {ex.Message}");
                    continue;
                }
                UIDocument doc;
                try { doc = UIDocumentParser.Parse(text); }
                catch (ParseException ex) {
                    Debug.LogWarning($"[IconSync] skipping malformed {path}: {ex.Message}");
                    continue;
                }
                foreach (var screen in doc.Screens)
                    CollectFromNode(screen.Root, refs, path);
                foreach (var tpl in doc.Templates.Values)
                    if (tpl.Body != null) CollectFromNode(tpl.Body, refs, path);
            }
            return refs;
        }

        static void CollectFromNode(ElementNode node,
                                    HashSet<(string, string)> refs, string path) {
            if (node == null) return;
            if (node.Tag == "Icon" && node.Namespace == null) {
                CollectFromAttr(node.Attributes.TryGetValue("name", out var n) ? n : null,
                                refs, path);
                if (node.VariantOverrides.TryGetValue("name", out var list))
                    foreach (var (_, v) in list) CollectFromAttr(v, refs, path);
            }
            foreach (var c in node.Children) CollectFromNode(c, refs, path);
        }

        static void CollectFromAttr(string value,
                                    HashSet<(string, string)> refs, string path) {
            if (string.IsNullOrEmpty(value)) return;
            int colon = value.IndexOf(':');
            if (colon <= 0 || colon == value.Length - 1) return;
            var ns = value.Substring(0, colon);
            var name = value.Substring(colon + 1);
            if (ns.Contains(DynamicMarker)) {
                Debug.LogWarning(
                    $"[IconSync] {path}: <Icon name='{value}'>: dynamic namespace " +
                    $"({DynamicMarker}...) is not analyzable; skipping");
                return;
            }
            if (name.Contains(DynamicMarker)) {
                Debug.LogWarning(
                    $"[IconSync] {path}: <Icon name='{value}'>: dynamic icon name " +
                    $"({DynamicMarker}...); list candidates in IconSet.alwaysInclude");
                return;
            }
            refs.Add((ns, name));
        }

        /// <summary>{iconName -> Sprite} 收集 sourceFolder 下所有 PNG。</summary>
        public static Dictionary<string, Sprite> EnumeratePngs(string folderAssetPath) {
            var dict = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(folderAssetPath)) return dict;
            if (!AssetDatabase.IsValidFolder(folderAssetPath)) {
                Debug.LogError($"[IconSync] not a folder: '{folderAssetPath}'");
                return dict;
            }

            var fullFolder = Path.GetFullPath(folderAssetPath);
            foreach (var fullPath in Directory.EnumerateFiles(
                         fullFolder, "*.png", SearchOption.AllDirectories)) {
                var assetPath = "Assets" +
                    fullPath.Substring(UnityEngine.Application.dataPath.Length).Replace('\\', '/');
                EnsureSpriteImporter(assetPath);
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sp == null) continue;
                var name = Path.GetFileNameWithoutExtension(assetPath);
                if (dict.ContainsKey(name))
                    Debug.LogWarning(
                        $"[IconSync] duplicate icon '{name}' in {folderAssetPath}; using first");
                else
                    dict[name] = sp;
            }
            return dict;
        }

        static void EnsureSpriteImporter(string assetPath) {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;
            if (importer.textureType == TextureImporterType.Sprite) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.SaveAndReimport();
        }

        /// <summary>差量同步 atlas 的 packables。返回 true 表示发生了变更。</summary>
        public static bool UpdateAtlas(SpriteAtlas atlas, Sprite[] desired) {
            var current = atlas.GetPackables();
            if (PackablesEqual(current, desired)) return false;
            atlas.Remove(current);
            var asObjects = new UnityEngine.Object[desired.Length];
            for (int i = 0; i < desired.Length; i++) asObjects[i] = desired[i];
            atlas.Add(asObjects);
            EditorUtility.SetDirty(atlas);
            SpriteAtlasUtility.PackAtlases(
                new[] { atlas },
                EditorUserBuildSettings.activeBuildTarget);
            return true;
        }

        static bool PackablesEqual(UnityEngine.Object[] a, Sprite[] b) {
            if (a.Length != b.Length) return false;
            var aSet = new HashSet<string>();
            foreach (var o in a) {
                var path = AssetDatabase.GetAssetPath(o);
                aSet.Add(AssetDatabase.AssetPathToGUID(path) + "|" + (o as Sprite)?.name);
            }
            foreach (var s in b) {
                var path = AssetDatabase.GetAssetPath(s);
                var key = AssetDatabase.AssetPathToGUID(path) + "|" + s.name;
                if (!aSet.Contains(key)) return false;
            }
            return true;
        }

        /// <summary>若 IconSet.atlas 为 null，在 SO 同目录创建 &lt;setName&gt;.spriteatlas 并回填。</summary>
        internal static SpriteAtlas EnsureAtlasAsset(PromptUGUI.Application.IconSet set) {
            if (set.Atlas != null) return set.Atlas;
            var setPath = AssetDatabase.GetAssetPath(set);
            if (string.IsNullOrEmpty(setPath)) {
                Debug.LogError("[IconSync] IconSet not saved as asset; cannot create atlas");
                return null;
            }
            var dir = Path.GetDirectoryName(setPath).Replace('\\', '/');
            var atlasPath = $"{dir}/{set.SetName}.spriteatlas";
            var atlas = new SpriteAtlas();
            AssetDatabase.CreateAsset(atlas, atlasPath);
            set.SetAtlasInternal(atlas);
            AssetDatabase.SaveAssets();
            return atlas;
        }
    }
}
