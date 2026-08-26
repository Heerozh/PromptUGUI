using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.IR;
using PromptUGUI.Lint;
using PromptUGUI.Parser;

namespace PromptUGUI.UIXmlLint
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (args.Length == 0)
            {
                PrintUsage();
                return 2;
            }

            var paths = ExpandPaths(args);
            if (paths.Count == 0)
            {
                Console.Error.WriteLine("UIXmlLint: no .ui.xml files matched.");
                return 2;
            }

            // Deduplicate ACROSS entry files, not just within one. Linting a directory walks both
            // a library and the document that imports it, and the expanded pass attributes a finding
            // to where it was written — so the same defect would otherwise be printed once per entry
            // file that reaches it. Origin + line is what makes that identity precise enough to fold.
            var reported = new HashSet<string>();
            var errorCount = 0;
            foreach (var path in paths)
                errorCount += LintFile(path, reported);

            if (errorCount > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"UIXmlLint: {errorCount} issue(s) across {paths.Count} file(s).");
                return 1;
            }

            Console.Out.WriteLine($"UIXmlLint: no issues across {paths.Count} file(s).");
            return 0;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("Usage: UIXmlLint <path> [path]...");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Each <path> can be a .ui.xml file or a directory (recursed for *.ui.xml).");
            Console.Error.WriteLine("Shell glob expansion (bash *.ui.xml) is supported by the shell, not by UIXmlLint itself.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  UIXmlLint Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml");
            Console.Error.WriteLine("  UIXmlLint Runtime/Resources/PromptUGUI/");
            Console.Error.WriteLine("  UIXmlLint Assets/UI/  # downstream Unity project");
        }

        private static List<string> ExpandPaths(string[] args)
        {
            var result = new List<string>();
            foreach (var arg in args)
            {
                if (File.Exists(arg))
                {
                    result.Add(arg);
                }
                else if (Directory.Exists(arg))
                {
                    foreach (var f in Directory.EnumerateFiles(arg, "*.ui.xml", SearchOption.AllDirectories))
                        result.Add(f);
                }
                else
                {
                    Console.Error.WriteLine($"UIXmlLint: path not found: {arg}");
                }
            }
            return result;
        }

        private static int LintFile(string path, HashSet<string> reported)
        {
            var doc = TryParse(path, out var parseFailed);
            if (parseFailed) return 1;

            // Everything about WHICH rules run and how the two passes dedup lives in
            // PromptUGUI.Lint.DocumentLinter, so it is covered by PromptUGUI.Tests.EditMode. The CLI
            // owns only I/O: reading files and guessing src -> path (the runtime resolves src through
            // a caller-supplied SourceResolver, which has no on-disk ground truth).
            var closure = TryLoadImportClosure(path, doc, out var unresolved);
            if (closure == null)
            {
                Console.Out.WriteLine(
                    $"{path}: skipping expanded pass - cannot resolve <Import src=\"{unresolved}\"> " +
                    "on disk (Addressables / custom resolver?). Raw-IR rules still applied.");
            }

            var count = 0;
            foreach (var issue in DocumentLinter.Walk(doc, SrcKeyOf(path),
                                                      closure == null ? null : s => Lookup(closure, s)))
            {
                // Origin is the file the markup was WRITTEN in — for a finding inside an imported
                // Template body that is the library, not the entry document that invoked it.
                // "file:line:" is the shape editors and terminals turn into a jump.
                var where = issue.Origin ?? path;
                if (issue.Line > 0) where += ":" + issue.Line;
                // The declaration site stays primary — that is where the edit goes. The invocation
                // is context, and only worth printing when it names a different place.
                var via = issue.Via != null && issue.Via != where ? $" (via {issue.Via})" : "";
                if (!reported.Add(where + via + "|" + issue.Code + "|" + issue.Message)) continue;
                Console.Error.WriteLine($"{where}: [{issue.Code}] {issue.Message}{via}");
                count++;
            }
            return count;
        }

        private static UIDocument Lookup(Dictionary<string, UIDocument> closure, string src)
            => closure.TryGetValue(src, out var d) ? d : null;

        private static UIDocument TryParse(string path, out bool failed)
        {
            failed = true;
            string xml;
            try
            {
                xml = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{path}: read failed: {ex.Message}");
                return null;
            }

            try
            {
                var doc = UIDocumentParser.Parse(xml, path);
                failed = false;
                return doc;
            }
            catch (ParseException ex)
            {
                Console.Error.WriteLine($"{path}: parse error: {ex.Message}");
                return null;
            }
            catch (System.Xml.XmlException ex)
            {
                Console.Error.WriteLine($"{path}: xml error (line {ex.LineNumber}, pos {ex.LinePosition}): {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Parses everything reachable through <c>&lt;Import&gt;</c>. Returns null (with the offending
        /// src) as soon as one cannot be found on disk — a partial closure would make
        /// <c>DocumentLinter</c> report a phantom "unknown template" for a name that resolves fine at
        /// runtime, so it is all or nothing.
        /// </summary>
        private static Dictionary<string, UIDocument> TryLoadImportClosure(
            string path, UIDocument doc, out string unresolved)
        {
            var closure = new Dictionary<string, UIDocument>();
            unresolved = null;
            return Prefetch(path, doc, closure, ref unresolved) ? closure : null;
        }

        private static bool Prefetch(
            string importingPath, UIDocument doc,
            Dictionary<string, UIDocument> closure, ref string unresolved)
        {
            foreach (var imp in doc.Imports)
            {
                if (closure.ContainsKey(imp.Src)) continue;

                var file = ResolveSrc(imp.Src, importingPath);
                if (file == null) { unresolved = imp.Src; return false; }

                UIDocument child;
                // Stamped with the RESOLVED path, not imp.Src: OriginSrc exists so a finding can
                // name a file the author can open, while imp.Src stays the assembler's lookup key.
                try { child = UIDocumentParser.Parse(File.ReadAllText(file), file); }
                catch (Exception) { unresolved = imp.Src; return false; }

                // Record before recursing so a cyclic Import terminates here; DocumentAssembler owns
                // the diagnostic itself, so the CLI and the runtime word it identically.
                closure[imp.Src] = child;
                if (!Prefetch(file, child, closure, ref unresolved)) return false;
            }
            return true;
        }

        /// <summary>
        /// The runtime resolves <c>src</c> through a caller-supplied <c>SourceResolver</c>, so there
        /// is no single ground truth on disk. Approximate the shipped Resources resolver
        /// (<c>UseResourcesResolver(root)</c> maps src to <c>root/src</c>) by trying the importing
        /// file's own directory first, then each ancestor up to and including a <c>Resources</c> one.
        /// </summary>
        private static string ResolveSrc(string src, string importingPath)
        {
            if (string.IsNullOrEmpty(src)) return null;
            var dir = Path.GetDirectoryName(Path.GetFullPath(importingPath));

            while (!string.IsNullOrEmpty(dir))
            {
                var withExt = Path.Combine(dir, src + ".xml");
                if (File.Exists(withExt)) return withExt;
                var verbatim = Path.Combine(dir, src);
                if (File.Exists(verbatim)) return verbatim;

                if (string.Equals(Path.GetFileName(dir), "Resources", StringComparison.Ordinal))
                    break;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>
        /// A stable identity for the entry document. It is only ever compared against
        /// <c>&lt;Import src&gt;</c> values, which are resolver keys, so a full path cannot collide.
        /// </summary>
        private static string SrcKeyOf(string path) => Path.GetFullPath(path);
    }
}
