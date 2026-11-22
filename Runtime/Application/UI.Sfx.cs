using System;
using System.Collections.Generic;
using PromptUGUI.Controls;
using PromptUGUI.Lint;
using R3;
using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>
        /// UI sound cues (spec 2026-11-23-ui-sfx-cues): one cue per real user action — a click, a flip, a pick, an expand, a
        /// drag landing, a refused click — plus Screen / Modal open and close. Code writes, attribute passes, ReSolve, initial
        /// state and teardown raise nothing. The library plays nothing: subscribe and map <see cref="SfxCue.Key"/> to audio;
        /// throttling, variation and mixing are the game's.
        /// </summary>
        public static class Sfx
        {
            private static Subject<SfxCue> s_cues = new();
            private static readonly Dictionary<(string, string), string> s_defaultKeys = new();

            /// <summary>
            /// Every cue, in the order they happen: synchronous, on the main thread, inside the input dispatch — a control's
            /// cue comes before its own C# event (OnClick, OnValueChanged, …). Subscribe once at startup; with Domain Reload
            /// off the stream is rebuilt at every Play, so do not subscribe in the <c>SubsystemRegistration</c> phase.
            /// </summary>
            public static Observable<SfxCue> OnCue => s_cues;

            /// <summary>
            /// Raises a cue whose key is looked up over <paramref name="chain"/>, nearest first (spec §5.3). False when the
            /// chain mutes it. A throwing subscriber is logged and never reaches the caller (P22).
            /// </summary>
            internal static bool Raise(string kind, string action, IControl source, IControl target, IScreen screen,
                                       SfxSpec first, SfxSpec second = null)
            {
                var defaultKey = DefaultKey(kind, action);
                var key = defaultKey;
                if (first != null && first.TryDecide(action, out var nearest)) key = nearest;
                else if (second != null && second.TryDecide(action, out var next)) key = next;
                if (key == null) return false;

                var cue = new SfxCue(kind, action, key, defaultKey, source, target, screen);
                try { s_cues.OnNext(cue); }
                catch (Exception ex) { Debug.LogException(ex); }
                return true;
            }

            // One string per (kind, action): cues are frequent and their keys come from a small set.
            private static string DefaultKey(string kind, string action)
            {
                if (!s_defaultKeys.TryGetValue((kind, action), out var key))
                {
                    key = SfxRules.DefaultKey(kind, action);
                    s_defaultKeys[(kind, action)] = key;
                }
                return key;
            }

            // A fresh stream per Play session (P21): with Domain Reload off a static Subject would keep last session's
            // subscribers, and a startup subscription would play every sound twice. Before any user initialiser runs.
            [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
            private static void ResetForPlaySession() => s_cues = new Subject<SfxCue>();

            internal static void ResetForTestsInternal() => s_cues = new Subject<SfxCue>();
        }
    }
}
