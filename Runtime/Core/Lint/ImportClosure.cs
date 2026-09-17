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
        {
            var closure = new Dictionary<string, UIDocument>();
            unresolved = null;
            return Prefetch(entryPath, entry, closure, resolve, read, ref unresolved) ? closure : null;
        }

        private static bool Prefetch(
            string importingPath, UIDocument doc,
            Dictionary<string, UIDocument> closure,
            Func<string, string, string> resolve,
            Func<string, string> read,
            ref string unresolved)
        {
            foreach (var imp in doc.Imports)
            {
                if (closure.ContainsKey(imp.Src)) continue;

                var file = resolve(imp.Src, importingPath);
                if (file == null) { unresolved = imp.Src; return false; }

                UIDocument child;
                // Stamped with the RESOLVED path, not imp.Src: OriginSrc exists so a finding can
                // name a file the author can open, while imp.Src stays the assembler's lookup key.
                try { child = UIDocumentParser.Parse(read(file), file); }
                catch (Exception) { unresolved = imp.Src; return false; }

                // Record before recursing so a cyclic Import terminates here; DocumentAssembler owns
                // the diagnostic itself, so the CLI and the runtime word it identically.
                closure[imp.Src] = child;
                if (!Prefetch(file, child, closure, resolve, read, ref unresolved)) return false;
            }
            return true;
        }
    }
}
