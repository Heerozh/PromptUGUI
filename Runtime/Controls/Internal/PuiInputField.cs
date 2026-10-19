using System;
using PromptUGUI.Application;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The <see cref="TMP_InputField"/> behind <c>&lt;InputField&gt;</c> (spec 2026-10-18 §6.6). With no
    /// suggestion list open it is TMP, unchanged. While one is open, ↑ / ↓ / Enter belong to the list:
    /// the keyboard through <see cref="OnUpdateSelected"/>, the gamepad through
    /// <see cref="OnMove"/> / <see cref="OnSubmit"/> / <see cref="OnCancel"/> — TMP itself swallows move
    /// while editing and would read Enter as "submit the text". It also reports the edit's edges
    /// (<see cref="InputField.EditingChanged"/>), whichever way editing ends.
    /// </summary>
    internal sealed class PuiInputField : TMP_InputField
    {
        /// <summary>
        /// How long after an arrow key the input module's Navigate is ignored. Holding ↓ produces both IMGUI
        /// key repeats and Navigate repeats (its first repeat comes 0.5 s after the press); only one of them
        /// may step the highlight, or it skips rows (P21).
        /// </summary>
        private const float ArrowNavigateWindow = 0.6f;

        internal InputField Owner;

        private readonly Event _event = new();
        private bool _wasFocused;
        private bool _wasComposing;
        private float _lastArrowKey = float.NegativeInfinity;

        private Suggestions List => Owner?.Suggestions;

        private bool ListTakesKeys => List != null && List.IsOpen && Owner.IsEditing;

        // ── Keyboard ───────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Takes the IMGUI event loop over while the list is open, or while an Escape the chain has spent
        /// is still on its way to TMP; otherwise <c>base</c>. There is no way to hand an event back to TMP,
        /// so the loop is TMP's own, with the list's keys routed out first. Everything it calls is TMP's
        /// public or protected surface.
        /// </summary>
        public override void OnUpdateSelected(BaseEventData eventData)
        {
            var composing = CompositionString().Length > 0;
            var compositionJustEnded = _wasComposing && !composing;
            _wasComposing = composing;

            var escapeSpent = EscapeChain.PopupConsumedFrame == Time.frameCount;
            if (!isFocused || composing || compositionJustEnded || !(ListTakesKeys || escapeSpent))
            {
                base.OnUpdateSelected(eventData);
                return;
            }

            var consumed = false;
            while (Event.PopEvent(_event))
            {
                var type = _event.rawType;
                if (type == EventType.KeyUp) continue;
                if (type == EventType.KeyDown)
                {
                    consumed = true;
                    if (HandleKeyDown(_event)) break;
                    continue;
                }
                if ((type == EventType.ValidateCommand || type == EventType.ExecuteCommand)
                    && _event.commandName == "SelectAll")
                {
                    SelectAll();
                    consumed = true;
                }
            }

            // Only a frame that actually spent a key is used: the input module skips move / submit / cancel
            // for the whole frame once update-selected is used, which would cut the gamepad off (P17).
            if (!consumed) return;
            UpdateLabel();
            eventData.Use();
        }

        /// <summary>
        /// One key-down while the list takes keys: the list's own action, or TMP's. True when the field
        /// left edit mode (a submit), which ends the event loop. Internal so EditMode tests drive it.
        /// </summary>
        internal bool HandleKeyDown(Event e)
        {
            var list = List;
            var state = new KeyState(
                listOpen: list != null && list.IsOpen,
                hasHighlight: list != null && list.HighlightedIndex >= 0,
                composing: false,
                compositionJustEnded: false,
                escapeSpent: EscapeChain.PopupConsumedFrame == Time.frameCount);

            switch (SuggestionKeys.Route(e, state))
            {
                case KeyAction.HighlightNext:
                    _lastArrowKey = Time.unscaledTime;
                    list.MoveHighlight(+1);
                    return false;
                case KeyAction.HighlightPrev:
                    _lastArrowKey = Time.unscaledTime;
                    list.MoveHighlight(-1);
                    return false;
                case KeyAction.CommitHighlighted:
                    list.CommitChosen(list.HighlightedIndex);
                    return true;
                case KeyAction.Swallow:
                    return false;
            }

            if (KeyPressed(e) == EditState.Finish)
            {
                if (!wasCanceled) SendOnSubmit();
                DeactivateInputField();
                return true;
            }
            UpdateLabel();
            // TMP's private "text component update required" flag stands for exactly this.
            if (textComponent != null) textComponent.ForceMeshUpdate();
            return false;
        }

        // The same lookup TMP makes: the current input module's BaseInput, else the legacy Input.
        private static string CompositionString()
        {
            try
            {
                var es = EventSystem.current;
                var input = es != null && es.currentInputModule != null ? es.currentInputModule.input : null;
                return (input != null ? input.compositionString : Input.compositionString) ?? string.Empty;
            }
            catch (InvalidOperationException)
            {
                // Input System-only projects may refuse the legacy Input class: no IME composition to see.
                return string.Empty;
            }
        }

        // ── Gamepad ────────────────────────────────────────────────────────────────────────

        public override void OnMove(AxisEventData eventData)
        {
            var vertical = eventData.moveDir == MoveDirection.Up || eventData.moveDir == MoveDirection.Down;
            if (vertical && ListTakesKeys)
            {
                // A Navigate echo of an arrow key the keyboard path has already handled.
                if (Time.unscaledTime - _lastArrowKey >= ArrowNavigateWindow)
                    List.MoveHighlight(eventData.moveDir == MoveDirection.Down ? +1 : -1);
                eventData.Use();
                return;
            }
            base.OnMove(eventData);
        }

        public override void OnSubmit(BaseEventData eventData)
        {
            if (ListTakesKeys && List.HighlightedIndex >= 0)
            {
                List.CommitChosen(List.HighlightedIndex);
                eventData?.Use();
                return;
            }
            base.OnSubmit(eventData);
        }

        /// <summary>
        /// The gamepad's East (and Escape, through the module's Cancel) closed the list through the chain
        /// this frame: TMP must not also cancel the edit and roll the text back (spec §6.2).
        /// </summary>
        public override void OnCancel(BaseEventData eventData)
        {
            if (EscapeChain.PopupConsumedFrame == Time.frameCount)
            {
                eventData?.Use();
                return;
            }
            if (ListTakesKeys)
            {
                List.Close();
                eventData?.Use();
                return;
            }
            base.OnCancel(eventData);
        }

        // ── Edit edges ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// <c>isFocused</c>'s edges, every way the edit can end: deselection, Enter, Escape, the soft
        /// keyboard closing, an in-place-editing change, the navigation gate.
        /// </summary>
        protected override void LateUpdate()
        {
            base.LateUpdate();
            var focused = isFocused;
            if (focused)
            {
                // Every frame of an edit; mid-composition the reported positions run ahead of the text.
                var composing = CompositionLength;
                RememberCaret(selectionStringAnchorPosition - composing, selectionStringFocusPosition - composing);
            }
            if (focused == _wasFocused) return;
            _wasFocused = focused;
            Owner?.NotifyEditingChanged(focused);
        }

        // ── The caret InputField.Insert uses once the edit is over (spec 2026-10-19 §4.12) ─────────────

        /// <summary>String positions of the selection the last edit ended with; −1 = never edited.</summary>
        internal int RememberedAnchor { get; private set; } = -1;

        internal int RememberedFocus { get; private set; } = -1;

        internal void RememberCaret(int anchor, int focus)
        {
            RememberedAnchor = anchor;
            RememberedFocus = focus;
        }

        internal void RememberCaretForTests(int anchor, int focus) => RememberCaret(anchor, focus);

        /// <summary>Test hook: an IME composition in progress (null = the live one).</summary>
        internal string CompositionForTests;

        /// <summary>The length of the IME's uncommitted text, read the way TMP reads it (its accessor is private).</summary>
        internal int CompositionLength
        {
            get
            {
                if (CompositionForTests != null) return CompositionForTests.Length;
                if (readOnly) return 0;
                try
                {
                    var es = EventSystem.current;
                    var input = es != null && es.currentInputModule != null ? es.currentInputModule.input : null;
                    var composition = input != null ? input.compositionString : Input.compositionString;
                    return composition?.Length ?? 0;
                }
                catch (System.InvalidOperationException)
                {
                    return 0;   // the legacy Input class is switched off and no module answers for it
                }
            }
        }

        /// <summary>
        /// The edit is about to end by a deselect: remember the selection now, before TMP lets go of it. The
        /// positions TMP reports already count the composition it is about to commit.
        /// </summary>
        public override void OnDeselect(BaseEventData eventData)
        {
            if (isFocused) RememberCaret(selectionStringAnchorPosition, selectionStringFocusPosition);
            base.OnDeselect(eventData);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            // InputField.OnAttached toggles this component before any list is adopted: both may be null.
            List?.Close();
            if (!_wasFocused) return;
            _wasFocused = false;
            Owner?.NotifyEditingChanged(false);
        }
    }
}
