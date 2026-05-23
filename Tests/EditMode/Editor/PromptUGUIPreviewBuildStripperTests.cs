using NUnit.Framework;
using PromptUGUI.Application;
using PromptUGUI.Editor.Preview;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PromptUGUI.Tests.Editor
{
    public class PromptUGUIPreviewBuildStripperTests
    {
        private GameObject _markerToCleanUp;

        [TearDown]
        public void TearDown()
        {
            if (_markerToCleanUp != null) Object.DestroyImmediate(_markerToCleanUp);
        }

        [Test]
        public void StripHostsFromScene_destroys_host_GameObjects()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var hostGO = new GameObject("Preview");
                hostGO.AddComponent<PromptUGUIDocumentHost>();
                SceneManagerMove(hostGO, scene);

                var keeper = new GameObject("Keeper");
                SceneManagerMove(keeper, scene);

                var removed = PromptUGUIPreviewBuildStripper.StripHostsFromScene(scene);

                Assert.AreEqual(1, removed);
                var roots = scene.GetRootGameObjects();
                foreach (var r in roots)
                    Assert.IsFalse(r.name == "Preview", "host node must be gone");
                var keeperStillThere = false;
                foreach (var r in roots) if (r.name == "Keeper") keeperStillThere = true;
                Assert.IsTrue(keeperStillThere, "non-host roots must be untouched");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        [Test]
        public void StripHostsFromScene_returns_zero_when_no_hosts()
        {
            var scene = EditorSceneManager.NewPreviewScene();
            try
            {
                var go = new GameObject("Unrelated");
                SceneManagerMove(go, scene);
                Assert.AreEqual(0, PromptUGUIPreviewBuildStripper.StripHostsFromScene(scene));
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(scene);
            }
        }

        private static void SceneManagerMove(GameObject go, UnityEngine.SceneManagement.Scene scene)
        {
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(go, scene);
        }
    }
}
