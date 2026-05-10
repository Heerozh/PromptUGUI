using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
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

        /// <summary>(setName, iconName) pairs found across all .ui.xml in the project.
        /// Two passes: (A) build Template Param-flow map across all docs, (B) walk each
        /// doc collecting literal &lt;Icon&gt; refs plus refs derived from Template
        /// invocations whose attributes feed an &lt;Icon name&gt; in the Template body.</summary>
        /// <param name="showProgress">When true, drives a cancelable progress bar; throws
        /// <see cref="OperationCanceledException"/> if the user cancels.</param>
        public static HashSet<(string set, string name)> ScanXmlReferences(
            bool showProgress = false)
        {
            var refs = new HashSet<(string, string)>();
            var parsed = new List<(string path, UIDocument doc)>();

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
                parsed.Add((path, doc));
            }

            // Pass A: cross-doc Template Param-flow map.
            // Templates may live in commons (one file) and be invoked in screens (another).
            // Key by Template local name only — Imports' `as` alias propagates only at
            // expansion time, but the Param-flow shape is identical regardless of alias.
            var templateFlows = new Dictionary<string, TemplateFlow>(StringComparer.Ordinal);
            foreach (var (path, doc) in parsed)
            {
                foreach (var tpl in doc.Templates.Values)
                {
                    if (tpl.Body == null) continue;
                    var flows = new Dictionary<string, IconParamFlow>(StringComparer.Ordinal);
                    AnalyzeIconNode(tpl.Body, flows, path, tpl.Name);
                    if (flows.Count == 0) continue;

                    // Treat Param `default` values as effective invocation args so a
                    // bare `<MyIcon/>` invocation (no explicit arg) still pre-packs.
                    var defaults = new Dictionary<string, string>(StringComparer.Ordinal);
                    foreach (var p in tpl.Params)
                        if (!string.IsNullOrEmpty(p.DefaultValue)) defaults[p.Name] = p.DefaultValue;

                    templateFlows[tpl.Name] = new TemplateFlow(flows, defaults);
                }
            }

            // Pass B: literal <Icon> + Template-Param-driven refs.
            foreach (var (path, doc) in parsed)
            {
                foreach (var screen in doc.Screens)
                    CollectFromNode(screen.Root, refs, templateFlows, path);
                foreach (var tpl in doc.Templates.Values)
                {
                    if (tpl.Body == null) continue;
                    CollectFromNode(tpl.Body, refs, templateFlows, path);
                    // Also fold Param defaults into refs at definition site (covers the
                    // case where a Template is defined but never invoked yet still ships
                    // a sensible default icon).
                    if (templateFlows.TryGetValue(tpl.Name, out var tf))
                    {
                        foreach (var (paramName, flow) in tf.Flows)
                        {
                            if (!tf.Defaults.TryGetValue(paramName, out var def)) continue;
                            CollectFromTemplateArg(def, flow, refs, path, tpl.Name, paramName);
                        }
                    }
                }
            }
            return refs;
        }

        private readonly struct TemplateFlow
        {
            public readonly Dictionary<string, IconParamFlow> Flows;
            public readonly Dictionary<string, string> Defaults;
            public TemplateFlow(Dictionary<string, IconParamFlow> flows,
                                Dictionary<string, string> defaults)
            { Flows = flows; Defaults = defaults; }
        }

        // If LiteralSet is non-null, the body has `set:{{param}}` and the invocation
        // arg is just the icon-name half. If null, the body has `{{param}}` and the
        // invocation arg is the full `set:icon`.
        private readonly struct IconParamFlow
        {
            public readonly string LiteralSet;
            public IconParamFlow(string literalSet) { LiteralSet = literalSet; }
        }

        private static readonly Regex FullPlaceholder =
            new(@"^\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}$", RegexOptions.Compiled);
        private static readonly Regex PartialPlaceholder =
            new(@"^([A-Za-z0-9_\-]+):\{\{\s*([A-Za-z_][A-Za-z0-9_]*)\s*\}\}$",
                RegexOptions.Compiled);

        private static void AnalyzeIconNode(ElementNode node,
                                    Dictionary<string, IconParamFlow> flows,
                                    string path, string tplName)
        {
            if (node == null) return;
            if (node.Tag == "Icon" && node.Namespace == null)
            {
                if (node.Attributes.TryGetValue("name", out var v))
                    TryAddFlow(v, flows, path, tplName);
                if (node.VariantOverrides.TryGetValue("name", out var list))
                    foreach (var (_, vv) in list) TryAddFlow(vv, flows, path, tplName);
            }
            foreach (var c in node.Children) AnalyzeIconNode(c, flows, path, tplName);
        }

        private static void TryAddFlow(string value,
                                Dictionary<string, IconParamFlow> flows,
                                string path, string tplName)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (!value.Contains(DynamicMarker)) return; // literal — no flow

            var m = FullPlaceholder.Match(value);
            if (m.Success)
            {
                flows[m.Groups[1].Value] = new IconParamFlow(null);
                return;
            }
            m = PartialPlaceholder.Match(value);
            if (m.Success)
            {
                flows[m.Groups[2].Value] = new IconParamFlow(m.Groups[1].Value);
                return;
            }
            Debug.LogWarning(
                $"[IconSync] {path}: <Template name='{tplName}'>: <Icon name='{value}'> " +
                $"uses a non-trivial substitution; only `{{x}}` and `set:{{x}}` are " +
                $"statically analyzable. List candidates in IconSet.alwaysInclude.");
        }

        private static void CollectFromNode(ElementNode node,
                                    HashSet<(string, string)> refs,
                                    IReadOnlyDictionary<string, TemplateFlow> templateFlows,
                                    string path)
        {
            if (node == null) return;
            if (node.Tag == "Icon" && node.Namespace == null)
            {
                CollectFromAttr(node.Attributes.TryGetValue("name", out var n) ? n : null,
                                refs, path);
                if (node.VariantOverrides.TryGetValue("name", out var list))
                    foreach (var (_, v) in list) CollectFromAttr(v, refs, path);
            }
            else if (templateFlows.TryGetValue(node.Tag, out var tf))
            {
                foreach (var (paramName, flow) in tf.Flows)
                {
                    if (!node.Attributes.TryGetValue(paramName, out var arg) ||
                        string.IsNullOrEmpty(arg))
                        continue;
                    CollectFromTemplateArg(arg, flow, refs, path, node.Tag, paramName);
                }
            }
            foreach (var c in node.Children) CollectFromNode(c, refs, templateFlows, path);
        }

        private static void CollectFromTemplateArg(string value, IconParamFlow flow,
                                            HashSet<(string, string)> refs, string path,
                                            string tplName, string paramName)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (value.Contains(DynamicMarker))
            {
                Debug.LogWarning(
                    $"[IconSync] {path}: <{tplName} {paramName}='{value}'>: arg is " +
                    $"itself a placeholder (forwarded from outer Param); cannot " +
                    $"analyze further. List final values in IconSet.alwaysInclude.");
                return;
            }
            if (flow.LiteralSet == null)
            {
                var colon = value.IndexOf(':');
                if (colon <= 0 || colon == value.Length - 1)
                {
                    Debug.LogWarning(
                        $"[IconSync] {path}: <{tplName} {paramName}='{value}'>: " +
                        $"expected 'set:icon' form; ignoring.");
                    return;
                }
                refs.Add((value.Substring(0, colon), value.Substring(colon + 1)));
            }
            else
            {
                refs.Add((flow.LiteralSet, value));
            }
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

        /// <summary>每个 PNG 一个 entry，pathKey = sourceFolder 下的相对路径（'/' 分隔、
        /// 去扩展名）。Root file 的 pathKey 等于裸文件名；子目录文件形如 "UI/heart"。
        /// 不再 first-wins —— 同名 PNG 在不同子目录下都会各自出现，由 <see cref="SyncAll"/>
        /// 决定如何引用（路径形 vs. 裸名别名）。Triggers sprite reimport on first encounter.
        /// </summary>
        /// <param name="progressLabel">When non-null, drives a cancelable progress bar
        /// and throws <see cref="OperationCanceledException"/> if the user cancels.</param>
        public static List<(string pathKey, Sprite sprite)> EnumeratePngs(
            string folderAssetPath, string progressLabel = null)
        {
            var result = new List<(string, Sprite)>();
            if (string.IsNullOrEmpty(folderAssetPath)) return result;
            if (!AssetDatabase.IsValidFolder(folderAssetPath))
            {
                Debug.LogError($"[IconSync] not a folder: '{folderAssetPath}'");
                return result;
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
                var rel = fullPath.Substring(fullFolder.Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                    .Replace('\\', '/');
                var ext = Path.GetExtension(rel);
                var pathKey = rel.Substring(0, rel.Length - ext.Length);
                result.Add((pathKey, sp));
            }
            return result;
        }

        /// <summary>从 EnumeratePngs 结果建一个统一的查找表：pathKey 总是可用；
        /// 当某个裸名（最后一段文件名）在整个表中唯一时，也可以裸名作为别名引用。
        /// 裸名冲突时不写入裸名 → 引用方必须用路径形。</summary>
        internal static Dictionary<string, Sprite> BuildLookup(
            IList<(string pathKey, Sprite sprite)> entries,
            out Dictionary<string, List<string>> bareCandidates)
        {
            var lookup = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            bareCandidates = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (var (pathKey, sprite) in entries)
            {
                lookup[pathKey] = sprite;
                var slash = pathKey.LastIndexOf('/');
                if (slash < 0) continue; // root file: pathKey IS bare name
                var bare = pathKey.Substring(slash + 1);
                if (!bareCandidates.TryGetValue(bare, out var list))
                {
                    list = new List<string>();
                    bareCandidates[bare] = list;
                }
                list.Add(pathKey);
            }
            // Promote bare → sprite ONLY when unambiguous (single candidate) AND
            // bare doesn't already collide with an existing pathKey (root file with
            // same basename always wins via the earlier lookup[pathKey] = sprite).
            foreach (var kv in bareCandidates)
            {
                var bare = kv.Key;
                var candidates = kv.Value;
                if (candidates.Count != 1) continue;
                if (lookup.ContainsKey(bare)) continue; // root file occupies this key
                lookup[bare] = lookup[candidates[0]];
            }
            return lookup;
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
                    var entries = EnumeratePngs(folder, label);
                    var lookup = BuildLookup(entries, out var bareCandidates);

                    var needed = new HashSet<string>();
                    foreach (var (ns, name) in refs)
                        if (ns == set.SetName) needed.Add(name);
                    foreach (var n in set.AlwaysInclude)
                        if (!string.IsNullOrEmpty(n)) needed.Add(n);

                    var picked = new HashSet<Sprite>();
                    var missing = new List<string>();
                    foreach (var n in needed)
                    {
                        if (lookup.TryGetValue(n, out var sp)) { picked.Add(sp); continue; }
                        // Bare-name reference where multiple subfolders contain a PNG with
                        // that basename — author must disambiguate via path form.
                        if (bareCandidates.TryGetValue(n, out var candidates) &&
                            candidates.Count > 1)
                        {
                            Debug.LogError(
                                $"[IconSync] '{set.SetName}': '{n}' is ambiguous; " +
                                $"use the explicit path form. Candidates: " +
                                string.Join(", ", candidates));
                            continue;
                        }
                        missing.Add(n);
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

                    // Persist the (key → Sprite) projection IconResolverHelpers reads at
                    // runtime: every key in `lookup` (pathKey + unique bare alias) that
                    // resolves to a picked sprite gets one entry on the IconSet.
                    var iconSetEntries = new List<(string key, Sprite sprite)>();
                    foreach (var kv in lookup)
                    {
                        if (!picked.Contains(kv.Value)) continue;
                        iconSetEntries.Add((kv.Key, kv.Value));
                    }
                    set.SetEntriesInternal(iconSetEntries);

                    var atlas = EnsureAtlasAsset(set);
                    if (atlas == null) continue;
                    var pickedArr = new Sprite[picked.Count];
                    var pi = 0;
                    foreach (var sp in picked) pickedArr[pi++] = sp;
                    UpdateAtlas(atlas, pickedArr);
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
