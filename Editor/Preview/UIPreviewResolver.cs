using System;
using System.Collections.Generic;
using System.IO;

namespace PromptUGUI.Editor.Preview
{
    /// <summary>
    /// The disk-first src resolution the UI Preview installs as <c>UI.SourceResolver</c>
    /// (2026-09-18 ui-preview-tool spec §4.4 / §4.5), as decisions over injected functions so it
    /// is testable without a project: where a src's text comes from, and — for hot reload — which
    /// src a saved asset path was served as. The overlay does the I/O; this class only decides.
    ///
    /// <para>Order, fixed: an absolute path → a project asset path (<c>Assets/…</c>,
    /// <c>Packages/…</c>, read through its physical location) → the locator (Addressables exact,
    /// Resources walk from the anchoring file) → a unique suffix match over the project's files
    /// (the fallback for a custom resolver; <c>X.ui</c> counts as <c>X.ui.xml</c>) → a project
    /// path that is simply missing is an error, never handed to the host (the author would take
    /// the host's copy for the file on disk) → everything else is the host's.</para>
    /// </summary>
    internal sealed class UIPreviewResolver
    {
        internal enum Source { Disk, Host, Error }

        internal readonly struct Resolution
        {
            public readonly Source Source;
            /// <summary>Disk: the asset path the file is known by (the served-table key).</summary>
            public readonly string AssetPath;
            /// <summary>Disk: the file to read.</summary>
            public readonly string Physical;
            /// <summary>Error: what to say.</summary>
            public readonly string Message;

            private Resolution(Source source, string assetPath, string physical, string message)
            {
                Source = source;
                AssetPath = assetPath;
                Physical = physical;
                Message = message;
            }

            public static Resolution Disk(string assetPath, string physical) => new Resolution(Source.Disk, assetPath, physical, null);
            public static Resolution Host() => new Resolution(Source.Host, null, null, null);
            public static Resolution Error(string message) => new Resolution(Source.Error, null, null, message);
        }

        private readonly Func<string, string> _physical;
        private readonly Func<string, bool> _exists;
        private readonly Func<string, string, string> _locate;
        private readonly Func<string, string> _toAssetPath;
        private readonly Dictionary<string, string> _served = new Dictionary<string, string>(StringComparer.Ordinal);

        /// <param name="physical">asset path (or absolute path) → file on disk (<c>UiXmlLocator.Physical</c>).</param>
        /// <param name="exists">file on disk → exists.</param>
        /// <param name="locate">(src, anchor asset path) → asset path, or null (<c>UiXmlLocator.Locate</c>).</param>
        /// <param name="toAssetPath">file on disk → asset path (<c>UiXmlLocator.ToAssetPath</c>).</param>
        internal UIPreviewResolver(Func<string, string> physical, Func<string, bool> exists,
                                   Func<string, string, string> locate, Func<string, string> toAssetPath)
        {
            _physical = physical;
            _exists = exists;
            _locate = locate;
            _toAssetPath = toAssetPath;
        }

        /// <summary>Asset paths of every <c>.ui.xml</c> the project owns — the suffix fallback's haystack.</summary>
        internal IReadOnlyList<string> Index { get; set; } = Array.Empty<string>();

        /// <summary>
        /// The file a Resources-style key is guessed from: the entry being loaded, else the last
        /// file loaded; null skips the Resources walk (the Addressables map still answers).
        /// </summary>
        internal string Anchor { get; set; }

        /// <summary>Whether the host has a resolver of its own to fall back to.</summary>
        internal bool HasHost { get; set; }

        /// <summary>asset path → the src it was first served as (spec §4.5). Read-only view for diagnostics.</summary>
        internal IReadOnlyDictionary<string, string> Served => _served;

        internal Resolution Resolve(string src)
        {
            if (string.IsNullOrEmpty(src)) return Resolution.Error("src is empty");

            // 1. absolute path
            if (Path.IsPathRooted(src) && _exists(src))
                return Resolution.Disk(_toAssetPath(src), src);

            // 2. a project asset path
            var isProjectPath = IsProjectPath(src);
            if (isProjectPath)
            {
                var physical = _physical(src);
                if (_exists(physical)) return Resolution.Disk(src, physical);
            }

            // 3. the locator: Addressables address / GUID, or a Resources key from the anchor
            var located = _locate(src, Anchor);
            if (!string.IsNullOrEmpty(located))
            {
                var physical = _physical(located);
                if (_exists(physical)) return Resolution.Disk(located, physical);
            }

            // 4. unique suffix over the project's files (custom-resolver fallback)
            var hit = MatchSuffix(src);
            if (hit != null)
            {
                var physical = _physical(hit);
                if (_exists(physical)) return Resolution.Disk(hit, physical);
            }

            // 5. a project path that is not there: say so, do not hand it to the host
            if (isProjectPath)
                return Resolution.Error($"'{src}' is not on disk — the preview reads project files from disk, not from the host's copy");

            // 6. the host's (built-in modal skins in Resources, build-only content)
            return HasHost ? Resolution.Host() : Resolution.Error($"cannot resolve src='{src}': not a project file and the host has no SourceResolver");
        }

        /// <summary>Remembers which src a file was served as; the first src to reach a file wins (spec §4.5).</summary>
        internal void RecordServed(string assetPath, string src)
        {
            if (string.IsNullOrEmpty(assetPath) || string.IsNullOrEmpty(src)) return;
            if (!_served.ContainsKey(assetPath)) _served[assetPath] = src;
        }

        /// <summary>
        /// The src to hot-reload for a saved asset path: what this resolver served it as, else what
        /// the host's mapping says, else nothing. Ours first — the entry is registered in the
        /// DepGraph under the asset path, which a host mapping (Addressables address, Resources
        /// name) would not produce.
        /// </summary>
        internal string AssetPathToSrc(string assetPath, Func<string, string> host)
        {
            if (string.IsNullOrEmpty(assetPath)) return null;
            if (_served.TryGetValue(assetPath, out var src)) return src;
            return host?.Invoke(assetPath);
        }

        internal static bool IsProjectPath(string src) =>
            src.StartsWith("Assets/", StringComparison.Ordinal) || src.StartsWith("Packages/", StringComparison.Ordinal);

        /// <summary>
        /// The one project file whose path ends with <c>/src</c> (or <c>/src.xml</c> for a
        /// Resources-style <c>X.ui</c>); two candidates is no answer — silently picking one is worse
        /// than the host's copy.
        /// </summary>
        internal string MatchSuffix(string src)
        {
            var trimmed = src.TrimStart('/');
            if (trimmed.Length == 0) return null;
            var needle = "/" + trimmed;
            var needleXml = trimmed.EndsWith(".ui", StringComparison.OrdinalIgnoreCase) ? needle + ".xml" : null;
            string hit = null;
            foreach (var path in Index)
            {
                var haystack = "/" + path;
                if (!haystack.EndsWith(needle, StringComparison.OrdinalIgnoreCase)
                    && (needleXml == null || !haystack.EndsWith(needleXml, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (hit != null) return null;
                hit = path;
            }
            return hit;
        }

        /// <summary>
        /// Whether a document text declares a <c>&lt;Screen&gt;</c> — a template-only file cannot be
        /// previewed on its own. Text scan, not a parse: cheap enough to run over every file at
        /// refresh. A <c>&lt;ScreenFoo&gt;</c> does not count; unreadable (null) text does — let the
        /// author click it and see the real error rather than a file that silently vanished.
        /// </summary>
        internal static bool HasScreen(string text)
        {
            if (text == null) return true;
            for (var i = text.IndexOf("<Screen", StringComparison.OrdinalIgnoreCase);
                 i >= 0;
                 i = text.IndexOf("<Screen", i + 7, StringComparison.OrdinalIgnoreCase))
            {
                var next = i + 7 < text.Length ? text[i + 7] : '>';
                if (char.IsWhiteSpace(next) || next == '>' || next == '/') return true;
            }
            return false;
        }

        /// <summary>
        /// The key a <c>&lt;Pages&gt;</c> selection is remembered under: per file, per Pages id,
        /// numbered so two template instances with the same id (in FindAll order) stay apart. Null
        /// when there is nothing to key on.
        /// </summary>
        internal static string PagesKey(string file, string pagesId, int index)
        {
            if (string.IsNullOrEmpty(file) || string.IsNullOrEmpty(pagesId)) return null;
            return $"{file}|{pagesId}#{index}";
        }
    }
}
