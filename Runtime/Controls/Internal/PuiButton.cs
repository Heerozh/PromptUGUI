using System;
using System.Collections.Generic;
using R3;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A uGUI <see cref="Button"/> that broadcasts its <see cref="Selectable"/> interaction
    /// state as a <see cref="BtnState"/> stream. <see cref="DoStateTransition"/> still calls
    /// <c>base</c> first, so the default <c>targetGraphic</c> ColorTint behaviour is preserved
    /// (back-compat); the mapped state is then pushed into an R3 <see cref="ReactiveProperty{T}"/>.
    /// </summary>
    internal sealed class PuiButton : Button
    {
        private readonly ReactiveProperty<BtnState> _state = new(BtnState.Normal);

        /// <summary>Replays the current state to new subscribers, then emits on every change.</summary>
        public Observable<BtnState> OnState => _state;

        public BtnState Current => _state.Value;

        // ---- <Show> coordination (Phase 4) ----
        // PuiButton is the single coordination owner: it tracks which BtnStates have an explicit
        // <Show> claim (so an unclaimed state can fall back to the Normal block) and re-drives every
        // registered Show on each state change. Shows do NOT separately subscribe to OnState — that
        // would duplicate the logic and fight ReactiveProperty's distinct-until-changed.
        private readonly HashSet<BtnState> _claimedShowStates = new();
        private readonly List<Action> _showReevaluators = new();

        /// <summary>States with at least one explicit <c><Show on="state-*"></c> sibling.</summary>
        internal IReadOnlyCollection<BtnState> ClaimedShowStates => _claimedShowStates;

        /// <summary>True if some <c><Show></c> has explicitly claimed <paramref name="state"/>.</summary>
        internal bool IsShowStateClaimed(BtnState state) => _claimedShowStates.Contains(state);

        /// <summary>
        /// Registers a <c><Show></c>: records its claimed <paramref name="state"/>, stores its
        /// <paramref name="reevaluate"/> callback, then invokes every stored callback. Because
        /// <c>ControlAttributeApplier</c> applies attributes after recursing children, sibling Shows
        /// register in sequence — re-evaluating all on each register guarantees every Show is correct
        /// once the last sibling has claimed its state (the claimed set is fully grown by then).
        /// </summary>
        internal void RegisterShow(BtnState state, Action reevaluate)
        {
            _claimedShowStates.Add(state);
            _showReevaluators.Add(reevaluate);
            for (int i = 0; i < _showReevaluators.Count; i++)
                _showReevaluators[i].Invoke();
        }

        /// <summary>
        /// Maps a uGUI <see cref="Selectable.SelectionState"/> to a <see cref="BtnState"/>.
        /// Selected folds into Normal (a momentary button keeps no sticky highlight after a tap).
        /// </summary>
        /// <remarks>
        /// Must be <c>protected</c> (not <c>internal</c>) because the parameter type
        /// <see cref="Selectable.SelectionState"/> is itself protected — an <c>internal</c> method
        /// taking it would be CS0051 "inconsistent accessibility". Tests reach this logic through
        /// the int overload <see cref="Map(int)"/>.
        /// </remarks>
        protected static BtnState Map(SelectionState state) => state switch
        {
            SelectionState.Normal => BtnState.Normal,
            SelectionState.Highlighted => BtnState.Hover,
            SelectionState.Pressed => BtnState.Pressed,
            SelectionState.Selected => BtnState.Normal,
            SelectionState.Disabled => BtnState.Disabled,
            _ => BtnState.Normal,
        };

        /// <summary>
        /// Test-callable overload of <see cref="Map(SelectionState)"/>. The test assembly cannot
        /// name the protected <see cref="Selectable.SelectionState"/> type, so it passes the
        /// ordinal int (Normal=0, Highlighted=1, Pressed=2, Selected=3, Disabled=4).
        /// </summary>
        internal static BtnState Map(int selectionStateOrdinal)
            => Map((SelectionState)selectionStateOrdinal);

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            _state.Value = Map(state);
            // Re-drive every registered Show against the new authoritative Current. (At the very
            // first transition during AddComponent the list is empty — Shows register later and
            // RegisterShow re-evaluates them then.)
            for (int i = 0; i < _showReevaluators.Count; i++)
                _showReevaluators[i].Invoke();
        }

        /// <summary>
        /// Test hook (mirrors <c>Btn.SimulateClick()</c>): drives a state transition without a
        /// live EventSystem. Takes the <see cref="Selectable.SelectionState"/> ordinal (see
        /// <see cref="Map(int)"/>) because the test assembly cannot name the protected type.
        /// </summary>
        internal void SimulateState(int selectionStateOrdinal)
            => DoStateTransition((SelectionState)selectionStateOrdinal, instant: true);

        protected override void OnDestroy()
        {
            _state.Dispose();
            base.OnDestroy();
        }
    }
}
