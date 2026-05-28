using System.Collections.Generic;
using PromptUGUI.Parser;
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// Singleton holding parsed &lt;Theme&gt; blocks. Mirrors <c>TranslationStore</c>:
    /// loaders register, runtime looks up. Hot reload routes through <c>ReplaceFromSrc</c>.
    /// Public surface is <c>UI.Theme.*</c>; this class is internal to keep the surface tight.
    /// </summary>
    internal sealed class ThemeStore
    {
        public static ThemeStore Instance { get; } = new();

        private sealed class Entry
        {
            public string Name;
            public string BaseName;
            public Dictionary<string, Color> Colors;
            public string Src;
            public Entry ResolvedBase;
        }

        private readonly Dictionary<string, Entry> _themes = new();

        public IReadOnlyCollection<string> Available => _themes.Keys;

        public void Register(string name, string baseName,
                             IReadOnlyDictionary<string, Color> colors, string src)
        {
            if (_themes.TryGetValue(name, out var existing) && existing.Src != src)
                throw new ParseException(
                    $"duplicate <Theme name=\"{name}\"> in '{existing.Src}' and '{src}'");
            // Same (name, src) pair: replace, not no-op. Two cases hit this branch:
            //   (1) Re-open a Screen whose Import brings the same theme back in —
            //       the values are identical, so replacement is observably a no-op.
            //   (2) Editor cycle "edit theme XML → re-Play" with Domain Reload
            //       disabled — the static singleton persists from the previous
            //       session and the new XML carries DIFFERENT values. An
            //       idempotent skip here silently drops the author's edit; a
            //       replace propagates it correctly. (Hot-reload during Play
            //       routes through ReplaceFromSrc instead of Register, so its
            //       value-update path is unaffected either way.)
            _themes[name] = new Entry
            {
                Name = name,
                BaseName = baseName,
                Colors = new Dictionary<string, Color>(colors),
                Src = src,
            };
        }

        public void ReplaceFromSrc(string src,
            IReadOnlyList<(string name, string baseName, IReadOnlyDictionary<string, Color> colors)> blocks)
        {
            // Hot reload: drop everything previously from src, then register new.
            var toRemove = new List<string>();
            foreach (var kv in _themes)
                if (kv.Value.Src == src) toRemove.Add(kv.Key);
            foreach (var k in toRemove) _themes.Remove(k);
            foreach (var b in blocks)
                Register(b.name, b.baseName, b.colors, src);
            ResolveBases();
        }

        public void ResolveBases()
        {
            foreach (var e in _themes.Values)
            {
                if (string.IsNullOrEmpty(e.BaseName)) { e.ResolvedBase = null; continue; }
                if (!_themes.TryGetValue(e.BaseName, out var b))
                    throw new ParseException(
                        $"<Theme name=\"{e.Name}\" base=\"{e.BaseName}\">: " +
                        $"base theme '{e.BaseName}' not found");
                e.ResolvedBase = b;
            }
            // Cycle: DFS from each theme, fail if we revisit.
            foreach (var e in _themes.Values)
            {
                var seen = new HashSet<string>();
                for (var cur = e; cur != null; cur = cur.ResolvedBase)
                {
                    if (!seen.Add(cur.Name))
                        throw new ParseException(
                            $"<Theme> base cycle starting at '{e.Name}': " +
                            string.Join(" → ", seen) + $" → {cur.Name}");
                }
            }
        }

        public Color? LookupChained(string themeName, string token)
        {
            if (!_themes.TryGetValue(themeName, out var e)) return null;
            for (var cur = e; cur != null; cur = cur.ResolvedBase)
            {
                if (cur.Colors.TryGetValue(token, out var c)) return c;
            }
            return null;
        }

        public void Clear() => _themes.Clear();
    }
}
