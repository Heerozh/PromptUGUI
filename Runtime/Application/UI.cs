using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;

namespace PromptUGUI.Application {
    public static partial class UI {
        static ControlRegistry _registry = CreateRegistryWithBuiltins();
        static readonly Dictionary<string, ScreenDef> _docs = new();
        static readonly Dictionary<string, Screen> _open = new();
        static readonly VariantStore _variantStore = new();
        static readonly System.Collections.Generic.Dictionary<DocumentLoader.TemplateKey, IR.TemplateDef> _commonsPool = new();
        static readonly DepGraph _depGraph = new();

        public static System.Func<string, string> SourceResolver { get; set; }
        public static System.Func<string, UnityEngine.Sprite> IconResolver { get; set; }

        // Invoked from Screen.Open() right after the Canvas + CanvasScaler + GraphicRaycaster
        // are added and renderMode is set to ScreenSpaceOverlay. Use to switch renderMode,
        // assign worldCamera, set sortingOrder, etc. Per-Screen behavior keys off the second arg.
        public static System.Action<UnityEngine.Canvas, string> CanvasConfigurator { get; set; }

        public static ControlRegistry Registry => _registry;

        internal static VariantStore VariantStore => _variantStore;

        public static class Variants {
            public static void Set(string name, bool active) =>
                _variantStore.Set(name, active);
            public static bool IsActive(string name) =>
                _variantStore.IsActive(name);
        }

        public static void LoadDocument(string label, string xml) {
            var raw = UIDocumentParser.Parse(xml);
            var doc = PromptUGUI.Template.TemplateExpander.Expand(raw);
            foreach (var s in doc.Screens) {
                if (_docs.ContainsKey(s.Name))
                    throw new System.InvalidOperationException(
                        $"Screen '{s.Name}' already loaded");
                _docs[s.Name] = s;
            }
        }

        public static IReadOnlyList<string> LoadDocumentFromSrc(string src) {
            if (SourceResolver == null)
                throw new System.InvalidOperationException(
                    "UI.SourceResolver must be set before LoadDocumentFromSrc");

            var loaded = DocumentLoader.LoadAndMerge(src, SourceResolver, _commonsPool);

            var expanded = PromptUGUI.Template.TemplateExpander.Expand(loaded);

            var added = new List<string>();
            foreach (var s in expanded.Screens) {
                if (_docs.ContainsKey(s.Name))
                    throw new System.InvalidOperationException(
                        $"Screen '{s.Name}' already loaded");
                _docs[s.Name] = s;
                added.Add(s.Name);
                _depGraph.ScreenDeps[s.Name] = new DepGraph.ScreenDep {
                    EntrySrc = src,
                    AllDeps = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs),
                };
            }
            _depGraph.SrcToDeps[src] = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs);
            return added;
        }

        public static void Reload(string screenName) {
            if (!_depGraph.ScreenDeps.TryGetValue(screenName, out var dep))
                throw new System.InvalidOperationException(
                    $"Screen '{screenName}' was not loaded by src; cannot reload " +
                    $"(use LoadDocumentFromSrc instead of LoadDocument(label, xml))");

            if (SourceResolver == null)
                throw new System.InvalidOperationException(
                    "UI.SourceResolver must be set before Reload");

            // 1) Parse + Expand FIRST. Failure here → throw, leave state intact.
            var loaded = DocumentLoader.LoadAndMerge(dep.EntrySrc, SourceResolver, _commonsPool);
            var expanded = PromptUGUI.Template.TemplateExpander.Expand(loaded);

            PromptUGUI.IR.ScreenDef newDef = null;
            foreach (var s in expanded.Screens) {
                if (s.Name == screenName) { newDef = s; break; }
            }
            if (newDef == null)
                throw new System.InvalidOperationException(
                    $"Screen '{screenName}' no longer present in src='{dep.EntrySrc}' after reload");

            // 2) Tear down old (after parse succeeded)
            bool wasOpen = _open.ContainsKey(screenName);
            if (wasOpen) Close(screenName);

            // 3) Replace docs + dep entries
            _docs.Remove(screenName);
            _depGraph.ScreenDeps.Remove(screenName);

            _docs[screenName] = newDef;
            _depGraph.ScreenDeps[screenName] = new DepGraph.ScreenDep {
                EntrySrc = dep.EntrySrc,
                AllDeps = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs),
            };
            _depGraph.SrcToDeps[dep.EntrySrc] = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs);

            // 4) Re-open if it was open
            if (wasOpen) Open(screenName);
        }

        public static void LoadCommonLibrary(string src, string @as = null) {
            if (SourceResolver == null)
                throw new System.InvalidOperationException(
                    "UI.SourceResolver must be set before LoadCommonLibrary");

            var loaded = DocumentLoader.Load(src, SourceResolver, allowScreens: false);

            // Conflict-check FIRST so we don't pollute on failure.
            var staged = new System.Collections.Generic.List<(DocumentLoader.TemplateKey Key, IR.TemplateDef Def)>();
            foreach (var kv in loaded.Templates) {
                var rebasedKey = @as == null
                    ? kv.Key
                    : new DocumentLoader.TemplateKey(@as, kv.Key.Name);
                if (_commonsPool.ContainsKey(rebasedKey))
                    throw new PromptUGUI.Template.TemplateException(
                        $"common library conflict: '{rebasedKey}' already in commons pool");
                staged.Add((rebasedKey, kv.Value));
            }

            foreach (var (key, def) in staged) {
                def.OriginSrc = src;
                _commonsPool[key] = def;
            }
            _depGraph.CommonsSources.Add(src);
            _depGraph.SrcToDeps[src] = new System.Collections.Generic.HashSet<string>(loaded.AllSrcs);
        }

        public static void ReloadCommonLibrary(string src) {
            if (!_depGraph.CommonsSources.Contains(src))
                throw new System.InvalidOperationException(
                    $"src='{src}' is not a registered common library");

            if (SourceResolver == null)
                throw new System.InvalidOperationException(
                    "UI.SourceResolver must be set before ReloadCommonLibrary");

            // Stash existing commons entries that came from this src (rollback if reload fails)
            // M4 v1 limitation: original `as=` namespace is not preserved across reload.
            // If a commons was loaded with as="ns", reload may fail with conflicts; users should
            // UnloadAll + re-bootstrap in that case. Spec §15 R-? for follow-up.
            var stashed = new System.Collections.Generic.List<
                System.Collections.Generic.KeyValuePair<DocumentLoader.TemplateKey, IR.TemplateDef>>();
            foreach (var kv in _commonsPool)
                if (kv.Value.OriginSrc == src) stashed.Add(kv);
            foreach (var kv in stashed) _commonsPool.Remove(kv.Key);

            var prevDeps = _depGraph.SrcToDeps.TryGetValue(src, out var d)
                ? new System.Collections.Generic.HashSet<string>(d) : null;
            _depGraph.CommonsSources.Remove(src);
            _depGraph.SrcToDeps.Remove(src);

            try {
                LoadCommonLibrary(src);
            } catch {
                // Roll back commons pool + depGraph state
                foreach (var kv in stashed) _commonsPool[kv.Key] = kv.Value;
                _depGraph.CommonsSources.Add(src);
                if (prevDeps != null) _depGraph.SrcToDeps[src] = prevDeps;
                throw;
            }

            // M4 v1 simplification: reload ALL screens (commons changes blast radius is global)
            var names = new System.Collections.Generic.List<string>(_depGraph.ScreenDeps.Keys);
            foreach (var name in names) Reload(name);
        }

        public static Screen Open(string screenName) {
            if (_open.TryGetValue(screenName, out var existing)) return existing;
            if (!_docs.TryGetValue(screenName, out var def))
                throw new System.InvalidOperationException(
                    $"Screen '{screenName}' not loaded; call LoadDocument first");

            var inst = new ScreenInstantiator(_registry, _variantStore);
            var screen = new Screen(def, inst, _registry, _variantStore);
            screen.Open();
            _open[screenName] = screen;
            return screen;
        }

        public static void Close(string screenName) {
            if (_open.TryGetValue(screenName, out var s)) {
                s.Close();
                _open.Remove(screenName);
            }
        }

        public static Screen Get(string screenName) =>
            _open.TryGetValue(screenName, out var s) ? s : null;

        /// <summary>
        /// Clears all commons-pool entries and dep-graph commons sources.
        /// Loaded Screens, depGraph.ScreenDeps, SourceResolver, Registry are preserved.
        /// Use when re-bootstrapping commons (e.g., to swap as= namespace).
        /// </summary>
        public static void UnloadAllCommonLibraries() {
            _commonsPool.Clear();
            _depGraph.CommonsSources.Clear();
            // Remove commons srcs from _srcToDeps; leave screen-related entries intact.
            var commonsSrcs = new System.Collections.Generic.List<string>();
            foreach (var src in _depGraph.SrcToDeps.Keys) {
                bool stillUsedByScreen = false;
                foreach (var sd in _depGraph.ScreenDeps.Values) {
                    if (sd.AllDeps.Contains(src)) { stillUsedByScreen = true; break; }
                }
                if (!stillUsedByScreen) commonsSrcs.Add(src);
            }
            foreach (var s in commonsSrcs) _depGraph.SrcToDeps.Remove(s);
        }

        /// <summary>
        /// Clears all loaded state — commons + Screens + open + dep graph.
        /// Preserves SourceResolver, HotReload.AssetPathToSrc (Editor), and Registry.
        /// </summary>
        public static void UnloadAll() {
            foreach (var s in _open.Values) s.Close();
            _open.Clear();
            _docs.Clear();
            _commonsPool.Clear();
            _depGraph.Clear();
        }

        // 仅测试使用
        internal static void ResetForTests() {
            foreach (var s in _open.Values) s.Close();
            _open.Clear();
            _docs.Clear();
            _variantStore.Reset();
            _registry = CreateRegistryWithBuiltins();
            _commonsPool.Clear();
            _depGraph.Clear();
            SourceResolver = null;
            IconResolver = null;
            CanvasConfigurator = null;
#if UNITY_EDITOR
            HotReload.AssetPathToSrc = null;
            HotReload.IconResolverRebuilder = null;
            HotReload.Enabled = true;
#endif
        }

        static ControlRegistry CreateRegistryWithBuiltins() {
            var r = new ControlRegistry();
            BuiltinPrimitives.Register(r);
            return r;
        }

        // Clears stale Screens/docs/commons/dep-graph that survive Play→Stop→Play
        // when "Reload Domain" is disabled in Enter Play Mode Options. SourceResolver,
        // IconResolver and Registry (with built-ins) are intentionally preserved.
        [UnityEngine.OnEnteringPlayMode]
        static void OnEnteringPlayMode() => UnloadAll();

        // Symmetric cleanup on play exit. Without this, Screens whose GameObjects
        // Unity tears down still sit in _open; later Editor work (e.g. icon sync's
        // ReSolve broadcast) walks them and hits destroyed RectTransforms.
        [UnityEngine.OnExitingPlayMode]
        static void OnExitingPlayMode() => UnloadAll();

        // Test seam for the [OnEnteringPlayMode] handler above.
        internal static void OnEnteringPlayModeForTests() => OnEnteringPlayMode();

#if UNITY_EDITOR
        public static class HotReload {
            public static System.Func<string, string> AssetPathToSrc { get; set; }
            public static bool Enabled { get; set; } = true;

            public static void NotifyAssetChanged(string assetPath) {
                if (!Enabled || AssetPathToSrc == null) return;
                var src = AssetPathToSrc(assetPath);
                if (string.IsNullOrEmpty(src)) return;

                if (_depGraph.IsCommons(src)) {
                    ReloadCommonLibrary(src);
                    return;
                }

                var affected = new System.Collections.Generic.List<string>();
                foreach (var name in _depGraph.ScreensDependingOn(src))
                    affected.Add(name);
                foreach (var name in affected) Reload(name);
            }

            /// <summary>
            /// 由 helper 注册：被调用时应当重建 UI.IconResolver 的 lookup
            /// (e.g., 重新枚举 IconSet 资源 + 重建 dict)。
            /// </summary>
            public static System.Action IconResolverRebuilder { get; set; }

            /// <summary>
            /// 由 AssetPostprocessor / 用户手动调用：通知 icon-related 资源变化。
            /// 重建 IconResolver lookup + 触发所有 open Screen ReSolve。
            /// </summary>
            public static void NotifyIconAssetsChanged() {
                if (!Enabled) return;
                IconResolverRebuilder?.Invoke();
                foreach (var s in _open.Values) s.ReSolve();
            }
        }
#endif
    }
}
