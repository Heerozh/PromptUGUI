using System;
using R3;
using UnityEngine.UI;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A uGUI <see cref="Button"/> that broadcasts its <see cref="Selectable"/> state through a
    /// held <see cref="StateBroadcaster"/>. <see cref="DoStateTransition"/> still calls
    /// <c>base</c> first, preserving the default <c>targetGraphic</c> ColorTint (back-compat).
    /// </summary>
    internal sealed class PuiButton : Button, IStateSource
    {
        private readonly StateBroadcaster _broadcaster = new();

        public Observable<InteractState> OnState => _broadcaster.OnState;
        public InteractState Current => _broadcaster.Current;
        public void RegisterShow(InteractState state, Action reevaluate) => _broadcaster.RegisterShow(state, reevaluate);
        public bool IsShowStateClaimed(InteractState state) => _broadcaster.IsShowStateClaimed(state);

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            base.DoStateTransition(state, instant);
            _broadcaster.SetTransient(StateBroadcaster.MapTransient((int)state));
        }

        /// <summary>Test hook: drive a transition without a live EventSystem (ordinal — the test
        /// assembly cannot name the protected SelectionState type).</summary>
        internal void SimulateState(int selectionStateOrdinal)
            => DoStateTransition((SelectionState)selectionStateOrdinal, instant: true);

        protected override void OnDestroy()
        {
            _broadcaster.Dispose();
            base.OnDestroy();
        }
    }
}
