using System;
using System.Collections.Generic;
using System.IO;
using PromptUGUI.Editor;
using UnityEngine;

namespace PromptUGUI.PxlPreview
{
    public static class Program
    {
        public static int Main(string[] args)
        {
            var opt = new RenderOptions();
            string outDir = null;
            string paletteOverride = null;
            var inputs = new List<string>();

            for (var i = 0; i < args.Length; i++)
            {
                var a = args[i];
                switch (a)
                {
                    case "--scale":
                    case "-s":
                        if (++i >= args.Length || !int.TryParse(args[i], out opt.Scale) ||
                            opt.Scale < 1 || opt.Scale > 64)
                        {
                            Console.Error.WriteLine("PxlPreview: --scale needs an integer 1..64");
                            return 2;
                        }
                        break;
                    case "--out-dir":
                    case "-o":
                        if (++i >= args.Length) { Console.Error.WriteLine("PxlPreview: --out-dir needs a path"); return 2; }
                        outDir = args[i];
                        break;
                    case "--palette":
                        if (++i >= args.Length) { Console.Error.WriteLine("PxlPreview: --palette needs a .gpl path"); return 2; }
                        paletteOverride = args[i];
                        break;
                    case "--guides":
                        opt.Guides = true;
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage();
                        return 0;
                    default:
                        if (a.StartsWith("-", StringComparison.Ordinal))
                        {
                            Console.Error.WriteLine($"PxlPreview: unknown option '{a}'");
                            return 2;
                        }
                        inputs.Add(a);
                        break;
                }
            }

            if (inputs.Count == 0) { PrintUsage(); return 2; }

            var paths = ExpandPaths(inputs);
            if (paths.Count == 0)
            {
                Console.Error.WriteLine("PxlPreview: no .pxl files matched.");
                return 2;
            }

            outDir = Path.GetFullPath(outDir ?? Path.Combine(Path.GetTempPath(), "pxlpreview"));
            try { Directory.CreateDirectory(outDir); }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"PxlPreview: cannot create out-dir {outDir}: {ex.Message}");
                return 2;
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var failed = 0;
            foreach (var path in paths)
            {
                if (!RenderFile(path, outDir, paletteOverride, opt, used)) failed++;
            }

            if (failed > 0)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine($"PxlPreview: {failed} of {paths.Count} file(s) failed.");
                return 1;
            }
            return 0;
        }

        private static void PrintUsage()
        {
            Console.Error.WriteLine("Usage: PxlPreview <path>... [--scale N] [--out-dir DIR] [--palette FILE] [--guides]");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Renders each .pxl to one PNG (all sections side by side, on a transparency");
            Console.Error.WriteLine("checkerboard, labelled) so the art can be looked at instead of read as text.");
            Console.Error.WriteLine("Parse / palette errors are reported exactly as the Unity importer reports them.");
            Console.Error.WriteLine();
            Console.Error.WriteLine("  <path>        a .pxl file or a directory (recursed for *.pxl)");
            Console.Error.WriteLine("  --scale N     pixel magnification, 1..64 (default 8)");
            Console.Error.WriteLine("  --out-dir DIR where PNGs are written (default: <temp>/pxlpreview)");
            Console.Error.WriteLine("  --palette F   use this .gpl instead of searching the project for @<name>");
            Console.Error.WriteLine("  --guides      overlay the 9-slice border split lines");
            Console.Error.WriteLine();
            Console.Error.WriteLine("Never point --out-dir at a SpriteSet sourceFolder: the PNGs would be ingested");
            Console.Error.WriteLine("as new sprite sources and collide with the .pxl's own keys.");
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
                    var found = new List<string>(Directory.EnumerateFiles(arg, "*.pxl", SearchOption.AllDirectories));
                    found.Sort(StringComparer.Ordinal);
                    result.AddRange(found);
                }
                else
                {
                    Console.Error.WriteLine($"PxlPreview: path not found: {arg}");
                }
            }
            return result;
        }

        private static bool RenderFile(string path, string outDir, string paletteOverride,
            RenderOptions opt, HashSet<string> used)
        {
            string text;
            try { text = File.ReadAllText(path); }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"{path}: cannot read: {ex.Message}");
                return false;
            }

            PxlDocument doc;
            try { doc = PxlParser.Parse(text); }
            catch (PxlParseException ex)
            {
                Console.Error.WriteLine($"{path}: {ex.Message}");
                return false;
            }

            GplPalette palette = null;
            if (doc.PaletteRef != null || paletteOverride != null)
            {
                var gplPath = paletteOverride;
                if (gplPath == null)
                {
                    gplPath = PaletteLocator.Find(path, doc.PaletteRef, out var error);
                    if (gplPath == null)
                    {
                        Console.Error.WriteLine($"{path}: {error}");
                        return false;
                    }
                }
                try { palette = GplPalette.Parse(File.ReadAllText(gplPath)); }
                catch (IOException ex)
                {
                    Console.Error.WriteLine($"{gplPath}: cannot read: {ex.Message}");
                    return false;
                }
                catch (FormatException ex)
                {
                    Console.Error.WriteLine($"{gplPath}: {ex.Message}");
                    return false;
                }
            }

            Dictionary<char, Color32> colors;
            try { colors = PxlColorResolver.Resolve(doc, doc.PaletteRef != null ? palette : null); }
            catch (PxlParseException ex)
            {
                Console.Error.WriteLine($"{path}: {ex.Message}");
                return false;
            }

            var basename = Path.GetFileNameWithoutExtension(path);
            var canvas = Renderer.Render(doc, colors, basename, opt);
            var outPath = UniquePath(outDir, basename, used);
            try { PngWriter.Write(outPath, canvas.Width, canvas.Height, canvas.Pixels); }
            catch (IOException ex)
            {
                Console.Error.WriteLine($"{outPath}: cannot write: {ex.Message}");
                return false;
            }

            Console.Out.WriteLine(outPath);
            foreach (var s in doc.Sections)
            {
                var name = s.Name ?? "(implicit)";
                var line = $"  {name}  {s.Width}x{s.Height}";
                if (s.Border.x != 0 || s.Border.y != 0 || s.Border.z != 0 || s.Border.w != 0)
                    line += $"  border {(int)s.Border.x},{(int)s.Border.y},{(int)s.Border.z},{(int)s.Border.w}";
                if (s.Tiled) line += "  tiled";
                Console.Out.WriteLine(line);
            }
            return true;
        }

        /// <summary>Same basename in two folders would otherwise overwrite silently
        /// when a whole tree is rendered into one flat out-dir.</summary>
        private static string UniquePath(string outDir, string basename, HashSet<string> used)
        {
            var candidate = basename;
            var n = 2;
            while (!used.Add(candidate))
                candidate = basename + "-" + n++;
            return Path.Combine(outDir, candidate + ".preview.png");
        }
    }
}
