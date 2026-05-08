using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;

namespace PromptUGUI.Application {
    public static class UI {
        static ControlRegistry _registry = new();
        static readonly Dictionary<string, ScreenDef> _docs = new();
        static readonly Dictionary<string, Screen> _open = new();
        static readonly VariantStore _variantStore = new();
        static readonly System.Collections.Generic.Dictionary<DocumentLoader.TemplateKey, IR.TemplateDef> _commonsPool = new();
        static readonly DepGraph _depGraph = new();

        public static System.Func<string, string> SourceResolver { get; set; }

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

        // 仅测试使用
        internal static void ResetForTests() {
            foreach (var s in _open.Values) s.Close();
            _open.Clear();
            _docs.Clear();
            _variantStore.Reset();
            _registry = new ControlRegistry();
            _commonsPool.Clear();
            _depGraph.Clear();
            SourceResolver = null;
        }
    }
}
