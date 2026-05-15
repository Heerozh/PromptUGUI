using PromptUGUI.Application;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace PromptUGUI.Editor
{
    public sealed class SpriteAtlasBuildHook : IPreprocessBuildWithReport
    {
        public int callbackOrder => 0;

        public void OnPreprocessBuild(BuildReport report)
        {
            var sets = new System.Collections.Generic.List<SpriteSet>();
            foreach (var s in SpriteAtlasSyncer.FindAllSpriteSets()) sets.Add(s);
            if (sets.Count == 0) return;
            Debug.Log($"[PromptUGUI] Pre-build syncing {sets.Count} SpriteSet(s)");
            SpriteAtlasSyncer.SyncAll(sets);
        }
    }
}
