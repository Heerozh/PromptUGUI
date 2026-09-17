using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.Lint;

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

            // Everything about WHICH rules run, how the two passes dedup, how a finding is spelled
            // and how duplicates fold across entry files lives in PromptUGUI.Lint (DocumentLinter /
            // LintRun), so it is covered by PromptUGUI.Tests.EditMode and shared with the Editor
            // menu. The CLI owns only I/O: enumerating files, reading them, guessing src -> path
            // (the runtime resolves src through a caller-supplied SourceResolver, which has no
            // on-disk ground truth), and printing.
            var run = new LintRun();
            foreach (var path in paths)
            {
                foreach (var finding in run.Lint(path, File.ReadAllText, ResolveSrc))
                {
                    if (finding.Kind == LintRun.Kind.Note)
                        Console.Out.WriteLine(finding.Text);
                    else
                        Console.Error.WriteLine(finding.Text);
                }
            }

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

        /// <summary>
        /// The filesystem guess at the shipped Resources resolver (see
        /// <see cref="ImportClosure.ResolveInResources"/>), started from the importing file's own
        /// directory made absolute so the walk can climb past the working directory.
        /// </summary>
        private static string ResolveSrc(string src, string importingPath)
            => ImportClosure.ResolveInResources(
                src, Path.GetDirectoryName(Path.GetFullPath(importingPath)), File.Exists);
    }
}
