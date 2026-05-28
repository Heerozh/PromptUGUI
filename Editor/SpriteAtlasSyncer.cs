using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using PromptUGUI.Application;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;
using UnityEditor;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.U2D;

namespace PromptUGUI.Editor
{
    public static class SpriteAtlasSyncer
    {
        private const string DynamicMarker = "{{";
        private const string ProgressTitle = "PromptUGUI Sprite Sync";

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
            var registry = UI.Registry;

            var guids = AssetDatabase.FindAssets("t:TextAsset");
            for (var i = 0; i < guids.Length; i++)
            {
                var guid = guids[i];
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".ui.xml", StringComparison.Ordinal))
                {
                    // Common foot-gun: PromptUGUI doc named `.xml` (single suffix)
                    // is silently skipped by the .ui.xml filter, so any Template /
                    // <Icon> in it never makes it into Pass A / Pass B. Sniff root
                    // element; if it's <PromptUGUI>, warn so the author renames.
                    if (path.EndsWith(".xml", StringComparison.Ordinal))
                        WarnIfMisnamedPromptUGUIDoc(path);
                    continue;
                }
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
                    Debug.LogWarning($"[SpriteSync] cannot read {path}: {ex.Message}");
                    continue;
                }
                UIDocument doc;
                try { doc = UIDocumentParser.Parse(text); }
                catch (ParseException ex)
                {
                    Debug.LogWarning($"[SpriteSync] skipping malformed {path}: {ex.Message}");
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
                    AnalyzeNode(tpl.Body, flows, path, tpl.Name, registry);
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
                    CollectFromNode(screen.Root, refs, templateFlows, path, registry);
                foreach (var tpl in doc.Templates.Values)
                {
                    if (tpl.Body == null) continue;
                    CollectFromNode(tpl.Body, refs, templateFlows, path, registry);
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

        // XmlReaderSettings shared across calls — Closing it after each use is fine,
        // settings are immutable. Comments / whitespace / processing instructions are
        // skipped so we land on the first real Element node and stop.
        private static readonly XmlReaderSettings MisnamedSniffSettings = new()
        {
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = true,
            DtdProcessing = DtdProcessing.Ignore,
        };

        private static void WarnIfMisnamedPromptUGUIDoc(string path)
        {
            try
            {
                using var reader = XmlReader.Create(path, MisnamedSniffSettings);
                while (reader.Read())
                {
                    if (reader.NodeType != XmlNodeType.Element) continue;
                    if (reader.Name == "PromptUGUI")
                    {
                        Debug.LogWarning(
                            $"[SpriteSync] '{path}' looks like a PromptUGUI document but is " +
                            $"named '.xml' (single suffix). Sprite sync only scans '.ui.xml' " +
                            $"files, so any <Icon>/sprite= refs in this file (including any " +
                            $"Template body that's invoked elsewhere) will be missed. " +
                            $"Rename to '.ui.xml'.");
                    }
                    return; // first element decides; stop reading either way
                }
            }
            catch (XmlException) { /* not valid XML — silently ignore */ }
            catch (IOException) { /* unreadable — silently ignore */ }
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

        private static void AnalyzeNode(ElementNode node,
                                    Dictionary<string, IconParamFlow> flows,
                                    string path, string tplName,
                                    ControlRegistry registry)
        {
            if (node == null) return;
            // Sprite-bearing attribute names for this tag come from
            // [UIAttr(IsSprite = true)] metadata via the registry; namespaced /
            // unregistered tags (custom controls not registered at Editor scan time)
            // fall back to the conventional "sprite" attribute name for back-compat.
            foreach (var attrName in SpriteAttrsFor(node, registry))
            {
                if (node.Attributes.TryGetValue(attrName, out var v))
                    TryAddFlow(v, flows, path, tplName, node.Tag, attrName);
                if (node.VariantOverrides.TryGetValue(attrName, out var list))
                    foreach (var (_, vv) in list)
                        TryAddFlow(vv, flows, path, tplName, node.Tag, attrName);
            }

            foreach (var c in node.Children) AnalyzeNode(c, flows, path, tplName, registry);
        }

        private static void TryAddFlow(string value,
                                Dictionary<string, IconParamFlow> flows,
                                string path, string tplName,
                                string elementTag, string attrName)
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
                $"[SpriteSync] {path}: <Template name='{tplName}'>: <{elementTag} {attrName}='{value}'> " +
                $"uses a non-trivial substitution; only `{{x}}` and `set:{{x}}` are " +
                $"statically analyzable. List candidates in SpriteSet.alwaysInclude.");
        }

        private static void CollectFromNode(ElementNode node,
                                    HashSet<(string, string)> refs,
                                    IReadOnlyDictionary<string, TemplateFlow> templateFlows,
                                    string path,
                                    ControlRegistry registry)
        {
            if (node == null) return;

            // Template invocations: resolve invocation args via Param flows. templateFlows
            // is keyed by local name only, so namespaced invocations (<lib.Themed .../>)
            // also resolve here. Both paths can fire — invocation args feed flow-driven
            // refs, and a stray literal sprite= on the same node feeds the registry-driven
            // path below; refs is a set so duplicates collapse.
            if (templateFlows.TryGetValue(node.Tag, out var tf))
            {
                foreach (var (paramName, flow) in tf.Flows)
                {
                    if (!node.Attributes.TryGetValue(paramName, out var arg) ||
                        string.IsNullOrEmpty(arg))
                        continue;
                    CollectFromTemplateArg(arg, flow, refs, path, node.Tag, paramName);
                }
            }

            // Sprite-bearing attribute names come from [UIAttr(IsSprite = true)] metadata
            // via the registry — covers Image / Btn / Toggle / Slider / Dropdown /
            // ScrollList / InputField (sprite=), Icon (name=), Progress (fill / bg /
            // frame / mask =), plus any future control. Namespaced or unregistered tags
            // (custom controls not registered at Editor scan time) fall back to the
            // conventional "sprite" attribute name for back-compat. Bare paths (no colon)
            // are Resources.Load — CollectFromAttr returns early.
            foreach (var attrName in SpriteAttrsFor(node, registry))
            {
                if (node.Attributes.TryGetValue(attrName, out var v))
                    CollectFromAttr(v, refs, path, node.Tag, attrName);
                if (node.VariantOverrides.TryGetValue(attrName, out var list))
                    foreach (var (_, vv) in list)
                        CollectFromAttr(vv, refs, path, node.Tag, attrName);
            }

            foreach (var c in node.Children) CollectFromNode(c, refs, templateFlows, path, registry);
        }

        // Per-tag sprite attribute names, taken from [UIAttr(IsSprite = true)] via the
        // registry. Namespaced tags (always Templates via Imports) and unregistered tags
        // (custom controls not registered at Editor scan time) fall back to the literal
        // "sprite" attribute, preserving back-compat.
        private static readonly string[] FallbackSpriteAttrs = { "sprite" };

        private static IReadOnlyCollection<string> SpriteAttrsFor(
            ElementNode node, ControlRegistry registry)
        {
            if (node.Namespace == null && registry != null && registry.Has(node.Tag))
                return registry.Resolve(node.Tag).Meta.SpriteAttrs;
            return FallbackSpriteAttrs;
        }

        private static void CollectFromTemplateArg(string value, IconParamFlow flow,
                                            HashSet<(string, string)> refs, string path,
                                            string tplName, string paramName)
        {
            if (string.IsNullOrEmpty(value)) return;
            if (value.Contains(DynamicMarker))
            {
                Debug.LogWarning(
                    $"[SpriteSync] {path}: <{tplName} {paramName}='{value}'>: arg is " +
                    $"itself a placeholder (forwarded from outer Param); cannot " +
                    $"analyze further. List final values in SpriteSet.alwaysInclude.");
                return;
            }
            if (flow.LiteralSet == null)
            {
                var colon = value.IndexOf(':');
                if (colon <= 0 || colon == value.Length - 1)
                {
                    Debug.LogWarning(
                        $"[SpriteSync] {path}: <{tplName} {paramName}='{value}'>: " +
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
                                    HashSet<(string, string)> refs, string path,
                                    string elementTag, string attrName)
        {
            if (string.IsNullOrEmpty(value)) return;
            var colon = value.IndexOf(':');
            if (colon <= 0 || colon == value.Length - 1) return;
            var ns = value.Substring(0, colon);
            var name = value.Substring(colon + 1);
            if (ns.Contains(DynamicMarker))
            {
                Debug.LogWarning(
                    $"[SpriteSync] {path}: <{elementTag} {attrName}='{value}'>: dynamic namespace " +
                    $"({DynamicMarker}...) is not analyzable; skipping");
                return;
            }
            if (name.Contains(DynamicMarker))
            {
                Debug.LogWarning(
                    $"[SpriteSync] {path}: <{elementTag} {attrName}='{value}'>: dynamic name " +
                    $"({DynamicMarker}...); list candidates in SpriteSet.alwaysInclude");
                return;
            }
            refs.Add((ns, name));
        }

        /// <summary>Cheap recursive count of sprite source assets (any Unity-recognized
        /// texture format + Aseprite single-sprite files) under a folder. No asset
        /// loading, no path resolution, no importer mutation — safe to call from
        /// OnInspectorGUI.</summary>
        public static int CountSpriteSources(string folderAssetPath)
        {
            if (string.IsNullOrEmpty(folderAssetPath)) return 0;
            if (!AssetDatabase.IsValidFolder(folderAssetPath)) return 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var folders = new[] { folderAssetPath };
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", folders)) seen.Add(g);
            foreach (var g in AssetDatabase.FindAssets("t:Sprite", folders)) seen.Add(g);
            return seen.Count;
        }

        /// <summary>每个 sprite source 一个 entry，pathKey = sourceFolder 下的相对路径（'/' 分隔、
        /// 去扩展名）。Root file 的 pathKey 等于裸文件名；子目录文件形如 "UI/heart"。
        /// 不再 first-wins —— 同名 sprite source 在不同子目录下都会各自出现，由 <see cref="SyncAll"/>
        /// 决定如何引用（路径形 vs. 裸名别名）。Triggers sprite reimport on first encounter.
        /// </summary>
        /// <param name="progressLabel">When non-null, drives a cancelable progress bar
        /// and throws <see cref="OperationCanceledException"/> if the user cancels.</param>
        public static List<(string pathKey, Sprite sprite)> EnumerateSpriteSources(
            string folderAssetPath, string progressLabel = null)
        {
            var result = new List<(string, Sprite)>();
            if (string.IsNullOrEmpty(folderAssetPath)) return result;
            if (!AssetDatabase.IsValidFolder(folderAssetPath))
            {
                Debug.LogError($"[SpriteSync] not a folder: '{folderAssetPath}'");
                return result;
            }

            var paths = EnumerateSpriteSourceGuids(folderAssetPath);
            var folderPrefix = folderAssetPath.EndsWith("/")
                ? folderAssetPath
                : folderAssetPath + "/";
            for (var i = 0; i < paths.Length; i++)
            {
                var assetPath = paths[i];
                if (progressLabel != null &&
                    EditorUtility.DisplayCancelableProgressBar(
                        ProgressTitle,
                        $"{progressLabel}: {Path.GetFileName(assetPath)} ({i + 1}/{paths.Length})",
                        (float)i / Mathf.Max(1, paths.Length)))
                {
                    throw new OperationCanceledException();
                }
                EnsureSpriteImporter(assetPath);
#if PROMPTUGUI_HAS_ASEPRITE
                // MF-D4: multi-sprite Aseprite is rejected at validation time; skip it
                // here so a stray first-sprite doesn't sneak into the SpriteSet via
                // LoadAssetAtPath<Sprite>'s arbitrary-first behavior. Note: this
                // LoadAllAssetsAtPath fires for EVERY Aseprite file (single- and
                // multi-sprite) — EnsureSpriteImporter above already paid the same
                // cost. The duplication is accepted per plan §5.5; refactor to a
                // single call if profiling shows it's hot.
                if (AssetImporter.GetAtPath(assetPath) is UnityEditor.U2D.Aseprite.AsepriteImporter)
                {
                    if (AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().Count() != 1)
                        continue;
                }
#endif
                var sp = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
                if (sp == null) continue;
                var rel = assetPath.Substring(folderPrefix.Length);
                var ext = Path.GetExtension(rel);
                var pathKey = rel.Substring(0, rel.Length - ext.Length);
                result.Add((pathKey, sp));
            }
            return result;
        }

        // Returns asset paths sorted ordinally for stable downstream output.
        // MF-D1: union t:Texture2D (covers PNG/JPG/.../Aseprite-SpriteSheet + PNG-Default-mode
        // for EnsureSpriteImporter auto-flip) with t:Sprite (covers Aseprite-AnimatedSprite
        // where the main asset is Sprite rather than Texture2D). HashSet by GUID dedupes
        // the overlap (eg. PNG-as-Sprite hits both filters).
        private static string[] EnumerateSpriteSourceGuids(string folderAssetPath)
        {
            var folders = new[] { folderAssetPath };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in AssetDatabase.FindAssets("t:Texture2D", folders)) seen.Add(g);
            foreach (var g in AssetDatabase.FindAssets("t:Sprite", folders)) seen.Add(g);
            var paths = new string[seen.Count];
            var idx = 0;
            foreach (var g in seen) paths[idx++] = AssetDatabase.GUIDToAssetPath(g);
            Array.Sort(paths, StringComparer.Ordinal);
            return paths;
        }

        /// <summary>从 EnumerateSpriteSources 结果建一个统一的查找表：pathKey 总是可用；
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
            var importer = AssetImporter.GetAtPath(assetPath);
            if (importer is TextureImporter ti)
            {
                if (ti.textureType == TextureImporterType.Sprite) return;
                ti.textureType = TextureImporterType.Sprite;
                ti.spriteImportMode = SpriteImportMode.Single;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
                ti.SaveAndReimport();
                return;
            }
#if PROMPTUGUI_HAS_ASEPRITE
            if (importer is UnityEditor.U2D.Aseprite.AsepriteImporter)
            {
                // MF-D4: validate single-sprite contract; do not coerce AsepriteImporter
                // settings — layer/frame configuration is author intent.
                var sprites = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().Count();
                if (sprites != 1)
                {
                    Debug.LogError(
                        $"[SpriteSync] Aseprite '{assetPath}' produces {sprites} sprites; " +
                        $"SpriteSet requires exactly 1 sprite per file. Skipping. " +
                        $"Set the AsepriteImporter Import Mode to single-frame output, " +
                        $"or use a different file per icon.");
                }
                return;
            }
#endif
            // Other importer types (eg. SVG via com.unity.vectorgraphics) - silent skip.
        }

        /// <summary>Force every texture under <paramref name="folderAssetPath"/> to the
        /// canonical PromptUGUI format: textureType=Sprite, spriteImportMode=Single,
        /// textureCompression=Uncompressed. Overrides prior author-set TextureImporter
        /// values — explicit "reset" semantics, intended for the SpriteSet inspector
        /// "Reset All Textures Format" button. Returns the number of textures reimported.
        /// Wraps the loop in <see cref="AssetDatabase.StartAssetEditing"/> for batch
        /// throughput.</summary>
        /// <param name="showProgress">When true, drives a cancelable progress bar;
        /// throws <see cref="OperationCanceledException"/> if the user cancels.</param>
        public static int ResetTextureImportSettings(string folderAssetPath,
                                                bool showProgress = false)
        {
            if (string.IsNullOrEmpty(folderAssetPath)) return 0;
            if (!AssetDatabase.IsValidFolder(folderAssetPath))
            {
                Debug.LogError($"[SpriteSync] not a folder: '{folderAssetPath}'");
                return 0;
            }
            // MF-D1b: TextureImporter-only operation; AsepriteImporter resources would be
            // rejected by the importer-type guard anyway, so single t:Texture2D filter
            // is sufficient (and avoids enumerating Aseprite AnimatedSprite which is t:Sprite).
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderAssetPath });
            var paths = new string[guids.Length];
            for (var i = 0; i < guids.Length; i++) paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            Array.Sort(paths, StringComparer.Ordinal);

            var count = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (var i = 0; i < paths.Length; i++)
                {
                    var assetPath = paths[i];
                    if (showProgress &&
                        EditorUtility.DisplayCancelableProgressBar(
                            ProgressTitle,
                            $"Resetting import format: {Path.GetFileName(assetPath)} " +
                            $"({i + 1}/{paths.Length})",
                            (float)i / Mathf.Max(1, paths.Length)))
                    {
                        throw new OperationCanceledException();
                    }
                    if (AssetImporter.GetAtPath(assetPath) is not TextureImporter imp)
                        continue;
                    imp.textureType = TextureImporterType.Sprite;
                    imp.spriteImportMode = SpriteImportMode.Single;
                    imp.textureCompression = TextureImporterCompression.Uncompressed;
                    imp.SaveAndReimport();
                    count++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                if (showProgress) EditorUtility.ClearProgressBar();
            }
            return count;
        }

        /// <summary>Inspector "Apply to All Textures" entry: copy
        /// <paramref name="templateTextureAssetPath"/>'s TextureImporter onto every other
        /// texture under <paramref name="folderAssetPath"/> via
        /// <see cref="EditorUtility.CopySerialized(UnityEngine.Object,UnityEngine.Object)"/>.
        /// The template itself is skipped. Per SpriteSet contract every icon is a single
        /// sprite, so manual slicing data is not expected to leak between textures.
        /// Returns the number of non-template textures that received the settings copy.</summary>
        /// <param name="showProgress">When true, drives a cancelable progress bar;
        /// throws <see cref="OperationCanceledException"/> if the user cancels.</param>
        public static int ApplyImportSettingsToFolder(
            string templateTextureAssetPath,
            string folderAssetPath,
            bool showProgress = false)
        {
            if (string.IsNullOrEmpty(templateTextureAssetPath)) return 0;
            if (string.IsNullOrEmpty(folderAssetPath)) return 0;
            if (!AssetDatabase.IsValidFolder(folderAssetPath))
            {
                Debug.LogError($"[SpriteSync] not a folder: '{folderAssetPath}'");
                return 0;
            }
            if (AssetImporter.GetAtPath(templateTextureAssetPath) is not TextureImporter template)
            {
                Debug.LogError(
                    $"[SpriteSync] template is not a TextureImporter: '{templateTextureAssetPath}'");
                return 0;
            }
            // MF-D1b: TextureImporter-only, same reasoning as ResetTextureImportSettings.
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderAssetPath });
            var paths = new string[guids.Length];
            for (var i = 0; i < guids.Length; i++) paths[i] = AssetDatabase.GUIDToAssetPath(guids[i]);
            Array.Sort(paths, StringComparer.Ordinal);
            var count = 0;
            try
            {
                AssetDatabase.StartAssetEditing();
                for (var i = 0; i < paths.Length; i++)
                {
                    var assetPath = paths[i];
                    if (string.Equals(assetPath, templateTextureAssetPath, StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (showProgress &&
                        EditorUtility.DisplayCancelableProgressBar(
                            ProgressTitle,
                            $"Applying import settings: {Path.GetFileName(assetPath)} " +
                            $"({i + 1}/{paths.Length})",
                            (float)i / Mathf.Max(1, paths.Length)))
                    {
                        throw new OperationCanceledException();
                    }
                    if (AssetImporter.GetAtPath(assetPath) is not TextureImporter dst) continue;
                    EditorUtility.CopySerialized(template, dst);
                    dst.SaveAndReimport();
                    count++;
                }
            }
            finally
            {
                AssetDatabase.StopAssetEditing();
                if (showProgress) EditorUtility.ClearProgressBar();
            }
            return count;
        }

        /// <summary>Alphabetically-first <see cref="TextureImporter"/> asset under
        /// <paramref name="folderAssetPath"/> (recursive), as a project-relative
        /// "Assets/..." path. Returns null if the folder is missing or contains no
        /// TextureImporter assets. AsepriteImporter assets are excluded because
        /// AsepriteImporterEditor isn't designed for embedded hosting (NREs in
        /// HasModified when hosted inside SpriteSetEditor). Used by the SpriteSet
        /// inspector to pick a default template for the embedded TextureImporter editor
        /// — sorting keeps the choice stable across filesystem enumeration order
        /// changes.</summary>
        public static string FindFirstTexture(string folderAssetPath)
        {
            if (string.IsNullOrEmpty(folderAssetPath)) return null;
            if (!AssetDatabase.IsValidFolder(folderAssetPath)) return null;
            // MF-D1b: TextureImporter-only template (filterMode source + embedded
            // inspector template). Aseprite SpriteSheet mode produces a t:Texture2D
            // main asset too, so t:Texture2D alone matches Aseprite assets — filter
            // by importer type. Aseprite has no equivalent filterMode and its
            // AsepriteImporterEditor isn't designed to be hosted inside our custom
            // inspector (NREs in HasModified).
            var guids = AssetDatabase.FindAssets("t:Texture2D", new[] { folderAssetPath });
            if (guids.Length == 0) return null;
            var paths = new List<string>(guids.Length);
            for (var i = 0; i < guids.Length; i++)
            {
                var p = AssetDatabase.GUIDToAssetPath(guids[i]);
                if (AssetImporter.GetAtPath(p) is TextureImporter)
                    paths.Add(p);
            }
            if (paths.Count == 0) return null;
            paths.Sort(StringComparer.Ordinal);
            return paths[0];
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
                Debug.LogError($"[SpriteSync] failed to load V2 atlas at {path}");
                return false;
            }
            var so = new SerializedObject(v2);
            var prop = so.FindProperty(V2PackablesPath);
            if (prop == null || !prop.isArray)
            {
                Debug.LogError(
                    $"[SpriteSync] cannot find '{V2PackablesPath}' on V2 atlas at {path}; " +
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

        public static void SyncAll(IEnumerable<PromptUGUI.Application.SpriteSet> sets)
        {
            var setList = new List<PromptUGUI.Application.SpriteSet>(sets);
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
                        Debug.LogError($"[SpriteSync] SpriteSet '{s.name}' has empty setName");
                        return;
                    }
                    if (!seen.Add(s.SetName))
                    {
                        Debug.LogError(
                            $"[SpriteSync] duplicate SpriteSet setName '{s.SetName}'; aborting");
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
                        Debug.LogError($"[SpriteSync] SpriteSet '{set.SetName}': sourceFolder invalid");
                        continue;
                    }
                    var label = $"Set {i + 1}/{setList.Count} '{set.SetName}'";
                    var entries = EnumerateSpriteSources(folder, label);
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
                                $"[SpriteSync] '{set.SetName}': '{n}' is ambiguous; " +
                                $"use the explicit path form. Candidates: " +
                                string.Join(", ", candidates));
                            continue;
                        }
                        missing.Add(n);
                    }
                    if (missing.Count > 0)
                        Debug.LogWarning(
                            $"[SpriteSync] '{set.SetName}': XML references missing sprites: " +
                            string.Join(", ", missing));

                    if (EditorUtility.DisplayCancelableProgressBar(
                            ProgressTitle, $"{label}: packing atlas...",
                            (i + 0.9f) / Mathf.Max(1, setList.Count)))
                    {
                        throw new OperationCanceledException();
                    }

                    // Persist the (key → Sprite) projection SpriteResolverHelpers reads at
                    // runtime: every key in `lookup` (pathKey + unique bare alias) that
                    // resolves to a picked sprite gets one entry on the SpriteSet.
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
                Debug.LogWarning("[SpriteSync] cancelled by user");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        public static IEnumerable<PromptUGUI.Application.SpriteSet> FindAllSpriteSets()
        {
            var guids = AssetDatabase.FindAssets("t:" + nameof(PromptUGUI.Application.SpriteSet));
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var s = AssetDatabase.LoadAssetAtPath<PromptUGUI.Application.SpriteSet>(path);
                if (s != null) yield return s;
            }
        }

        /// <summary>若 SpriteSet.atlas 为 null，在 SO 同目录创建 &lt;setName&gt;.spriteatlas 并回填。
        /// 新建 atlas 的 FilterMode 沿用 sourceFolder 下首个 texture 的 TextureImporter.filterMode，
        /// 这样像素美术 (Point) 与一般贴图 (Bilinear) 在 atlas 上不会被默认值覆盖。</summary>
        internal static SpriteAtlas EnsureAtlasAsset(PromptUGUI.Application.SpriteSet set)
        {
            if (set.Atlas != null) return set.Atlas;
            var setPath = AssetDatabase.GetAssetPath(set);
            if (string.IsNullOrEmpty(setPath))
            {
                Debug.LogError("[SpriteSync] SpriteSet not saved as asset; cannot create atlas");
                return null;
            }
            var dir = Path.GetDirectoryName(setPath).Replace('\\', '/');
            var atlasPath = $"{dir}/{set.SetName}.spriteatlas";
            var atlas = new SpriteAtlas();
            AssetDatabase.CreateAsset(atlas, atlasPath);
            ApplyTemplateFilterMode(atlas, set.SourceFolderPath);
            set.SetAtlasInternal(atlas);
            AssetDatabase.SaveAssets();
            return atlas;
        }

        private static void ApplyTemplateFilterMode(SpriteAtlas atlas, string folderAssetPath)
        {
            var firstTexture = FindFirstTexture(folderAssetPath);
            if (firstTexture == null) return;
            if (AssetImporter.GetAtPath(firstTexture) is not TextureImporter ti) return;
            var ts = atlas.GetTextureSettings();
            ts.filterMode = ti.filterMode;
            atlas.SetTextureSettings(ts);
        }
    }
}
