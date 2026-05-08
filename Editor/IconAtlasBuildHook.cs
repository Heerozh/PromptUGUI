using PromptUGUI.Application;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PromptUGUI.Editor {
    public sealed class IconAtlasBuildHook : IPreprocessBuildWithReport {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report) {
            var sets = new System.Collections.Generic.List<IconSet>();
            foreach (var s in IconAtlasSyncer.FindAllIconSets()) sets.Add(s);
            if (sets.Count == 0) return;
            Debug.Log($"[PromptUGUI] Pre-build syncing {sets.Count} IconSet(s)");
            IconAtlasSyncer.SyncAll(sets);
        }
    }
}
