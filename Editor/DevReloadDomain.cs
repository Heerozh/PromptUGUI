using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor
{
    internal static class DevReloadDomain
    {
        // %#r = Ctrl+Shift+R (Win/Linux) / Cmd+Shift+R (macOS)
        [MenuItem("Tools/PromptUGUI/Force Reload Domain %#r")]
        private static void Run()
        {
            EditorUtility.RequestScriptReload();
            Debug.Log("[PromptUGUI] Domain reload requested.");
        }
    }
}
