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

        /// <summary>
        /// How many frames a published backdrop stays trusted without being refreshed. One frame of
        /// slack is required — the watchdog runs before the cameras render, so in any given frame the
        /// newest backdrop is legitimately from the frame before.
        /// </summary>
        private const int StaleAfterFrames = 2;

        private static int _activePanels;
        private static bool _backdropAvailable;
        private static bool _enabled = true;
        private static bool _captureRunning;
        private static int _lastPublishFrame = int.MinValue;
        private static bool _watchdogHooked;

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

        /// <summary>
        /// Replaces the colour transform the capture applies while downsampling (see
        /// <c>GlassBackdropDecode</c>). Only the render tests set this: the real transform is
        /// derived from the HDR display state, which no test can switch on, so they substitute a
        /// matrix of their own to prove the capture actually applies one.
        /// </summary>
        internal static Matrix4x4? BackdropDecodeOverrideForTests { get; set; }

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
            if (available) _lastPublishFrame = Time.frameCount;
            if (_backdropAvailable == available) return;
            _backdropAvailable = available;
            Push();
        }

        internal static void SetBackdropAvailableForTests(bool available)
            => SetBackdropAvailable(available);

        /// <summary>Publishes with an explicit frame stamp so the watchdog can be driven in tests.</summary>
        internal static void PublishBackdropForTests(int frame)
        {
            _backdropAvailable = true;
            _lastPublishFrame = frame;
            Push();
        }

        /// <summary>
        /// Drops a backdrop nobody is refreshing any more.
        ///
        /// <para>Availability is latched by the capture pass, so every way production can stop
        /// without the capture being stopped — the capture camera disabled for a cutscene or
        /// destroyed, the render pipeline swapped, URP running in Compatibility Mode where
        /// <c>RecordRenderGraph</c> is never called — would otherwise leave every glass panel
        /// sampling one frozen frame indefinitely, with <c>UI.Glass.IsActive</c> still claiming all
        /// is well. Watching the freshness of the result covers all of those at once, which is why
        /// this is one watchdog rather than a check per cause.</para>
        /// </summary>
        internal static void TickBackdropWatchdog(int currentFrame)
        {
            if (!_backdropAvailable) return;
            if (currentFrame - _lastPublishFrame < StaleAfterFrames) return;
            _backdropAvailable = false;
            Push();
        }

        private static void SyncCapture()
        {
            var wanted = _enabled && _activePanels > 0;
            if (wanted == _captureRunning) return;
            _captureRunning = wanted;
#if PROMPTUGUI_HAS_URP
            Glass.GlassBackdropSystem.SetActive(wanted);
#endif
            HookWatchdog(wanted);
            // Stopping the capture invalidates whatever texture was published; say so immediately
            // rather than leaving panels sampling a stale frame.
            if (!wanted) SetBackdropAvailable(false);
        }

        /// <summary>
        /// The watchdog rides <c>Canvas.willRenderCanvases</c> because it has to tick even when no
        /// camera renders at all — an Overlay-only frame with every camera disabled is exactly one of
        /// the cases that strands a frozen backdrop, and a camera callback cannot observe it.
        /// </summary>
        private static void HookWatchdog(bool hook)
        {
            if (hook == _watchdogHooked) return;
            _watchdogHooked = hook;
            if (hook) Canvas.willRenderCanvases += OnWillRenderCanvases;
            else Canvas.willRenderCanvases -= OnWillRenderCanvases;
        }

        private static void OnWillRenderCanvases() => TickBackdropWatchdog(Time.frameCount);

        private static void Push()
            => Shader.SetGlobalFloat(BackdropAvailableId,
                                     _enabled && _backdropAvailable ? 1f : 0f);

        internal static void ResetForTestsInternal() => ResetAll();

        /// <summary>
        /// Statics do not survive a domain reload — but with <em>Enter Play Mode Options</em> set to
        /// skip it they survive every play session after the first, and the warn-once diagnostics
        /// below are the ones that explain why glass silently degraded. Left latched, they fire in
        /// the first session of an editor run and then leave every later reproduction in exactly the
        /// silence they exist to break.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayStart() => ResetAll();

        private static void ResetAll()
        {
            _activePanels = 0;
            _enabled = true;
            Camera = null;
            RenderOutsidePlayModeForTests = false;
            BackdropDecodeOverrideForTests = null;
            SyncCapture();
            _backdropAvailable = false;
            _lastPublishFrame = int.MinValue;
            Push();

            // Warn-once latches live next to the state they describe, so they are cleared with it.
            Controls.Internal.ProceduralPanel.ResetDiagnostics();
            Controls.Internal.GlassGroupPanel.ResetDiagnostics();
#if PROMPTUGUI_HAS_URP
            Glass.GlassBackdropSystem.ResetDiagnostics();
#endif
        }
    }
}
