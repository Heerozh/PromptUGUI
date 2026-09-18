using PromptUGUI.Application;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Editor.Preview
{
    /// <summary>
    /// The in-Play half of the UI Preview tool (2026-09-18 ui-preview-tool spec §4.3): an IMGUI
    /// panel drawn through a <see cref="UIPreviewHost"/> that <see cref="UIPreview"/> injects into
    /// the preview scene at Play time. A plain class, not a component — Unity will not attach an
    /// Editor-assembly MonoBehaviour to anything, so the Runtime-side host carries the messages.
    /// </summary>
    internal sealed class UIPreviewOverlay : UIPreviewHost.IOverlay
    {
        internal UIPreviewHost Host { get; private set; }

        internal bool Collapsed
        {
            get => UIPreviewUserState.instance.collapsed;
            set
            {
                if (UIPreviewUserState.instance.collapsed == value) return;
                UIPreviewUserState.instance.collapsed = value;
                UIPreviewUserState.instance.SaveNow();
            }
        }

        private bool _canvasConfiguratorInstalled;

        internal void Attach(UIPreviewHost host)
        {
            Host = host;
            host.Overlay = this;

            EnsureEventSystem();

            // The built-in scene's camera is ours: canvas="camera" Screens (glass needs them) fall
            // back to Overlay silently without a worldCamera, so wire it — but only when the host
            // did not, and only in our own scene (spec §4.2).
            if (UIPreview.UsesBuiltInScene && UI.CanvasConfigurator == null)
            {
                UI.CanvasConfigurator = (canvas, _) => canvas.worldCamera = Camera.main;
                _canvasConfiguratorInstalled = true;
            }
        }

        public void OnUpdate()
        {
        }

        public void OnHostDestroyed()
        {
            if (_canvasConfiguratorInstalled) UI.CanvasConfigurator = null;
            Host = null;
            UIPreview.OnOverlayDestroyed(this);
        }

        public void OnDraw()
        {
            var e = Event.current;
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.F1)
            {
                Collapsed = !Collapsed;
                e.Use();
            }

            if (Collapsed)
            {
                if (GUI.Button(new Rect(10, 10, 140, 26), "UI Preview (F1)")) Collapsed = false;
                return;
            }

            GUI.Box(new Rect(10, 10, 460, 60), GUIContent.none);
            GUI.Label(new Rect(18, 16, 440, 20), "PromptUGUI UI Preview");
            if (GUI.Button(new Rect(18, 38, 100, 24), "Hide (F1)")) Collapsed = true;
        }

        // Without one, runtime pointer events (Btn clicks, ScrollView drags) are silently dropped.
        // Same module choice as UI.Navigation.Enable: the project's active input backend decides.
        private static void EnsureEventSystem()
        {
            if (Object.FindAnyObjectByType<EventSystem>() != null) return;
#if ENABLE_INPUT_SYSTEM
            new GameObject("EventSystem", typeof(EventSystem), typeof(UnityEngine.InputSystem.UI.InputSystemUIInputModule));
#else
            new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
#endif
        }
    }
}
