using UnityEngine;

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

        private static void Inject() =>
            PromptUGUI.Application.UI.Markdown.Renderer ??= new MarkdigRenderer();
    }
}
