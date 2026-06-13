using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Compilation;

namespace PromptUGUI.Editor
{
    /// <summary>Defines PROMPTUGUI_HAS_MARKDIG for <b>every</b> build target group whenever the
    /// "Markdig" (or "Markdig.Signed") precompiled assembly is present, and removes it when absent.
    /// Markdig is a NuGetForUnity DLL, not a UPM package, so asmdef versionDefines can't detect it —
    /// we ask the compilation pipeline for precompiled assembly names instead.
    ///
    /// The symbol MUST be applied to all groups, not just the active one: the gated
    /// PromptUGUI.Markdown asmdef (defineConstraints = PROMPTUGUI_HAS_MARKDIG) has to compile into
    /// whatever platform the user actually builds. If the symbol lived only on the editor's active
    /// target, the editor demo would work while a player built for any other platform silently lost
    /// the whole asmdef → UI.Markdown.Renderer stayed null → "Markdig Not Detected".</summary>
    [InitializeOnLoad]
    internal static class MarkdigDetector
    {
        private const string Symbol = "PROMPTUGUI_HAS_MARKDIG";

        static MarkdigDetector()
        {
            var present = HasMarkdig();
            foreach (var group in AllNamedBuildTargets())
                Apply(group, present);
        }

        private static void Apply(NamedBuildTarget group, bool present)
        {
            string defines;
            try { defines = PlayerSettings.GetScriptingDefineSymbols(group); }
            catch { return; }   // platform module not installed / target unsupported in this editor

            var list = new List<string>(defines.Split(';', StringSplitOptions.RemoveEmptyEntries));
            var has = list.Contains(Symbol);
            if (present == has) return;   // already correct — don't dirty ProjectSettings or trigger a needless recompile

            if (present) list.Add(Symbol);
            else list.Remove(Symbol);

            try { PlayerSettings.SetScriptingDefineSymbols(group, string.Join(";", list)); }
            catch { /* unsupported target — skip */ }
        }

        /// <summary>Every build target group that maps to a NamedBuildTarget, de-duplicated by name.
        /// We deliberately do NOT skip obsolete enum members: BuildTargetGroup.iOS shares its value
        /// with the obsolete alias iPhone and the enum's ToString() resolves to "iPhone", so an
        /// obsolete filter would silently drop iOS. FromBuildTargetGroup throws for dead groups
        /// (caught below) and the TargetName HashSet collapses aliases onto their canonical target.</summary>
        private static IEnumerable<NamedBuildTarget> AllNamedBuildTargets()
        {
            var seen = new HashSet<string>();
            foreach (BuildTargetGroup grp in Enum.GetValues(typeof(BuildTargetGroup)))
            {
                if (grp == BuildTargetGroup.Unknown) continue;

                NamedBuildTarget named;
                try { named = NamedBuildTarget.FromBuildTargetGroup(grp); }
                catch { continue; }   // group has no public NamedBuildTarget (dead/obsolete platform)

                if (seen.Add(named.TargetName))
                    yield return named;
            }
        }

        private static bool HasMarkdig()
        {
            foreach (var dll in CompilationPipeline.GetPrecompiledAssemblyNames())
            {
                // Returns bare file names, e.g. "Markdig.Signed.dll".
                // NuGetForUnity may install "Markdig" (unsigned) or "Markdig.Signed" (signed variant).
                if (string.Equals(dll, "Markdig.dll", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(dll, "Markdig.Signed.dll", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }
    }
}
