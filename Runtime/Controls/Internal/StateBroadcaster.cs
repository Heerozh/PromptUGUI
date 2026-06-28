using System;
using System.Collections.Generic;
using R3;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Shared interaction-state holder for every <see cref="IStateSource"/>. Owns the R3 stream,
    /// the <c>&lt;Show&gt;</c> coordination set, and the composite rule that folds a transient
    /// Selectable state with a persistent checked (<c>isOn</c>) flag into one <see cref="InteractState"/>.
    /// A Selectable subclass holds one of these and forwards to it (C# cannot share a base across
    /// Button/Toggle).
    /// </summary>
    internal sealed class StateBroadcaster
    {
        private readonly ReactiveProperty<InteractState> _state = new(InteractState.Normal);
        private readonly HashSet<InteractState> _claimedShowStates = new();
        private readonly List<Action> _showReevaluators = new();

        private InteractState _transient = InteractState.Normal;
        private bool _isOn;

        /// <summary>Replays the current value to new subscribers, then emits on every change.</summary>
        public Observable<InteractState> OnState => _state;
        public InteractState Current => _state.Value;

        public bool IsShowStateClaimed(InteractState state) => _claimedShowStates.Contains(state);

        public void RegisterShow(InteractState state, Action reevaluate)
        {
            _claimedShowStates.Add(state);
            _showReevaluators.Add(reevaluate);
            for (int i = 0; i < _showReevaluators.Count; i++)
                _showReevaluators[i].Invoke();
        }

        /// <summary>Set the transient Selectable state (Normal/Hover/Pressed/Disabled).</summary>
        public void SetTransient(InteractState transient)
        {
            _transient = transient;
            Recompute();
        }

        /// <summary>Set the persistent checked flag (Toggle.isOn). Non-toggles never call this.</summary>
        public void SetOn(bool isOn)
        {
            _isOn = isOn;
            Recompute();
        }

        // Selected = resting baseline of an active control: transient Normal + isOn reads Selected;
        // any non-Normal transient overrides it (and reverts on release). The always-on "active"
        // indicator lives on the independent Toggle.graphic channel, so active-ness is never lost.
        private void Recompute()
        {
            // Focus is visible only in Directional input mode; in Pointer mode it folds to Normal so a
            // mouse click doesn't leave the control stuck-highlighted (spec §3).
            var t = _transient;
            if (t == InteractState.Focused && !PromptUGUI.Application.UI.Navigation.IsDirectional)
                t = InteractState.Normal;
            var composite = t == InteractState.Normal
                ? (_isOn ? InteractState.Selected : InteractState.Normal)
                : t;
            _state.Value = composite;
            for (int i = 0; i < _showReevaluators.Count; i++)
                _showReevaluators[i].Invoke();
        }

        /// <summary>
        /// Maps a uGUI <see cref="UnityEngine.UI.Selectable.SelectionState"/> ordinal to a transient
        /// <see cref="InteractState"/> (navigation-Selected folds to Normal). Takes the int ordinal
        /// because the protected SelectionState type cannot appear in a non-Selectable class's
        /// accessible signature (CS0051). Ordinals: Normal=0 Highlighted=1 Pressed=2 Selected=3 Disabled=4.
        /// </summary>
        public static InteractState MapTransient(int selectionStateOrdinal) => selectionStateOrdinal switch
        {
            0 => InteractState.Normal,
            1 => InteractState.Hover,
            2 => InteractState.Pressed,
            3 => InteractState.Focused,
            4 => InteractState.Disabled,
            _ => InteractState.Normal,
        };

        /// <summary>Re-runs Recompute without changing the held transient. Called by
        /// <see cref="IStateSource.RefreshState"/> when <c>UI.Navigation.Mode</c> flips while the
        /// control already holds a <see cref="InteractState.Focused"/> transient (spec §3).</summary>
        public void Refresh() => Recompute();

        public void Dispose() => _state.Dispose();
    }
}
