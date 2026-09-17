using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The one clock every drifting fog reads (spec 2026-09-17 haze §5.5): a global shader float,
    /// <c>_PuguiUnscaledTime</c>, published once per canvas render. A global rather than a
    /// per-material uniform because the time is not a parameter — writing it into each material
    /// every frame would defeat the shared-material cache, and a material change would mark every
    /// panel dirty. A global costs one <c>SetGlobalFloat</c> per frame, no matter how many panels
    /// drift.
    ///
    /// <para><b>Unscaled.</b> A pause menu (<c>timeScale = 0</c>) keeps its fog flowing, the same
    /// clock the close transition, Toasts and the Carousel run on. In Edit mode
    /// <c>Time.unscaledTime</c> does not advance between editor frames, so the editor's real-time
    /// clock stands in; the fog then only moves when something repaints, like every other animation.</para>
    ///
    /// <para><b>Starts on demand.</b> <see cref="Ensure"/> is called by the material cache when it
    /// configures the first material with <c>hazeDrift &gt; 0</c>; a project with no moving fog
    /// never subscribes and the global stays at 0, which every still fog reads as "no offset".
    /// <see cref="Canvas.willRenderCanvases"/> is the hook because it fires exactly once per frame
    /// before the canvases are drawn (and from <c>Canvas.ForceUpdateCanvases</c>, which is what an
    /// EditMode render test calls), on every platform, with no MonoBehaviour to host.</para>
    /// </summary>
    internal static class HazeClock
    {
        internal static readonly int TimeId = Shader.PropertyToID("_PuguiUnscaledTime");

        private static bool _running;
        private static float? _pinned;
        private static int _subscriptions;

        /// <summary>Whether the per-frame publisher is subscribed. Test observability.</summary>
        internal static bool IsRunning => _running;

        /// <summary>How many times the publisher was subscribed — a guard for Ensure's idempotence.</summary>
        internal static int SubscriptionsForTests => _subscriptions;

        internal static void Ensure()
        {
            if (_running) return;
            _running = true;
            _subscriptions++;
            Canvas.willRenderCanvases += Tick;
        }

        private static void Tick()
        {
            // A render test pins the clock between two snapshots; the tick must not overwrite it.
            if (_pinned.HasValue) return;
            var now = UnityEngine.Application.isPlaying ? Time.unscaledTime : Time.realtimeSinceStartup;
            Shader.SetGlobalFloat(TimeId, now);
        }

        /// <summary>Holds the clock at <paramref name="seconds"/> until the next reset.</summary>
        internal static void SetTimeForTests(float seconds)
        {
            _pinned = seconds;
            Shader.SetGlobalFloat(TimeId, seconds);
        }

        /// <summary>Unsubscribes, drops any pin and rewinds the global to 0. Called from <c>UI.ResetForTests</c>.</summary>
        internal static void ResetForTests()
        {
            if (_running)
            {
                Canvas.willRenderCanvases -= Tick;
                _running = false;
            }
            _pinned = null;
            Shader.SetGlobalFloat(TimeId, 0f);
        }
    }
}
