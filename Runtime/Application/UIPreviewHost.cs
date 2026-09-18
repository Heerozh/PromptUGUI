#if UNITY_EDITOR
using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// Editor-only carrier for the UI Preview overlay (2026-09-18 ui-preview-tool spec §4.3). The
    /// overlay's logic lives in the Editor assembly, but Unity refuses to attach an Editor-assembly
    /// MonoBehaviour to a GameObject ("Can't add script behaviour … because it is an editor
    /// script"), so this shell — in the Runtime assembly, compiled only for the Editor — carries the
    /// component and forwards its messages to whatever the Editor side attached. Injected into the
    /// preview scene at Play time, never serialized into an asset; the whole file is
    /// <c>UNITY_EDITOR</c>, so a Player has no such type.
    /// </summary>
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class UIPreviewHost : MonoBehaviour
    {
        /// <summary>What the Editor assembly plugs in: the three messages it needs, in Unity's order.</summary>
        public interface IOverlay
        {
            public void OnUpdate();
            public void OnDraw();
            public void OnHostDestroyed();
        }

        public IOverlay Overlay { get; set; }

        private void Update() => Overlay?.OnUpdate();
        private void OnGUI() => Overlay?.OnDraw();
        private void OnDestroy() => Overlay?.OnHostDestroyed();
    }
}
#endif
