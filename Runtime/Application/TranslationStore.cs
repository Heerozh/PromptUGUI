using System.Collections.Generic;
using PromptUGUI.I18n;

namespace PromptUGUI.Application {
    public sealed class TranslationStore {
        readonly Dictionary<(string locale, string ctx, string msgid), string> _entries = new();

        public static TranslationStore Instance { get; } = new();

        public string Lookup(string locale, string ctx, string msgid) {
            if (_entries.TryGetValue((locale, ctx, msgid), out var v)) return v;
            return null;
        }

        public void Load(string locale, IEnumerable<PoEntry> entries) {
            foreach (var e in entries) {
                if (string.IsNullOrEmpty(e.Msgstr)) continue;     // miss == empty
                _entries[(locale, e.Msgctxt, e.Msgid)] = e.Msgstr;
            }
        }

        public void UnloadLocale(string locale) {
            var toRemove = new List<(string, string, string)>();
            foreach (var k in _entries.Keys) if (k.locale == locale) toRemove.Add(k);
            foreach (var k in toRemove) _entries.Remove(k);
        }

        public void UnloadAll() => _entries.Clear();
    }
}
