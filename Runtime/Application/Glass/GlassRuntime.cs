using UnityEngine;

namespace PromptUGUI.Application
{
    /// <summary>
    /// The render-pipeline-agnostic half of the glass system: it counts how many glass panels are
    /// actually on screen, starts and stops the backdrop capture accordingly, and publishes one
    /// global shader value telling every glass shader whether a backdrop exists this frame.
    ///
    /// <para>Two deliberate performance choices live here.</para>
    ///
    /// <para><b>Nothing runs when nothing is glass.</b> The capture chain is driven by a panel
    /// count, not by a component the user drops in a scene: with no glass panel visible the URP pass
    /// is never enqueued, no render targets exist, and the per-frame cost is exactly zero.</para>
    ///
    /// <para><b>Availability is a global uniform, not a shader keyword.</b> A keyword (or a flag in
    /// <c>PanelParams</c>) would make every quality-setting toggle re-key, re-acquire and re-assign a
    /// material on every glass panel alive — and a material swap is a canvas material rebuild. One
    /// <c>SetGlobalFloat</c> reaches all of them for free, and the branch it feeds is uniform across
    /// every fragment, so the GPU never diverges on it.</para>
    /// </summary>
    internal static class GlassRuntime
    {
        internal const string BackdropAvailableProperty = "_PUGUI_GlassBackdropAvailable";
        private static readonly int BackdropAvailableId =
            Shader.PropertyToID(BackdropAvailableProperty);

        private static int _activePanels;
        private static bool _backdropAvailable;
        private static bool _enabled = true;
        private static bool _captureRunning;

        /// <summary>Live glass panels. Test-only observability.</summary>
        internal static int ActivePanelCount => _activePanels;

        /// <summary>Backs <c>UI.Glass.Enabled</c>.</summary>
        internal static bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                _enabled = value;
                SyncCapture();
                Push();
            }
        }

        /// <summary>Backs <c>UI.Glass.Camera</c>.</summary>
        internal static Camera Camera { get; set; }

        /// <summary>
        /// Lets the capture run outside play mode. Only the render tests set this: they drive
        /// <c>Camera.Render()</c> by hand so the whole pipeline — capture pass, blur chain, glass
        /// shader — can be verified against real pixels without a play session.
        /// </summary>
        internal static bool RenderOutsidePlayModeForTests { get; set; }

        internal static void PanelActivated()
        {
            _activePanels++;
            if (_activePanels == 1) SyncCapture();
        }

        internal static void PanelDeactivated()
        {
            if (_activePanels == 0) return;
            _activePanels--;
            if (_activePanels == 0) SyncCapture();
        }

        /// <summary>Called by the capture pass once it has published a usable backdrop.</summary>
        internal static void SetBackdropAvailable(bool available)
        {
            if (_backdropAvailable == available) return;
            _backdropAvailable = available;
            Push();
        }

        internal static void SetBackdropAvailableForTests(bool available)
            => SetBackdropAvailable(available);

        private static void SyncCapture()
        {
            var wanted = _enabled && _activePanels > 0;
            if (wanted == _captureRunning) return;
            _captureRunning = wanted;
#if PROMPTUGUI_HAS_URP
            Glass.GlassBackdropSystem.SetActive(wanted);
#endif
            // Stopping the capture invalidates whatever texture was published; say so immediately
            // rather than leaving panels sampling a stale frame.
            if (!wanted) SetBackdropAvailable(false);
        }

        private static void Push()
            => Shader.SetGlobalFloat(BackdropAvailableId,
                                     _enabled && _backdropAvailable ? 1f : 0f);

        internal static void ResetForTestsInternal()
        {
            _activePanels = 0;
            _enabled = true;
            Camera = null;
            RenderOutsidePlayModeForTests = false;
            SyncCapture();
            _backdropAvailable = false;
            Push();
        }
    }
}
