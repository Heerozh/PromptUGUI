using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>What one key press does while a field has a suggestion list.</summary>
    internal enum KeyAction
    {
        /// <summary>TMP handles it as it always has.</summary>
        PassToTmp,
        HighlightNext,
        HighlightPrev,
        CommitHighlighted,

        /// <summary>Nobody acts on it: the Escape chain owns it.</summary>
        Swallow,
    }

    /// <summary>The state a key is routed against.</summary>
    internal readonly struct KeyState
    {
        public readonly bool ListOpen;
        public readonly bool HasHighlight;
        public readonly bool Composing;
        public readonly bool CompositionJustEnded;

        /// <summary>An Escape now belongs to a press <c>EscapeChain</c> spent on a popup (owed, or held down).</summary>
        public readonly bool EscapeSpent;

        public KeyState(bool listOpen, bool hasHighlight, bool composing, bool compositionJustEnded, bool escapeSpent)
        {
            ListOpen = listOpen;
            HasHighlight = hasHighlight;
            Composing = composing;
            CompositionJustEnded = compositionJustEnded;
            EscapeSpent = escapeSpent;
        }
    }

    /// <summary>
    /// Which keys the suggestion list takes from its field (spec 2026-10-18 §6.2), as a pure function
    /// so the table is tested key by key. Nothing is taken while an input method is composing, nor in the
    /// frame composition ends — those keys belong to the IME's own candidate window (§6.11).
    /// </summary>
    internal static class SuggestionKeys
    {
        private const EventModifiers Chords =
            EventModifiers.Shift | EventModifiers.Control | EventModifiers.Alt | EventModifiers.Command;

        internal static KeyAction Route(Event e, in KeyState s)
        {
            if (s.Composing || s.CompositionJustEnded) return KeyAction.PassToTmp;

            // The chain closed the list on this press in an Input System callback; TMP reads its IMGUI KeyDown
            // in this frame's EventSystem update or a later one, and must not also cancel the edit and roll
            // the text back.
            if (e.keyCode == KeyCode.Escape && s.EscapeSpent) return KeyAction.Swallow;
            if (!s.ListOpen) return KeyAction.PassToTmp;

            var bare = (e.modifiers & Chords) == 0;
            switch (e.keyCode)
            {
                case KeyCode.DownArrow when bare:
                    return KeyAction.HighlightNext;
                case KeyCode.UpArrow when bare:
                    return KeyAction.HighlightPrev;
                case KeyCode.Return:
                case KeyCode.KeypadEnter:
                    return s.HasHighlight ? KeyAction.CommitHighlighted : KeyAction.PassToTmp;
                case KeyCode.Escape:
                    return KeyAction.Swallow;
                default:
                    return KeyAction.PassToTmp;
            }
        }
    }
}
