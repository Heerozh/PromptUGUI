using System.Collections.Generic;
using PromptUGUI.IR;
using PromptUGUI.Parser;
using PromptUGUI.Registry;

namespace PromptUGUI.Application {
    public static class UI {
        static ControlRegistry _registry = new();
        static readonly Dictionary<string, ScreenDef> _docs = new();
        static readonly Dictionary<string, Screen> _open = new();

        public static ControlRegistry Registry => _registry;

        public static void LoadDocument(string label, string xml) {
            var doc = UIDocumentParser.Parse(xml);
            foreach (var s in doc.Screens) {
                if (_docs.ContainsKey(s.Name))
                    throw new System.InvalidOperationException(
                        $"Screen '{s.Name}' already loaded");
                _docs[s.Name] = s;
            }
        }

        public static Screen Open(string screenName) {
            if (_open.TryGetValue(screenName, out var existing)) return existing;
            if (!_docs.TryGetValue(screenName, out var def))
                throw new System.InvalidOperationException(
                    $"Screen '{screenName}' not loaded; call LoadDocument first");

            var inst = new ScreenInstantiator(_registry);
            var screen = new Screen(def, inst);
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
            _registry = new ControlRegistry();
        }
    }
}
