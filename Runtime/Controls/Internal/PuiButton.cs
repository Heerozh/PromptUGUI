using System;
using PromptUGUI.Application;
using R3;
using UnityEngine.EventSystems;
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
        private int _bornFrame = int.MinValue;

        public Observable<InteractState> OnState => _broadcaster.OnState;
        public InteractState Current => _broadcaster.Current;
        public void RegisterShow(InteractState state, Action reevaluate) => _broadcaster.RegisterShow(state, reevaluate);
        public bool IsShowStateClaimed(InteractState state) => _broadcaster.IsShowStateClaimed(state);
        public void RefreshState() => _broadcaster.Refresh();

        protected override void OnEnable()
        {
            _bornFrame = BornFrame.Capture();
            base.OnEnable();   // paints currentSelectionState instantly (uGUI default)
        }

        protected override void DoStateTransition(SelectionState state, bool instant)
        {
            // A state change in the born frame (e.g. a modal Configure hook flipping interactable
            // right after Open) must be instant, not faded in from the freshly-painted enabled look.
            instant |= BornFrame.IsCurrent(_bornFrame);
            base.DoStateTransition(state, instant);
            _broadcaster.SetTransient(StateBroadcaster.MapTransient((int)state));
        }

        /// <summary>
        /// 鼠标用过后焦点光标隐藏时，第一次 Submit 只唤回光标、不点击（见 nav-hidden-submit-wake）。
        /// 仅拦键盘/手柄确认；鼠标点击走 OnPointerClick 不受影响。
        /// </summary>
        public override void OnSubmit(BaseEventData eventData)
        {
            if (UI.Navigation.TryWakeOnSubmit()) return;
            base.OnSubmit(eventData);
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
