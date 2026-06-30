#if ENABLE_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;

namespace PromptUGUI.Application.Navigation
{
    /// <summary>挂在 EventSystem GO 上：每帧按"上次输入设备"翻 UI.Navigation.Mode（spec §3）。
    /// 鼠标移动/点击/触屏 → Pointer；手柄摇杆/方向键/按钮、键盘导航键 → Directional。</summary>
    internal sealed class NavigationController : MonoBehaviour
    {
        private const float MouseMoveThreshold = 1f;   // 屏幕像素

        private void Update()
        {
            // Detect input device, then fall through to EnforceContainment regardless.
            // (Early returns would skip the containment check the same frame a directional
            // key navigates the selection out of the modal.)
            // Submit-class inputs (gamepad South / keyboard Enter) intentionally do NOT flip the
            // mode here. If they did, the same-frame race with InputSystemUIInputModule's Submit
            // dispatch could let the OnSubmit wake-gate read a stale Directional and act on a hidden
            // focus. Only genuine *movement* establishes Directional; Submit is woken via the gate
            // (PuiButton/PuiToggle.OnSubmit → UI.Navigation.TryWakeOnSubmit). buttonEast (Cancel/Back)
            // stays — a focus-independent back input that, lacking any ICancelHandler, cannot act on a
            // stale focus; it only re-establishes Directional. (Modals close on Esc / gamepad Start via
            // ModalEscapeListener, not on East.)
            var gp = Gamepad.current;
            if (gp != null && (gp.leftStick.ReadValue().sqrMagnitude > 0.25f
                               || gp.dpad.ReadValue().sqrMagnitude > 0.25f
                               || gp.buttonEast.wasPressedThisFrame))
            {
                UI.Navigation.NoteDirectionalInput();
            }
            else
            {
                var kb = Keyboard.current;
                if (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame
                                   || kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame
                                   || kb.tabKey.wasPressedThisFrame))
                {
                    UI.Navigation.NoteDirectionalInput();
                }
                else
                {
                    var mouse = Mouse.current;
                    if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > MouseMoveThreshold * MouseMoveThreshold
                                          || mouse.leftButton.wasPressedThisFrame))
                    {
                        UI.Navigation.NotePointerInput();
                    }
                    else if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                    {
                        UI.Navigation.NotePointerInput();
                    }
                }
            }

            UI.Navigation.EnforceContainment();
        }
    }
}
#else
namespace PromptUGUI.Application.Navigation
{
    internal sealed class NavigationController : UnityEngine.MonoBehaviour { }
}
#endif
