using System;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Utilities;
#endif

namespace PromptUGUI.Application.Modals
{
    internal sealed class ModalEscapeListener : MonoBehaviour
    {
        internal Action OnEscape;

        /// <summary>
        /// Also listen on the gamepad's Cancel button. Off for modals — a modal is dismissed with
        /// Start, and East stays free for the navigation controller to read as "back". A popup that
        /// is not a modal (an open <see cref="Controls.TabMenu"/>) does want East to close it.
        /// </summary>
        internal bool AlsoCancelButton;

#if ENABLE_INPUT_SYSTEM
        private global::UnityEngine.InputSystem.InputAction _action;
        private global::UnityEngine.InputSystem.InputActionMap _map;

        private void OnEnable()
        {
            _map = new global::UnityEngine.InputSystem.InputActionMap("PromptUGUI.Modal");
            _action = _map.AddAction("Escape", global::UnityEngine.InputSystem.InputActionType.Button);
            _action.AddBinding("<Keyboard>/escape");
            _action.AddBinding("<Gamepad>/start");
            if (AlsoCancelButton) _action.AddBinding("<Gamepad>/buttonEast");
            _action.performed += OnPerformed;
            _map.Enable();
        }

        private void OnDisable()
        {
            if (_map == null) return;
            if (_action != null)
            {
                _action.performed -= OnPerformed;
            }
            _map.Disable();
            _map.Dispose();
            _map = null;
            _action = null;
        }

        private void OnPerformed(global::UnityEngine.InputSystem.InputAction.CallbackContext _)
            => OnEscape?.Invoke();

#elif ENABLE_LEGACY_INPUT_MANAGER
        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
                OnEscape?.Invoke();
        }
#endif

        internal void FireForTests() => OnEscape?.Invoke();
    }
}
