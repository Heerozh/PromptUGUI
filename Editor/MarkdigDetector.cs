using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;

namespace PromptUGUI.Editor
{
    /// <summary>Defines PROMPTUGUI_HAS_MARKDIG for the active build target group whenever a "Markdig"
    /// assembly is loaded (NuGetForUnity / DLL), and removes it when absent. Markdig is not a UPM
    /// package, so asmdef versionDefines can't detect it — we scan the AppDomain instead.</summary>
    [InitializeOnLoad]
    internal static class MarkdigDetector
    {
        private const string Symbol = "PROMPTUGUI_HAS_MARKDIG";

        static MarkdigDetector()
        {
            var present = HasMarkdig();
            var group = NamedBuildTarget.FromBuildTargetGroup(
                BuildPipeline.GetBuildTargetGroup(EditorUserBuildSettings.activeBuildTarget));
            var defines = PlayerSettings.GetScriptingDefineSymbols(group);
            var list = new List<string>(defines.Split(';', StringSplitOptions.RemoveEmptyEntries));
            var has = list.Contains(Symbol);

            if (present && !has) { list.Add(Symbol); PlayerSettings.SetScriptingDefineSymbols(group, string.Join(";", list)); }
            else if (!present && has) { list.Remove(Symbol); PlayerSettings.SetScriptingDefineSymbols(group, string.Join(";", list)); }
        }

        private static bool HasMarkdig()
        {
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                var name = a.GetName().Name;
                // NuGetForUnity may install "Markdig" (unsigned) or "Markdig.Signed" (signed variant)
                if (name == "Markdig" || name == "Markdig.Signed") return true;
            }
            return false;
        }
    }
}
