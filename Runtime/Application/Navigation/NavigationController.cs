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
            var gp = Gamepad.current;
            if (gp != null && (gp.leftStick.ReadValue().sqrMagnitude > 0.25f
                               || gp.dpad.ReadValue().sqrMagnitude > 0.25f
                               || gp.buttonSouth.wasPressedThisFrame
                               || gp.buttonEast.wasPressedThisFrame))
            {
                UI.Navigation.NoteDirectionalInput();
                return;
            }
            var kb = Keyboard.current;
            if (kb != null && (kb.leftArrowKey.wasPressedThisFrame || kb.rightArrowKey.wasPressedThisFrame
                               || kb.upArrowKey.wasPressedThisFrame || kb.downArrowKey.wasPressedThisFrame
                               || kb.tabKey.wasPressedThisFrame || kb.enterKey.wasPressedThisFrame))
            {
                UI.Navigation.NoteDirectionalInput();
                return;
            }
            var mouse = Mouse.current;
            if (mouse != null && (mouse.delta.ReadValue().sqrMagnitude > MouseMoveThreshold * MouseMoveThreshold
                                  || mouse.leftButton.wasPressedThisFrame))
            {
                UI.Navigation.NotePointerInput();
                return;
            }
            if (Touchscreen.current != null && Touchscreen.current.primaryTouch.press.wasPressedThisFrame)
                UI.Navigation.NotePointerInput();
        }
    }
}
#else
namespace PromptUGUI.Application.Navigation
{
    internal sealed class NavigationController : UnityEngine.MonoBehaviour { }
}
#endif
