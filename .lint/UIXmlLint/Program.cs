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

            var errorCount = 0;
            foreach (var path in paths)
                errorCount += LintFile(path);

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

        private static int LintFile(string path)
        {
            string xml;
            try
            {
                xml = File.ReadAllText(path);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"{path}: read failed: {ex.Message}");
                return 1;
            }

            UIDocument doc;
            try
            {
                doc = UIDocumentParser.Parse(xml);
            }
            catch (ParseException ex)
            {
                Console.Error.WriteLine($"{path}: parse error: {ex.Message}");
                return 1;
            }
            catch (System.Xml.XmlException ex)
            {
                Console.Error.WriteLine($"{path}: xml error (line {ex.LineNumber}, pos {ex.LinePosition}): {ex.Message}");
                return 1;
            }

            var count = 0;
            foreach (var issue in IRWalker.Walk(doc))
            {
                Console.Error.WriteLine($"{path}: [{issue.Code}] {issue.Message}");
                count++;
            }
            return count;
        }
    }
}
