using PromptUGUI.Application;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PromptUGUI.Editor.Preview
{
    /// <summary>
    /// Strips <see cref="PromptUGUIDocumentHost"/> preview nodes from any scene
    /// being built. The host's runtime payload is already a no-op shell (all
    /// logic is <c>#if UNITY_EDITOR</c>), but removing the GameObject entirely
    /// avoids the "missing serialized field" warnings from the empty MonoBehaviour
    /// shell trying to deserialize editor-only fields in a Player build.
    /// </summary>
    internal sealed class PromptUGUIPreviewBuildStripper : IProcessSceneWithReport
    {
        public int callbackOrder => 0;

        public void OnProcessScene(Scene scene, BuildReport report)
        {
            // report is null when this runs at Play-mode enter; only strip during
            // an actual build so EditMode preview keeps working.
            if (report == null) return;
            StripHostsFromScene(scene);
        }

        internal static int StripHostsFromScene(Scene scene)
        {
            var roots = scene.GetRootGameObjects();
            var removed = 0;
            foreach (var root in roots)
            {
                var hosts = root.GetComponentsInChildren<PromptUGUIDocumentHost>(includeInactive: true);
                foreach (var h in hosts)
                {
                    if (h == null) continue;
                    Object.DestroyImmediate(h.gameObject);
                    removed++;
                }
            }
            return removed;
        }
    }
}
