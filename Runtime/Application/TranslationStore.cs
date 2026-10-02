using System.Collections.Generic;
using PromptUGUI.I18n;

namespace PromptUGUI.Application
{
    /// <summary>
    /// (locale, ctx, msgid) → msgstr. The base table holds the built-in .po (PoResolver / Resources); runtime
    /// catalogs (<c>UI.Locale.RegisterRuntimeCatalog</c>) sit on top as ordered layers, so one can be removed
    /// without disturbing the base it overrode (spec 2026-10-01-runtime-sprite-sets-design §8.2).
    /// </summary>
    public sealed class TranslationStore
    {
        private readonly Dictionary<(string locale, string ctx, string msgid), string> _entries = new();

        // Registration order: the last layer wins. Empty for a project with no runtime catalogs, which keeps
        // Lookup's path exactly what it was.
        private readonly List<Layer> _layers = new();

        public static TranslationStore Instance { get; } = new();

        /// <summary>
        /// One runtime .po catalog's entries. A handle: the catalog registry keeps it, and "still the same
        /// catalog" is a reference comparison.
        /// </summary>
        internal sealed class Layer
        {
            internal readonly Dictionary<(string locale, string ctx, string msgid), string> Entries = new();
        }

        public string Lookup(string locale, string ctx, string msgid)
        {
            var key = (locale, ctx, msgid);
            for (var i = _layers.Count - 1; i >= 0; i--)
                if (_layers[i].Entries.TryGetValue(key, out var hit)) return hit;
            if (_entries.TryGetValue(key, out var v)) return v;
            return null;
        }

        public void Load(string locale, IEnumerable<PoEntry> entries) => Merge(_entries, locale, entries);

        public void UnloadLocale(string locale)
        {
            RemoveLocale(_entries, locale);
            foreach (var layer in _layers) RemoveLocale(layer.Entries, locale);
        }

        /// <summary>Clears every entry. Layers stay, empty, in their order: they belong to the catalog registry.</summary>
        public void UnloadAll()
        {
            _entries.Clear();
            foreach (var layer in _layers) layer.Entries.Clear();
        }

        internal Layer AddLayer()
        {
            var layer = new Layer();
            _layers.Add(layer);
            return layer;
        }

        internal bool RemoveLayer(Layer layer) => _layers.Remove(layer);

        /// <summary>Merges into <paramref name="layer"/>; a layer already removed (its catalog unregistered
        /// meanwhile) takes nothing.</summary>
        internal void LoadLayer(Layer layer, string locale, IEnumerable<PoEntry> entries)
        {
            if (!_layers.Contains(layer)) return;
            Merge(layer.Entries, locale, entries);
        }

        internal void ClearLayers() => _layers.Clear();

        /// <summary>
        /// Swaps one locale's built-in entries for <paramref name="entries"/> in a single step: a reload never
        /// leaves a moment with neither the old entries nor the new.
        /// </summary>
        internal void ReplaceLocale(string locale, IEnumerable<PoEntry> entries)
        {
            RemoveLocale(_entries, locale);
            Merge(_entries, locale, entries);
        }

        /// <summary><see cref="ReplaceLocale"/> for one layer; a layer already removed takes nothing.</summary>
        internal void ReplaceLayer(Layer layer, string locale, IEnumerable<PoEntry> entries)
        {
            if (!_layers.Contains(layer)) return;
            RemoveLocale(layer.Entries, locale);
            Merge(layer.Entries, locale, entries);
        }

        private static void Merge(Dictionary<(string, string, string), string> into, string locale,
                                  IEnumerable<PoEntry> entries)
        {
            foreach (var e in entries)
            {
                if (string.IsNullOrEmpty(e.Msgstr)) continue;     // miss == empty
                into[(locale, e.Msgctxt, e.Msgid)] = e.Msgstr;
            }
        }

        private static void RemoveLocale(Dictionary<(string locale, string ctx, string msgid), string> from,
                                         string locale)
        {
            var toRemove = new List<(string, string, string)>();
            foreach (var k in from.Keys) if (k.locale == locale) toRemove.Add(k);
            foreach (var k in toRemove) from.Remove(k);
        }
    }
}
