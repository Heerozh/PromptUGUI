using System;
using PromptUGUI.Application;
using R3;
using UnityEngine.EventSystems;
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
        private readonly Subject<Unit> _clicked = new();
        private int _bornFrame = int.MinValue;

        public Observable<InteractState> OnState => _broadcaster.OnState;

        /// <summary>
        /// Fires on every activation — pointer click or gamepad/keyboard Submit — <b>including</b>
        /// one that does not change <c>isOn</c>.
        ///
        /// <para><c>onValueChanged</c> cannot serve this: inside a <c>ToggleGroup</c> with
        /// <c>allowSwitchOff=false</c>, re-picking the already-selected member is swallowed
        /// (<c>isOn</c> was already true), so nothing is emitted. <see cref="TabMenu"/> needs the
        /// click regardless — picking the current channel still has to close the menu.</para>
        /// </summary>
        internal Observable<Unit> OnClicked => _clicked;
        public InteractState Current => _broadcaster.Current;
        public void RegisterShow(InteractState state, Action reevaluate) => _broadcaster.RegisterShow(state, reevaluate);
        public bool IsShowStateClaimed(InteractState state) => _broadcaster.IsShowStateClaimed(state);
        public void RefreshState() => _broadcaster.Refresh();

        /// <summary>
        /// Wires the checked dimension. Called by the owning control (Tab/Toggle) in OnAttached
        /// right after AddComponent — explicit timing, not a uGUI lifecycle hook.
        /// </summary>
        internal void InitStateBroadcast()
        {
            onValueChanged.AddListener(_broadcaster.SetOn);
            _broadcaster.SetOn(isOn);
        }

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
        /// 鼠标用过后焦点光标隐藏时，第一次 Submit 只唤回光标、不翻转 isOn（见 nav-hidden-submit-wake）。
        /// 仅拦键盘/手柄确认；鼠标点击走 OnPointerClick 不受影响。盖 Tab 与 Toggle。
        /// </summary>
        public override void OnSubmit(BaseEventData eventData)
        {
            if (UI.Navigation.TryWakeOnSubmit()) return;
            base.OnSubmit(eventData);
            // After the early return, deliberately: a wake-only Submit recalls the focus cursor
            // and is NOT an activation (see nav-hidden-submit-wake).
            _clicked.OnNext(Unit.Default);
        }

        public override void OnPointerClick(PointerEventData eventData)
        {
            base.OnPointerClick(eventData);
            // Same button filter uGUI's Toggle applies before InternalToggle: a right-click is not
            // an activation, and firing here would let it close a TabMenu the base ignored.
            if (eventData.button != PointerEventData.InputButton.Left) return;
            _clicked.OnNext(Unit.Default);
        }

        /// <summary>Test hook: drive a transition without a live EventSystem (ordinal — the test
        /// assembly cannot name the protected SelectionState type).</summary>
        internal void SimulateState(int selectionStateOrdinal)
            => DoStateTransition((SelectionState)selectionStateOrdinal, instant: true);

        /// <summary>Test hook: raise <see cref="OnClicked"/> without a live EventSystem.</summary>
        internal void SimulateClickForTests() => _clicked.OnNext(Unit.Default);

        protected override void OnDestroy()
        {
            _clicked.Dispose();
            _broadcaster.Dispose();
            base.OnDestroy();
        }
    }
}
