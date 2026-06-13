using UnityEngine;
using UnityEngine.Scripting;

// PromptUGUI.Markdown is a leaf assembly — the dependency points the other way (Runtime never
// references the gated backend), so no asmdef in a host game references it. The Editor loads every
// compiled asmdef into the AppDomain (so [InitializeOnLoadMethod] below runs there), but a player
// build only ships assemblies reachable from a referenced root, so an IL2CPP/WebGL build drops this
// one entirely — its [RuntimeInitializeOnLoadMethod] never runs and UI.Markdown.Renderer stays null
// even with PROMPTUGUI_HAS_MARKDIG defined and Markdig shipped. AlwaysLinkAssembly forces the build
// to include this assembly and scan it for the init method.
[assembly: AlwaysLinkAssembly]

namespace PromptUGUI.MarkdigBackend
{
    internal static class MarkdigBootstrap
    {
        private static bool _hooked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
#endif
        private static void Install()
        {
            Inject();
            if (_hooked) return;
            PromptUGUI.Application.UI.OnReset += Inject;
            _hooked = true;
        }

        private static void Inject()
        {
            try
            {
                PromptUGUI.Application.UI.Markdown.Renderer ??= new MarkdigRenderer();
            }
            catch (System.Exception e)
            {
                // Turn a silent null Renderer into a diagnosable error (e.g. a Markdig type that
                // fails to initialize under IL2CPP) instead of a feature that quietly no-ops.
                Debug.LogError("[PromptUGUI] MarkdigRenderer initialization failed: " + e);
            }
        }
    }
}
