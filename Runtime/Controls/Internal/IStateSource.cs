using System;
using R3;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// Implemented by any Selectable-backed control that broadcasts its interaction state
    /// (PuiButton, PuiToggle). Consumers (<see cref="StateTintReactor"/>, <see cref="Show"/>,
    /// <see cref="TriggerSourceResolver.FindStateSource"/>) resolve this, never a concrete type,
    /// so the feature is generic over Button / Toggle / future Selectables.
    /// </summary>
    internal interface IStateSource
    {
        public Observable<InteractState> OnState { get; }
        public InteractState Current { get; }
        public void RegisterShow(InteractState state, Action reevaluate);
        public bool IsShowStateClaimed(InteractState state);
        /// <summary>Re-evaluate the transient state from the live Selectable SelectionState (used when
        /// the navigation Mode flips while this control stays selected — spec §3).</summary>
        public void RefreshState();
    }
}
