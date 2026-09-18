using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using PromptUGUI.IR;
using PromptUGUI.Lint;

namespace PromptUGUI.UIXmlLint
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            if (!Options.TryParse(args, out var options, out var usageError))
            {
                if (usageError != null) Console.Error.WriteLine("UIXmlLint: " + usageError);
                PrintUsage();
                return 2;
            }

            var paths = ExpandPaths(options.Paths);
            if (paths.Count == 0)
            {
                Console.Error.WriteLine("UIXmlLint: no .ui.xml files matched.");
                return 2;
            }

            var commons = LoadCommons(options, paths);
            if (commons == null) return 2;

            // Everything about WHICH rules run, how the two passes dedup, how a finding is spelled,
            // how the common libraries are merged and how duplicates fold across entry files lives
            // in PromptUGUI.Lint (DocumentLinter / LintRun), so it is covered by
            // PromptUGUI.Tests.EditMode and shared with the Editor menu. The CLI owns only I/O:
            // enumerating files, reading them, guessing src -> path (the runtime resolves src
            // through a caller-supplied SourceResolver, which has no on-disk ground truth), finding
            // the settings asset, and printing.
            var run = new LintRun();
            var resolve = MakeResolver(options.SrcRoots);
            foreach (var path in paths)
            {
                foreach (var finding in run.Lint(path, File.ReadAllText, resolve, commons))
                {
                    if (finding.Kind == LintRun.Kind.Note)
                        Console.Out.WriteLine(finding.Text);
                    else
                        Console.Error.WriteLine(finding.Text);
                }
            }

            // A note is printed once per unresolved library; how much of the run it cost is said here.
            if (run.Skipped > 0)
                Console.Out.WriteLine(
                    $"UIXmlLint: {run.Skipped} of {paths.Count} file(s) skipped the expanded pass over an unresolved src " +
                    "(see the notes above); --src-root <dir> names the folder those keys are relative to.");

            // An unknown style / template with no common library declared is most often a library
            // nobody listed — the same hint the runtime and the Editor menu give (spec §4.8, §4.10).
            if (run.SawUnknownName && commons.Count == 0)
                Console.Out.WriteLine(
                    "UIXmlLint: no common library is configured — if these names live in a shared library, " +
                    "list it under Common Libraries in the PromptUGUISettings asset (or pass --settings / --commons).");

            if (run.Issues > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"UIXmlLint: {run.Issues} issue(s) across {paths.Count} file(s).");
                return 1;
            }

            Console.Out.WriteLine($"UIXmlLint: no issues across {paths.Count} file(s).");
            return 0;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("Usage: UIXmlLint [options] <path> [path]...");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Each <path> can be a .ui.xml file or a directory (recursed for *.ui.xml).");
            Console.Error.WriteLine("Shell glob expansion (bash *.ui.xml) is supported by the shell, not by UIXmlLint itself.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Options:");
            Console.Error.WriteLine("  --settings <file.asset>  PromptUGUISettings asset whose Common Libraries to merge into every");
            Console.Error.WriteLine("                           document's expanded pass. Default: the settings asset found under the");
            Console.Error.WriteLine("                           nearest Assets/ folder above the linted files (none = no commons).");
            Console.Error.WriteLine("  --commons <src>[@<as>]   A common library, as the settings would declare it (repeatable). Reads");
            Console.Error.WriteLine("                           no asset at all — for CI or a checkout without a Unity project.");
            Console.Error.WriteLine("  --src-root <dir>         Extra folder to resolve a src against (<dir>/<src>.xml, then <dir>/<src>),");
            Console.Error.WriteLine("                           after the usual guess from the importing file's folder up to Resources/.");
            Console.Error.WriteLine("                           The root of UseResourcesResolver(root), or the folder Addressables");
            Console.Error.WriteLine("                           addresses are relative to. Repeatable.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Examples:");
            Console.Error.WriteLine("  UIXmlLint Runtime/Resources/PromptUGUI/Modals/MessageBox.ui.xml");
            Console.Error.WriteLine("  UIXmlLint Runtime/Resources/PromptUGUI/");
            Console.Error.WriteLine("  UIXmlLint Assets/UI/  # downstream Unity project; settings asset found automatically");
            Console.Error.WriteLine("  UIXmlLint Assets/_Project/ --src-root Assets/_Project/Common  # short Addressables addresses");
            Console.Error.WriteLine("  UIXmlLint Assets/UI/ --commons UI/Templates/Theme.ui --commons UI/Widgets.ui@ui");
        }

        // ── arguments ──────────────────────────────────────────────────────────────────────────

        private sealed class Options
        {
            public readonly List<string> Paths = new List<string>();
            public readonly List<ImportRef> Commons = new List<ImportRef>();
            public readonly List<string> SrcRoots = new List<string>();
            public string Settings;

            public static bool TryParse(string[] args, out Options options, out string error)
            {
                options = new Options();
                error = null;
                for (var i = 0; i < args.Length; i++)
                {
                    var arg = args[i];
                    switch (arg)
                    {
                        case "-h":
                        case "--help":
                            return false;
                        case "--settings":
                            if (!Next(args, ref i, arg, out var settings, out error)) return false;
                            options.Settings = settings;
                            break;
                        case "--commons":
                            if (!Next(args, ref i, arg, out var row, out error)) return false;
                            options.Commons.Add(ParseCommons(row));
                            break;
                        case "--src-root":
                            if (!Next(args, ref i, arg, out var root, out error)) return false;
                            options.SrcRoots.Add(root);
                            break;
                        default:
                            if (arg.StartsWith("--", StringComparison.Ordinal))
                            {
                                error = "unknown option " + arg;
                                return false;
                            }
                            options.Paths.Add(arg);
                            break;
                    }
                }
                if (options.Paths.Count == 0)
                {
                    error = args.Length == 0 ? null : "no path given";
                    return false;
                }
                return true;
            }

            private static bool Next(string[] args, ref int i, string option, out string value, out string error)
            {
                value = null;
                error = null;
                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    error = option + " needs a value";
                    return false;
                }
                value = args[++i];
                return true;
            }

            /// <summary><c>src</c> or <c>src@ns</c> — the settings row, spelled on one line.</summary>
            private static ImportRef ParseCommons(string row)
            {
                var at = row.LastIndexOf('@');
                if (at < 0) return new ImportRef(row.Trim(), null);
                var ns = row.Substring(at + 1).Trim();
                return new ImportRef(row.Substring(0, at).Trim(), ns.Length == 0 ? null : ns);
            }
        }

        private static List<string> ExpandPaths(List<string> args)
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

        // ── src → file ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// The filesystem guess at the shipped Resources resolver (see
        /// <see cref="ImportClosure.ResolveInResources"/>), started from the importing file's own
        /// directory made absolute so the walk can climb past the working directory; then each
        /// <c>--src-root</c>, for the keys that are relative to a folder the guess cannot know —
        /// <c>UseResourcesResolver(root)</c>'s root, or the folder short Addressables addresses
        /// are relative to.
        /// </summary>
        private static Func<string, string, string> MakeResolver(List<string> srcRoots)
        {
            return (src, importing) =>
            {
                var hit = ImportClosure.ResolveInResources(
                    src, Path.GetDirectoryName(Path.GetFullPath(importing)), File.Exists);
                if (hit != null) return hit;
                foreach (var root in srcRoots)
                {
                    var withExt = Path.Combine(root, src + ".xml");
                    if (File.Exists(withExt)) return withExt;
                    var verbatim = Path.Combine(root, src);
                    if (File.Exists(verbatim)) return verbatim;
                }
                return null;
            };
        }

        // ── common libraries (2026-09-18 commons-settings spec §4.9) ──────────────────────────

        /// <summary>
        /// The rows to merge into every document: <c>--commons</c> as given; else the
        /// <c>--settings</c> asset; else the settings asset discovered under the linted files'
        /// <c>Assets/</c> folder; else none. Null (after a message) when an asset that was asked
        /// for explicitly cannot be used — that is a usage error, not "no commons".
        /// </summary>
        private static List<ImportRef> LoadCommons(Options options, List<string> paths)
        {
            if (options.Commons.Count > 0) return options.Commons;

            var explicitAsset = options.Settings != null;
            var asset = options.Settings ?? DiscoverSettings(paths);
            if (asset == null) return new List<ImportRef>();

            string yaml;
            try
            {
                yaml = File.ReadAllText(asset);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"UIXmlLint: cannot read settings asset {asset}: {ex.Message}");
                return explicitAsset ? null : new List<ImportRef>();
            }
            if (!SettingsAssetReader.IsPromptUGUISettings(Head(yaml)))
            {
                Console.Error.WriteLine($"UIXmlLint: {asset} is not a PromptUGUISettings asset (text-serialized, m_Script guid {SettingsAssetReader.ScriptGuid}).");
                return explicitAsset ? null : new List<ImportRef>();
            }

            var refs = new List<ImportRef>();
            foreach (var row in SettingsAssetReader.ReadCommonLibraries(yaml))
                if (row != null && !row.IsBlank) refs.Add(row.ToImportRef());

            var spelled = new List<string>();
            foreach (var r in refs) spelled.Add(r.Namespace == null ? r.Src : r.Src + "@" + r.Namespace);
            Console.Out.WriteLine(
                $"UIXmlLint: common libraries from {asset}: " + (spelled.Count == 0 ? "none declared" : string.Join(", ", spelled)));
            return refs;
        }

        private static string Head(string text) => text.Length <= 4096 ? text : text.Substring(0, 4096);

        /// <summary>
        /// The one <c>PromptUGUISettings</c> asset of the project the linted files belong to: from
        /// each entry, the nearest <c>Assets/</c> ancestor; under it, first a file with the default
        /// name, else every <c>*.asset</c> whose head names the settings script. Several found:
        /// the first (sorted) with a note on stderr — the Editor's own maintainer already errors on
        /// that. None: no commons, exactly as before the setting existed.
        /// </summary>
        private static string DiscoverSettings(List<string> paths)
        {
            var roots = new List<string>();
            foreach (var path in paths)
            {
                var root = AssetsRootOf(path);
                if (root != null && !roots.Contains(root)) roots.Add(root);
            }

            var found = new List<string>();
            foreach (var root in roots)
            {
                var named = new List<string>();
                foreach (var f in Directory.EnumerateFiles(root, SettingsAssetReader.DefaultFileName, SearchOption.AllDirectories))
                    if (IsSettingsAsset(f)) named.Add(f);
                if (named.Count > 0)
                {
                    found.AddRange(named);
                    continue;
                }
                foreach (var f in Directory.EnumerateFiles(root, "*.asset", SearchOption.AllDirectories))
                    if (IsSettingsAsset(f)) found.Add(f);
            }
            if (found.Count == 0) return null;

            found.Sort(StringComparer.Ordinal);
            if (found.Count > 1)
                Console.Error.WriteLine(
                    $"UIXmlLint: {found.Count} PromptUGUISettings assets found; using {found[0]} " +
                    "(pass --settings to pick one).");
            return found[0];
        }

        private static string AssetsRootOf(string path)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(path));
            while (!string.IsNullOrEmpty(dir))
            {
                if (string.Equals(Path.GetFileName(dir), "Assets", StringComparison.Ordinal)) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>The first 4 KB is where Unity writes <c>m_Script</c>; no need to read a whole asset.</summary>
        private static bool IsSettingsAsset(string file)
        {
            try
            {
                using (var stream = File.OpenRead(file))
                {
                    var buffer = new byte[4096];
                    var n = stream.Read(buffer, 0, buffer.Length);
                    return SettingsAssetReader.IsPromptUGUISettings(Encoding.UTF8.GetString(buffer, 0, n));
                }
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
