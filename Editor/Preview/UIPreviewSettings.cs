using UnityEditor;
using UnityEngine;

namespace PromptUGUI.Editor.Preview
{
    /// <summary>
    /// Project-wide UI Preview configuration (2026-09-18 ui-preview-tool spec §4.12): which scene
    /// the preview plays in and the two Game view sizes the Landscape / Portrait buttons switch
    /// between. Lives in <c>ProjectSettings/PromptUGUIPreview.asset</c> — committed with the
    /// project, edited under Project Settings › PromptUGUI › UI Preview.
    ///
    /// <para>Not a field on <c>PromptUGUISettings</c>: that asset is preloaded into the Player, and
    /// a <c>SceneAsset</c> reference on it would drag the preview scene (and everything it references)
    /// into every build. A GUID string here costs nothing and survives the scene being moved.</para>
    /// </summary>
    [FilePath("ProjectSettings/PromptUGUIPreview.asset", FilePathAttribute.Location.ProjectFolder)]
    internal sealed class UIPreviewSettings : ScriptableSingleton<UIPreviewSettings>
    {
        /// <summary>GUID of the scene to play the preview in; empty = the package's built-in scene.</summary>
        public string sceneGuid = "";

        public Vector2Int landscape = new Vector2Int(1920, 1080);
        public Vector2Int portrait = new Vector2Int(1080, 1920);

        public void SaveNow() => Save(saveAsText: true);
    }
}
