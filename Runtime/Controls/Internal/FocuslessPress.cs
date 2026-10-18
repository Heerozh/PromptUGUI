using System;
using R3;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A press that is not a <see cref="UnityEngine.UI.Selectable"/> (spec 2026-10-18 §5.4) — the
    /// shell of a <c>&lt;Suggestions&gt;</c> row and the input field's submit icon. Pressing a
    /// Selectable selects it, which deselects the input field being edited and ends the edit; this
    /// takes the pointer without ever becoming the selection, so the input module's
    /// <c>GetEventHandler&lt;ISelectHandler&gt;</c> walks past it to the field.
    ///
    /// <para>It is the state source of the <c>&lt;Show on="state-*"&gt;</c> blocks under it, folded
    /// the way a Toggle's is: <see cref="SetOn"/> (the keyboard highlight) reads <c>selected</c>, and
    /// hover / pressed override it while they last.</para>
    /// </summary>
    internal sealed class FocuslessPress : MonoBehaviour, IStateSource,
        IPointerEnterHandler, IPointerExitHandler, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        // Field-initialised, not built in Awake: EditMode never runs Awake on this component, yet
        // ExecuteEvents still reaches its pointer handlers.
        private readonly StateBroadcaster _broadcaster = new();
        private readonly Subject<Unit> _clicked = new();

        private bool _interactable = true;
        private bool _inside;
        private bool _pressed;
        private bool _on;
        private InteractState _transient = InteractState.Normal;

        public Observable<InteractState> OnState => _broadcaster.OnState;
        public InteractState Current => _broadcaster.Current;
        public void RegisterShow(InteractState state, Action reevaluate) => _broadcaster.RegisterShow(state, reevaluate);
        public bool IsShowStateClaimed(InteractState state) => _broadcaster.IsShowStateClaimed(state);

        /// <summary>Only the EventSystem's selection is refreshed on a navigation-mode flip, and this
        /// never is one.</summary>
        public void RefreshState() { }

        /// <summary>False: reads <c>disabled</c>, takes no press and never clicks.</summary>
        internal bool Interactable
        {
            get => _interactable;
            set
            {
                if (_interactable == value) return;
                _interactable = value;
                if (!value) _pressed = false;
                PushTransient();
            }
        }

        /// <summary>A left press on it that has not been released yet.</summary>
        internal bool IsPressed => _pressed;

        /// <summary>A left click on it while interactable.</summary>
        internal Observable<Unit> OnClicked => _clicked;

        /// <summary>
        /// A left press on it was released; true when the release is about to click it (the pointer is still
        /// over it and the press did not turn into a drag). Raised before the click.
        /// </summary>
        internal event Action<bool> Released;

        /// <summary>The persistent highlight: reads <c>selected</c> while no hover / press overrides it.</summary>
        internal void SetOn(bool on)
        {
            if (_on == on) return;
            _on = on;
            _broadcaster.SetOn(on);
        }

        public void OnPointerEnter(PointerEventData eventData)
        {
            _inside = true;
            PushTransient();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            _inside = false;
            PushTransient();
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !_interactable) return;
            _pressed = true;
            PushTransient();
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !_pressed) return;
            _pressed = false;
            PushTransient();
            Released?.Invoke(_inside && _interactable && eventData.eligibleForClick);
        }

        public void OnPointerClick(PointerEventData eventData)
        {
            if (eventData.button != PointerEventData.InputButton.Left || !_interactable) return;
            _clicked.OnNext(Unit.Default);
        }

        private void OnDisable()
        {
            _inside = false;
            _pressed = false;
            PushTransient();
        }

        private void OnDestroy()
        {
            _broadcaster.Dispose();
            _clicked.Dispose();
        }

        // Same precedence as Selectable.currentSelectionState, minus selection: disabled, pressed
        // (wherever the pointer is now), hover. Pushed only on a change — the broadcaster re-runs
        // every <Show> on each push.
        private void PushTransient()
        {
            var t = !_interactable ? InteractState.Disabled
                : _pressed ? InteractState.Pressed
                : _inside ? InteractState.Hover
                : InteractState.Normal;
            if (t == _transient) return;
            _transient = t;
            _broadcaster.SetTransient(t);
        }
    }
}
