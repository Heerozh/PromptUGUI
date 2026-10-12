using System.Collections.Generic;
using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The one clock every running <see cref="Cooldown"/> reads (spec 2026-10-13 §5.6): once per
    /// frame it advances them all — the sweep, the countdown, and the ready edge when one runs out.
    ///
    /// <para><b>Not tied to a GameObject.</b> A cooldown on a page a <c>&lt;TabBar&gt;</c> or the Router
    /// has hidden keeps counting and still goes ready on time — the game's skill bar does not stop
    /// cooling because the player opened the inventory. So there is no MonoBehaviour <c>Update</c>
    /// here: the tick hangs off <see cref="Canvas.willRenderCanvases"/>, which fires once per frame
    /// before the canvases are drawn whatever is active (and from <c>Canvas.ForceUpdateCanvases</c>,
    /// which is what an EditMode test calls) — the hook <see cref="HazeClock"/> already uses.</para>
    ///
    /// <para><b>Timestamps, not deltas.</b> A cooldown stores when it ends; a tick only compares that
    /// with <see cref="Now"/>. Dropped frames, a hidden page and a re-sync from the server cannot
    /// drift it.</para>
    ///
    /// <para><b>Starts on demand</b>, with the first cooldown that runs; a project without one never
    /// subscribes.</para>
    /// </summary>
    internal static class CooldownClock
    {
        private static readonly List<Cooldown> _running = new();
        // Ticking walks a copy: a ready handler may start another cooldown, or restart this one.
        private static readonly List<Cooldown> _scratch = new();
        private static bool _subscribed;
        private static float? _pinnedScaled;
        private static float? _pinnedUnscaled;

        /// <summary>
        /// The time a cooldown is measured against. <c>scaled</c> follows <c>Time.timeScale</c> (a
        /// paused game pauses its cooldowns); unscaled does not. Outside Play mode both read the
        /// editor's real-time clock, which is the only one that advances there.
        /// </summary>
        internal static float Now(bool scaled)
        {
            var pinned = scaled ? _pinnedScaled : _pinnedUnscaled;
            if (pinned.HasValue) return pinned.Value;
            if (!UnityEngine.Application.isPlaying) return Time.realtimeSinceStartup;
            return scaled ? Time.time : Time.unscaledTime;
        }

        /// <summary>Number of cooldowns being advanced. Test observability.</summary>
        internal static int RunningCount => _running.Count;

        internal static void Add(Cooldown cooldown)
        {
            if (!_running.Contains(cooldown)) _running.Add(cooldown);
            if (_subscribed) return;
            _subscribed = true;
            Canvas.willRenderCanvases += Tick;
        }

        internal static void Remove(Cooldown cooldown) => _running.Remove(cooldown);

        private static void Tick()
        {
            if (_running.Count == 0) return;
            _scratch.Clear();
            _scratch.AddRange(_running);
            for (var i = 0; i < _scratch.Count; i++) _scratch[i].Advance();
            _scratch.Clear();
        }

        /// <summary>One frame's worth of advancing, on demand — EditMode has no frames.</summary>
        internal static void TickForTests() => Tick();

        /// <summary>Holds both clocks at <paramref name="seconds"/> until the next reset.</summary>
        internal static void SetTimeForTests(float seconds) => SetTimeForTests(seconds, seconds);

        /// <summary>Holds the scaled and the unscaled clock separately, for the <c>time=</c> tests.</summary>
        internal static void SetTimeForTests(float scaled, float unscaled)
        {
            _pinnedScaled = scaled;
            _pinnedUnscaled = unscaled;
        }

        /// <summary>
        /// With <em>Enter Play Mode Options</em> skipping the domain reload, statics outlive a Play
        /// session — and the list would still hold the last session's cooldowns, whose GameObjects
        /// are gone.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetOnPlayStart() => ResetForTests();

        /// <summary>Forgets every cooldown, drops the pins and unsubscribes. Called from <c>UI.ResetForTests</c>.</summary>
        internal static void ResetForTests()
        {
            _running.Clear();
            _scratch.Clear();
            _pinnedScaled = null;
            _pinnedUnscaled = null;
            if (!_subscribed) return;
            Canvas.willRenderCanvases -= Tick;
            _subscribed = false;
        }
    }
}
