using System;
using R3;
using UnityToggle = UnityEngine.UI.Toggle;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A uGUI <see cref="UnityToggle"/> that broadcasts its interaction state (transient Selectable
    /// state + persistent <c>isOn</c>) through a held <see cref="StateBroadcaster"/>. Serves both
    /// <see cref="Tab"/> and <see cref="Toggle"/>.
    /// </summary>
    internal sealed class PuiToggle : UnityToggle, IStateSource
    {
        private readonly StateBroadcaster _broadcaster = new();

        public Observable<InteractState> OnState => _broadcaster.OnState;
        public InteractState Current => _broadcaster.Current;
        public void RegisterShow(InteractState state, Action reevaluate) => _broadcaster.RegisterShow(state, reevaluate);
        public bool IsShowStateClaimed(InteractState state) => _broadcaster.IsShowStateClaimed(state);

        /// <summary>
        /// Wires the checked dimension. Called by the owning control (Tab/Toggle) in OnAttached
        /// right after AddComponent — explicit timing, not a uGUI lifecycle hook.
        /// </summary>
        internal void InitStateBroadcast()
        {
            onValueChanged.AddListener(_broadcaster.SetOn);
            _broadcaster.SetOn(isOn);
        }

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
