using System.IO;
using PromptUGUI.Application;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Editor.Preview
{
    /// <summary>
    /// Registers Hierarchy / Scene drag handlers so dropping a <c>.ui.xml</c>
    /// asset spawns a <see cref="PromptUGUIDocumentHost"/> wired to it.
    /// </summary>
    [InitializeOnLoad]
    internal static class UIXmlDragHandler
    {
        static UIXmlDragHandler()
        {
            DragAndDrop.AddDropHandlerV2(HierarchyDrop);
            DragAndDrop.AddDropHandlerV2(SceneDrop);
        }

        private static DragAndDropVisualMode HierarchyDrop(
            UnityEngine.EntityId dropTargetEntityId,
            HierarchyDropFlags dropMode,
            Transform parentForDraggedObjects,
            bool perform)
        {
            var asset = FindFirstUiXml(DragAndDrop.objectReferences);
            if (asset == null) return DragAndDropVisualMode.None;
            if (perform)
            {
                Transform parent = parentForDraggedObjects;
                if (parent == null)
                {
                    var target = EditorUtility.EntityIdToObject(dropTargetEntityId) as GameObject;
                    if (target != null) parent = target.transform;
                }
                SpawnHost(asset, parent);
            }
            return DragAndDropVisualMode.Copy;
        }

        private static DragAndDropVisualMode SceneDrop(
            UnityEngine.Object dropUpon,
            Vector3 worldPosition,
            Vector2 viewportPosition,
            Transform parentForDraggedObjects,
            bool perform)
        {
            var asset = FindFirstUiXml(DragAndDrop.objectReferences);
            if (asset == null) return DragAndDropVisualMode.None;
            if (perform)
            {
                var parent = parentForDraggedObjects;
                if (parent == null && dropUpon is GameObject go) parent = go.transform;
                SpawnHost(asset, parent);
            }
            return DragAndDropVisualMode.Copy;
        }

        private static TextAsset FindFirstUiXml(UnityEngine.Object[] refs)
        {
            if (refs == null) return null;
            for (var i = 0; i < refs.Length; i++)
            {
                if (refs[i] is not TextAsset ta) continue;
                var path = AssetDatabase.GetAssetPath(ta);
                if (!string.IsNullOrEmpty(path) && path.EndsWith(".ui.xml")) return ta;
            }
            return null;
        }

        private static void SpawnHost(TextAsset asset, Transform parent)
        {
            // Strip ".ui.xml" so the host node reads as "Login_Preview" rather
            // than "Login.ui_Preview" in the Hierarchy.
            var baseName = Path.GetFileName(AssetDatabase.GetAssetPath(asset));
            if (baseName.EndsWith(".ui.xml"))
                baseName = baseName.Substring(0, baseName.Length - ".ui.xml".Length);
            var go = new GameObject(baseName + "_Preview");
            Undo.RegisterCreatedObjectUndo(go, "Create PromptUGUI Preview");
            if (parent != null) Undo.SetTransformParent(go.transform, parent, "Parent PromptUGUI Preview");

            var host = Undo.AddComponent<PromptUGUIDocumentHost>(go);
            host.xmlAsset = asset;
            host.Refresh();

            EnsureEventSystem();

            Selection.activeGameObject = go;
            EditorSceneManager.MarkSceneDirty(go.scene);
        }

        private static void EnsureEventSystem()
        {
            // Without one in the Scene, runtime pointer events (Btn clicks,
            // ScrollView drags) are silently dropped — the #1 "looks fine but
            // doesn't work in Play Mode" trap for new authors.
            if (UnityEngine.Object.FindAnyObjectByType<EventSystem>() != null) return;
            var esGO = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
            Undo.RegisterCreatedObjectUndo(esGO, "Create EventSystem for PromptUGUI Preview");
        }
    }
}
