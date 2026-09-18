using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.IR;
using PromptUGUI.Parser;

namespace PromptUGUI.Lint
{
    /// <summary>
    /// Fetches everything an entry document reaches through <c>&lt;Import&gt;</c> so that
    /// <see cref="DocumentLinter"/> can lint the EXPANDED tree. The runtime resolves <c>src</c>
    /// through a caller-supplied <c>SourceResolver</c>, so there is no single ground truth for which
    /// file a src means — the two front ends supply that answer (the CLI guesses from the
    /// filesystem, the Editor menu can also ask Addressables) and this class owns everything after
    /// it: the walk, the cycle guard, the all-or-nothing verdict and the origin stamps.
    ///
    /// <para>No I/O of its own: <c>exists</c> / <c>read</c> come in as delegates, which keeps this
    /// file in the pure-C# Core subset the CLI compiles and keeps the Runtime assembly
    /// filesystem-free.</para>
    /// </summary>
    public static class ImportClosure
    {
        /// <summary>
        /// Parsed documents shared across the entries of one lint run: src → (the file it resolved
        /// to, the document). The file is kept because a cached library's own imports are resolved
        /// relative to it, the same as on first fetch.
        /// </summary>
        public sealed class Cache
        {
            private readonly Dictionary<string, (string File, UIDocument Doc)> _bySrc =
                new Dictionary<string, (string, UIDocument)>();

            public int Count => _bySrc.Count;

            public bool TryGet(string src, out string file, out UIDocument doc)
            {
                if (_bySrc.TryGetValue(src, out var hit))
                {
                    file = hit.File;
                    doc = hit.Doc;
                    return true;
                }
                file = null;
                doc = null;
                return false;
            }

            public void Add(string src, string file, UIDocument doc) => _bySrc[src] = (file, doc);
        }

        /// <summary>
        /// Approximates the shipped Resources resolver (<c>UseResourcesResolver(root)</c> maps src to
        /// <c>root/src</c>) without knowing <c>root</c>: try the importing file's own directory
        /// first, then each ancestor up to and including a <c>Resources</c> one, each time with a
        /// trailing <c>.xml</c> (the src is <c>Home.ui</c> on disk as <c>Home.ui.xml</c>) and then
        /// verbatim (an address that already carries its extension).
        /// </summary>
        /// <param name="importingDir">
        /// Directory of the document that wrote the <c>&lt;Import&gt;</c>. Pass it absolute to keep
        /// walking above the working directory; relative and the walk ends at the first segment.
        /// </param>
        public static string ResolveInResources(string src, string importingDir, Func<string, bool> exists)
        {
            if (string.IsNullOrEmpty(src)) return null;
            var dir = importingDir;

            while (!string.IsNullOrEmpty(dir))
            {
                var withExt = Path.Combine(dir, src + ".xml");
                if (exists(withExt)) return withExt;
                var verbatim = Path.Combine(dir, src);
                if (exists(verbatim)) return verbatim;

                if (string.Equals(Path.GetFileName(dir), "Resources", StringComparison.Ordinal))
                    break;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>
        /// Parses everything reachable through <c>&lt;Import&gt;</c>, keyed by the src the assembler
        /// will ask for. Returns null (naming the offending src in <paramref name="unresolved"/>) as
        /// soon as one cannot be resolved or parsed — a partial closure would make
        /// <see cref="DocumentLinter"/> report a phantom "unknown template" for a name that resolves
        /// fine at runtime, so it is all or nothing.
        /// </summary>
        /// <param name="resolve">
        /// <c>(src, importingPath) → path</c>, or null when unknown. The importing path is the file
        /// that wrote the Import, so a relative guess can start from the right place.
        /// </param>
        /// <param name="read">Path → XML text. May throw; that counts as unresolvable.</param>
        public static Dictionary<string, UIDocument> TryLoad(
            string entryPath, UIDocument entry,
            Func<string, string, string> resolve,
            Func<string, string> read,
            out string unresolved)
            => TryLoad(entryPath, entry, resolve, read, commons: null, cache: null, out _, out unresolved);

        /// <summary>
        /// As above, with the project's common libraries (<c>PromptUGUISettings.commonLibraries</c>,
        /// 2026-09-18 commons-settings spec §4.7) folded in: they are the <c>&lt;Import&gt;</c> every
        /// document implicitly has, so each row is resolved from the ENTRY file — a Resources-style
        /// short key is guessed from the entry's folder exactly like an Import written there — and
        /// its own imports are followed. The entry's explicit imports come first; a row they already
        /// brought in is not fetched twice. Any row that cannot be resolved makes the whole closure
        /// null, like an Import would: a document expanded against half its commons would report
        /// phantom unknown names.
        /// </summary>
        /// <param name="cache">
        /// Optional, shared across the entries of one run: src → parsed document for the commons
        /// closure (rows and their transitive imports). A src found here is taken as is, without
        /// <paramref name="resolve"/> or <paramref name="read"/> — forty entries re-parsing the
        /// same theme library would be forty times the work for nothing.
        /// </param>
        /// <param name="applied">
        /// The rows that apply to THIS entry — <paramref name="commons"/> minus any row that resolves
        /// to the entry file itself. A directory run reaches the theme library as an entry in its
        /// own right, and a library is not its own commons: merged onto itself, its every name would
        /// "conflict with commons pool". Hand this, not the full list, to <c>DocumentLinter.Walk</c>.
        /// </param>
        public static Dictionary<string, UIDocument> TryLoad(
            string entryPath, UIDocument entry,
            Func<string, string, string> resolve,
            Func<string, string> read,
            IReadOnlyList<ImportRef> commons,
            Cache cache,
            out IReadOnlyList<ImportRef> applied,
            out string unresolved)
        {
            var closure = new Dictionary<string, UIDocument>();
            unresolved = null;
            applied = commons;
            if (!Prefetch(entryPath, entry, closure, resolve, read, null, ref unresolved)) return null;
            if (commons == null) return closure;

            List<ImportRef> kept = null;
            foreach (var lib in commons)
            {
                if (string.IsNullOrEmpty(lib.Src) || closure.ContainsKey(lib.Src)) continue;

                string file = null;
                var cached = cache != null && cache.TryGet(lib.Src, out file, out _);
                if (!cached) file = resolve(lib.Src, entryPath);
                if (file != null && SamePath(file, entryPath))
                {
                    kept ??= Without(commons, lib);
                    continue;
                }
                if (!Fetch(lib.Src, entryPath, closure, resolve, read, cache, ref unresolved, file)) return null;
            }
            if (kept != null) applied = kept;
            return closure;
        }

        private static List<ImportRef> Without(IReadOnlyList<ImportRef> rows, ImportRef drop)
        {
            var kept = new List<ImportRef>(rows.Count);
            foreach (var row in rows)
                if (!ReferenceEquals(row, drop)) kept.Add(row);
            return kept;
        }

        /// <summary>
        /// Two spellings of one file. Full paths so a relative entry and an absolute resolution
        /// compare equal; case-insensitive because the file systems that matter here mostly are, and
        /// two files differing only in case is nothing a lint run needs to tell apart.
        /// </summary>
        private static bool SamePath(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
            }
            catch (Exception)
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        private static bool Prefetch(
            string importingPath, UIDocument doc,
            Dictionary<string, UIDocument> closure,
            Func<string, string, string> resolve,
            Func<string, string> read,
            Cache cache,
            ref string unresolved)
        {
            foreach (var imp in doc.Imports)
            {
                if (closure.ContainsKey(imp.Src)) continue;
                if (!Fetch(imp.Src, importingPath, closure, resolve, read, cache, ref unresolved)) return false;
            }
            return true;
        }

        /// <summary>One src into the closure — from the cache when it is there, else resolved, read, parsed — then its own imports.</summary>
        private static bool Fetch(
            string src, string importingPath,
            Dictionary<string, UIDocument> closure,
            Func<string, string, string> resolve,
            Func<string, string> read,
            Cache cache,
            ref string unresolved,
            string resolvedFile = null)
        {
            UIDocument child;
            string file;
            if (cache == null || !cache.TryGet(src, out file, out child))
            {
                file = resolvedFile ?? resolve(src, importingPath);
                if (file == null) { unresolved = src; return false; }

                // Stamped with the RESOLVED path, not the src: OriginSrc exists so a finding can
                // name a file the author can open, while the src stays the assembler's lookup key.
                try { child = UIDocumentParser.Parse(read(file), file); }
                catch (Exception) { unresolved = src; return false; }

                cache?.Add(src, file, child);
            }

            // Record before recursing so a cyclic Import terminates here; DocumentAssembler owns
            // the diagnostic itself, so the CLI and the runtime word it identically.
            closure[src] = child;
            return Prefetch(file, child, closure, resolve, read, cache, ref unresolved);
        }
    }
}
