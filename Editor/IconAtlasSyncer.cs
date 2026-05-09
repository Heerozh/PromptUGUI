using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace PromptUGUI.Editor
{
    public static class IconAtlasSyncer
    {
        private const string DynamicMarker = "{{";
        private const string ProgressTitle = "PromptUGUI Icon Sync";

        /// <summary>(setName, iconName) pairs found across all .ui.xml in the project.</summary>
        /// <param name="showProgress">When true, drives a cancelable progress bar; throws
        /// <see cref="OperationCanceledException"/> if the user cancels.</param>
        public static HashSet<(string set, string name)> ScanXmlReferences(
            bool showProgress = false)
        {
            var refs = new HashSet<(string, string)>();
            var guids = AssetDatabase.FindAssets("t:TextAsset");
            for (var i = 0; i < guids.Length; i++)
            {
                var guid = guids[i];
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".ui.xml", StringComparison.Ordinal)) continue;
                if (showProgress &&
                    EditorUtility.DisplayCancelableProgressBar(
                        ProgressTitle,
                        $"Scanning XML references ({i + 1}/{guids.Length}): {path}",
                        (float)i / Mathf.Max(1, guids.Length)))
                {
                    throw new OperationCanceledException();
                }
                string text;
                try { text = File.ReadAllText(path); }
                catch (IOException ex)
                {
                    Debug.LogWarning($"[IconSync] cannot read {path}: {ex.Message}");
                    continue;
                }
                UIDocument doc;
                try { doc = UIDocumentParser.Parse(text); }
                catch (ParseException ex)
                {
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

        private static void CollectFromNode(ElementNode node,
                                    HashSet<(string, string)> refs, string path)
        {
            if (node == null) return;
            if (node.Tag == "Icon" && node.Namespace == null)
            {
                CollectFromAttr(node.Attributes.TryGetValue("name", out var n) ? n : null,
                                refs, path);
                if (node.VariantOverrides.TryGetValue("name", out var list))
                    foreach (var (_, v) in list) CollectFromAttr(v, refs, path);
            }
            foreach (var c in node.Children) CollectFromNode(c, refs, path);
        }

        private static void CollectFromAttr(string value,
                                    HashSet<(string, string)> refs, string path)
        {
            if (string.IsNullOrEmpty(value)) return;
            var colon = value.IndexOf(':');
            if (colon <= 0 || colon == value.Length - 1) return;
            var ns = value.Substring(0, colon);
            var name = value.Substring(colon + 1);
            if (ns.Contains(DynamicMarker))
            {
                Debug.LogWarning(
                    $"[IconSync] {path}: <Icon name='{value}'>: dynamic namespace " +
                    $"({DynamicMarker}...) is not analyzable; skipping");
                return;
            }
            if (name.Contains(DynamicMarker))
            {
                Debug.LogWarning(
                    $"[IconSync] {path}: <Icon name='{value}'>: dynamic icon name " +
                    $"({DynamicMarker}...); list candidates in IconSet.alwaysInclude");
                return;
            }
            refs.Add((ns, name));
        }

        /// <summary>Cheap recursive count of *.png files under a folder. No asset
        /// loading, no importer mutation — safe to call from OnInspectorGUI.</summary>
        public static int CountPngs(string folderAssetPath)
        {
            if (string.IsNullOrEmpty(folderAssetPath)) return 0;
            if (!AssetDatabase.IsValidFolder(folderAssetPath)) return 0;
            var fullFolder = Path.GetFullPath(folderAssetPath);
            var n = 0;
            foreach (var _ in Directory.EnumerateFiles(
                         fullFolder, "*.png", SearchOption.AllDirectories)) n++;
            return n;
        }

        /// <summary>{iconName -> Sprite} 收集 sourceFolder 下所有 PNG。Triggers sprite
        /// reimport on first encounter — call from sync paths, not Inspector repaints.</summary>
        /// <param name="progressLabel">When non-null, drives a cancelable progress bar
        /// and throws <see cref="OperationCanceledException"/> if the user cancels.</param>
        public static Dictionary<string, Sprite> EnumeratePngs(
            string folderAssetPath, string progressLabel = null)
        {
            var dict = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(folderAssetPath)) return dict;
            if (!AssetDatabase.IsValidFolder(folderAssetPath))
            {
                Debug.LogError($"[IconSync] not a folder: '{folderAssetPath}'");
                return dict;
            }

            var fullFolder = Path.GetFullPath(folderAssetPath);
            var files = new List<string>(Directory.EnumerateFiles(
                fullFolder, "*.png", SearchOption.AllDirectories));
            for (var i = 0; i < files.Count; i++)
            {
                var fullPath = files[i];
                var assetPath = "Assets" +
                    fullPath.Substring(UnityEngine.Application.dataPath.Length).Replace('\\', '/');
                if (progressLabel != null &&
                    EditorUtility.DisplayCancelableProgressBar(
                        ProgressTitle,
                        $"{progressLabel}: {Path.GetFileName(assetPath)} ({i + 1}/{files.Count})",
                        (float)i / Mathf.Max(1, files.Count)))
                {
                    throw new OperationCanceledException();
                }
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

        private static void EnsureSpriteImporter(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not TextureImporter importer) return;
            if (importer.textureType == TextureImporterType.Sprite) return;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
        }

        /// <summary>差量同步 atlas 的 packables。返回 true 表示发生了变更。
        /// V2 atlases (`*.spriteatlasv2`) require <see cref="SpriteAtlasAsset.Save"/>
        /// to persist; mutating the runtime <see cref="SpriteAtlas"/> view alone updates
        /// only in-memory state and the editor will show an empty atlas on disk.</summary>
        public static bool UpdateAtlas(SpriteAtlas atlas, Sprite[] desired)
        {
            var path = AssetDatabase.GetAssetPath(atlas);
            if (!string.IsNullOrEmpty(path) &&
                path.EndsWith(".spriteatlasv2", StringComparison.Ordinal))
            {
                return UpdateAtlasV2(path, desired);
            }
            return UpdateAtlasV1(atlas, desired);
        }

        private static bool UpdateAtlasV1(SpriteAtlas atlas, Sprite[] desired)
        {
            var current = atlas.GetPackables();
            if (PackablesEqual(current, desired)) return false;
            atlas.Remove(current);
            var asObjects = new UnityEngine.Object[desired.Length];
            for (var i = 0; i < desired.Length; i++) asObjects[i] = desired[i];
            atlas.Add(asObjects);
            EditorUtility.SetDirty(atlas);
            SpriteAtlasUtility.PackAtlases(
                new[] { atlas },
                EditorUserBuildSettings.activeBuildTarget);
            return true;
        }

        // V2's packables list lives at m_ImporterData.packables. v2.Add/Remove route
        // through this serialized array but offer no enumeration, and v2.GetMasterAtlas()
        // returns the packed-output runtime view, NOT this input list — which is why a
        // diff against master.GetPackables() lets every sync re-Add and accumulate.
        // Overwrite the array via SerializedObject so re-sync produces a stable result.
        private const string V2PackablesPath = "m_ImporterData.packables";

        private static bool UpdateAtlasV2(string path, Sprite[] desired)
        {
            var v2 = SpriteAtlasAsset.Load(path);
            if (v2 == null)
            {
                Debug.LogError($"[IconSync] failed to load V2 atlas at {path}");
                return false;
            }
            var so = new SerializedObject(v2);
            var prop = so.FindProperty(V2PackablesPath);
            if (prop == null || !prop.isArray)
            {
                Debug.LogError(
                    $"[IconSync] cannot find '{V2PackablesPath}' on V2 atlas at {path}; " +
                    $"Unity API may have changed");
                return false;
            }

            var current = new UnityEngine.Object[prop.arraySize];
            for (var i = 0; i < prop.arraySize; i++)
                current[i] = prop.GetArrayElementAtIndex(i).objectReferenceValue;
            if (PackablesEqual(current, desired)) return false;

            prop.arraySize = desired.Length;
            for (var i = 0; i < desired.Length; i++)
                prop.GetArrayElementAtIndex(i).objectReferenceValue = desired[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            SpriteAtlasAsset.Save(v2, path);
            AssetDatabase.ImportAsset(path);
            var refreshed = AssetDatabase.LoadAssetAtPath<SpriteAtlas>(path);
            if (refreshed != null)
            {
                SpriteAtlasUtility.PackAtlases(
                    new[] { refreshed },
                    EditorUserBuildSettings.activeBuildTarget);
            }
            return true;
        }

        private static bool PackablesEqual(UnityEngine.Object[] a, Sprite[] b)
        {
            if (a.Length != b.Length) return false;
            var aSet = new HashSet<string>();
            foreach (var o in a)
            {
                var path = AssetDatabase.GetAssetPath(o);
                aSet.Add(AssetDatabase.AssetPathToGUID(path) + "|" + (o as Sprite)?.name);
            }
            foreach (var s in b)
            {
                var path = AssetDatabase.GetAssetPath(s);
                var key = AssetDatabase.AssetPathToGUID(path) + "|" + s.name;
                if (!aSet.Contains(key)) return false;
            }
            return true;
        }

        public static void SyncAll(IEnumerable<PromptUGUI.Application.IconSet> sets)
        {
            var setList = new List<PromptUGUI.Application.IconSet>(sets);
            try
            {
                var refs = ScanXmlReferences(showProgress: true);

                // detect duplicate setNames before any work
                var seen = new HashSet<string>();
                foreach (var s in setList)
                {
                    if (s == null) continue;
                    if (string.IsNullOrEmpty(s.SetName))
                    {
                        Debug.LogError($"[IconSync] IconSet '{s.name}' has empty setName");
                        return;
                    }
                    if (!seen.Add(s.SetName))
                    {
                        Debug.LogError(
                            $"[IconSync] duplicate IconSet setName '{s.SetName}'; aborting");
                        return;
                    }
                }

                for (var i = 0; i < setList.Count; i++)
                {
                    var set = setList[i];
                    if (set == null) continue;
                    var folder = set.SourceFolderPath;
                    if (string.IsNullOrEmpty(folder) || !AssetDatabase.IsValidFolder(folder))
                    {
                        Debug.LogError($"[IconSync] IconSet '{set.SetName}': sourceFolder invalid");
                        continue;
                    }
                    var label = $"Set {i + 1}/{setList.Count} '{set.SetName}'";
                    var available = EnumeratePngs(folder, label);
                    var needed = new HashSet<string>();
                    foreach (var (ns, name) in refs)
                        if (ns == set.SetName) needed.Add(name);
                    foreach (var n in set.AlwaysInclude)
                        if (!string.IsNullOrEmpty(n)) needed.Add(n);

                    var picked = new List<Sprite>();
                    var missing = new List<string>();
                    foreach (var n in needed)
                    {
                        if (available.TryGetValue(n, out var sp)) picked.Add(sp);
                        else missing.Add(n);
                    }
                    if (missing.Count > 0)
                        Debug.LogWarning(
                            $"[IconSync] '{set.SetName}': XML references missing PNGs: " +
                            string.Join(", ", missing));

                    if (EditorUtility.DisplayCancelableProgressBar(
                            ProgressTitle, $"{label}: packing atlas...",
                            (i + 0.9f) / Mathf.Max(1, setList.Count)))
                    {
                        throw new OperationCanceledException();
                    }
                    var atlas = EnsureAtlasAsset(set);
                    if (atlas == null) continue;
                    UpdateAtlas(atlas, picked.ToArray());
                }

                AssetDatabase.SaveAssets();
            }
            catch (OperationCanceledException)
            {
                Debug.LogWarning("[IconSync] cancelled by user");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static IEnumerable<PromptUGUI.Application.IconSet> FindAllIconSets()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(PromptUGUI.Application.IconSet));
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var s = AssetDatabase.LoadAssetAtPath<PromptUGUI.Application.IconSet>(path);
                if (s != null) yield return s;
            }
        }

        /// <summary>若 IconSet.atlas 为 null，在 SO 同目录创建 &lt;setName&gt;.spriteatlas 并回填。</summary>
        internal static SpriteAtlas EnsureAtlasAsset(PromptUGUI.Application.IconSet set)
        {
            if (set.Atlas != null) return set.Atlas;
            var setPath = AssetDatabase.GetAssetPath(set);
            if (string.IsNullOrEmpty(setPath))
            {
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
