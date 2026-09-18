using System;

namespace PromptUGUI.Editor.Preview
{
    /// <summary>
    /// The decisions the UI Preview menu makes before touching the Editor (2026-09-18
    /// ui-preview-tool spec §4.1 / §4.2), as pure functions so they are testable without a Play
    /// session: what F8 does in the current state, and which scene the session plays in.
    /// </summary>
    internal static class UIPreviewRules
    {
        internal enum MenuAction { Launch, ToggleCollapse, Refuse }

        /// <summary>
        /// F8 in Edit mode launches; F8 while the preview is playing toggles the panel; F8 in any
        /// other Play session, or with compile errors outstanding, is refused with a reason.
        /// Compile errors matter because <c>EnterPlaymode()</c> then never fires
        /// <c>ExitingEditMode</c>, and a Pending flag left behind would send the NEXT ordinary Play
        /// into the preview scene.
        /// </summary>
        internal static MenuAction Decide(bool isPlayingOrWillChange, bool previewActive, bool compilationFailed, out string reason)
        {
            reason = null;
            if (previewActive) return MenuAction.ToggleCollapse;
            if (isPlayingOrWillChange)
            {
                reason = "UI Preview: already in Play mode — stop it first.";
                return MenuAction.Refuse;
            }
            if (compilationFailed)
            {
                reason = "UI Preview: fix the compile errors first.";
                return MenuAction.Refuse;
            }
            return MenuAction.Launch;
        }

        internal readonly struct ScenePick
        {
            public readonly string Path;
            public readonly bool IsBuiltIn;
            /// <summary>Non-null when a configured scene could not be found and the built-in one is used instead.</summary>
            public readonly string Warning;

            public ScenePick(string path, bool isBuiltIn, string warning)
            {
                Path = path;
                IsBuiltIn = isBuiltIn;
                Warning = warning;
            }
        }

        /// <summary>
        /// The configured scene when its GUID still names a scene asset; otherwise the built-in
        /// one — with a warning when something WAS configured (moved or deleted), silently when
        /// nothing was (the default).
        /// </summary>
        internal static ScenePick PickScene(string sceneGuid, Func<string, string> guidToPath, Func<string, bool> isSceneAsset, string builtInPath)
        {
            if (string.IsNullOrEmpty(sceneGuid)) return new ScenePick(builtInPath, true, null);
            var path = guidToPath(sceneGuid);
            if (!string.IsNullOrEmpty(path) && isSceneAsset(path)) return new ScenePick(path, false, null);
            return new ScenePick(builtInPath, true,
                "[PromptUGUI] UI Preview: the configured preview scene was not found (moved or deleted?) — " +
                "using the built-in scene. Fix it under Project Settings › PromptUGUI › UI Preview.");
        }
    }
}
