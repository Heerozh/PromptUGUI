using System.Collections.Generic;
using UnityEngine;

#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace PromptUGUI.Application.Modals
{
    /// <summary>
    /// A source of back-key presses, hung on every ad-hoc dialog, every routed Modal and every open
    /// popup. It decides nothing: each press goes to <see cref="EscapeChain.Dispatch"/>, which acts
    /// once per press however many listeners heard it (spec 2026-10-18 §4.4).
    /// </summary>
    internal sealed class ModalEscapeListener : MonoBehaviour
    {
        private bool _alsoCancelButton;

        /// <summary>
        /// Also listen on the gamepad's Cancel button. Off for modals — a modal is dismissed with
        /// Start, and East stays free for the navigation controller to read as "back". A popup (an
        /// open <see cref="Controls.TabMenu"/>) does want East to close it. Setting it after the
        /// listener is enabled rebinds it.
        /// </summary>
        internal bool AlsoCancelButton
        {
            get => _alsoCancelButton;
            set
            {
                if (_alsoCancelButton == value) return;
                _alsoCancelButton = value;
#if ENABLE_INPUT_SYSTEM
                if (_map != null) Rebuild();
#endif
            }
        }

#if ENABLE_INPUT_SYSTEM
        private InputAction _action;
        private InputActionMap _map;

        // Disposing a map inside one of its own callbacks trips an Input System assert, and an Escape
        // that closes a dialog destroys that dialog's listener right there. Maps let go of during a
        // callback are disposed once the Input System update is over.
        private static int s_inCallback;
        private static readonly List<InputActionMap> s_pendingDispose = new();

        /// <summary>Whether the map, as built, answers the gamepad's East button.</summary>
        internal bool BindsCancelButton
        {
            get
            {
                if (_action == null) return false;
                foreach (var b in _action.bindings)
                    if (b.path == "<Gamepad>/buttonEast") return true;
                return false;
            }
        }

        // Built on the first enable and kept: a popup's panel is toggled on every open and close.
        private void OnEnable()
        {
            if (_map == null) Build();
            _map.Enable();
        }

        private void OnDisable()
        {
            if (_map != null) _map.Disable();   // safe inside a callback, unlike Dispose
        }

        private void OnDestroy() => Release();

        private void Build()
        {
            _map = new InputActionMap("PromptUGUI.Modal");
            _action = _map.AddAction("Escape", InputActionType.Button);
            _action.AddBinding("<Keyboard>/escape");
            _action.AddBinding("<Gamepad>/start");
            if (_alsoCancelButton) _action.AddBinding("<Gamepad>/buttonEast");
            _action.performed += OnPerformed;
        }

        private void Rebuild()
        {
            Release();
            Build();
            if (isActiveAndEnabled) _map.Enable();
        }

        private void Release()
        {
            if (_map == null) return;
            _action.performed -= OnPerformed;
            _map.Disable();
            if (s_inCallback > 0)
            {
                if (s_pendingDispose.Count == 0) InputSystem.onAfterUpdate += DisposePending;
                s_pendingDispose.Add(_map);
            }
            else
            {
                _map.Dispose();
            }
            _map = null;
            _action = null;
        }

        private static void DisposePending()
        {
            InputSystem.onAfterUpdate -= DisposePending;
            foreach (var map in s_pendingDispose) map.Dispose();
            s_pendingDispose.Clear();
        }

        private void OnPerformed(InputAction.CallbackContext context)
        {
            var key = context.control != null && context.control.name == "buttonEast"
                ? EscapeKey.CancelButton
                : EscapeKey.Escape;
            s_inCallback++;
            try
            {
                EscapeChain.Dispatch(key, Time.frameCount);
            }
            finally
            {
                s_inCallback--;
            }
        }

#else
        internal bool BindsCancelButton => false;

#if ENABLE_LEGACY_INPUT_MANAGER
        private void Update()
        {
            if (UnityEngine.Input.GetKeyDown(UnityEngine.KeyCode.Escape))
                EscapeChain.Dispatch(EscapeKey.Escape, Time.frameCount);
        }
#endif
#endif

        /// <summary>One Escape press, distinct from every other call.</summary>
        internal void FireForTests() => EscapeChain.Dispatch(EscapeKey.Escape, EscapeChain.NextTestPress());
    }
}
