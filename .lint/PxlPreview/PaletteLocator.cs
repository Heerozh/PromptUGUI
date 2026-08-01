using System;
using System.Collections.Generic;
using System.IO;

namespace PromptUGUI.PxlPreview
{
    /// <summary>Outside Unity there is no AssetDatabase, so `palette: @ui` is
    /// resolved by scanning a project root for `ui.gpl` — same contract as
    /// PxlImporter.FindPalettePath: exactly one match, or an error that lists the
    /// candidates.</summary>
    internal static class PaletteLocator
    {
        private static readonly HashSet<string> SkipDirs =
            new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "Library", "Temp", "Logs", "obj", "bin", "Build", "Builds", ".git", ".utmp", "node_modules" };

        public static string Find(string pxlPath, string paletteRef, out string error)
        {
            var start = Path.GetDirectoryName(Path.GetFullPath(pxlPath));
            var root = FindSearchRoot(start);
            var matches = new List<string>();
            Collect(root, paletteRef + ".gpl", matches, 0);
            matches.Sort(StringComparer.Ordinal);

            if (matches.Count == 1) { error = null; return matches[0]; }
            error = matches.Count == 0
                ? $"palette '@{paletteRef}' not found (no '{paletteRef}.gpl' under {root})"
                : $"palette '@{paletteRef}' is ambiguous: {string.Join(", ", matches)}";
            return null;
        }

        /// <summary>Nearest thing to "the project": the Unity project root (parent of
        /// the outermost `Assets` ancestor, so embedded `Packages/` are searched too),
        /// else the enclosing git repo, else the file's own folder.</summary>
        private static string FindSearchRoot(string dir)
        {
            string assetsParent = null;
            string repo = null;
            var d = new DirectoryInfo(dir);
            while (d != null)
            {
                if (string.Equals(d.Name, "Assets", StringComparison.Ordinal) && d.Parent != null)
                    assetsParent = d.Parent.FullName;
                if (repo == null && Directory.Exists(Path.Combine(d.FullName, ".git")))
                    repo = d.FullName;
                d = d.Parent;
            }
            return assetsParent ?? repo ?? dir;
        }

        private static void Collect(string dir, string fileName, List<string> into, int depth)
        {
            if (depth > 32) return; // symlink loop guard
            try
            {
                foreach (var f in Directory.EnumerateFiles(dir, fileName))
                    into.Add(f);
                foreach (var sub in Directory.EnumerateDirectories(dir))
                {
                    var name = Path.GetFileName(sub);
                    if (SkipDirs.Contains(name)) continue;
                    Collect(sub, fileName, into, depth + 1);
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }
}
