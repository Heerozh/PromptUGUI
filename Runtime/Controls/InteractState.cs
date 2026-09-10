namespace PromptUGUI.Controls
{
    /// <summary>
    /// The broadcast interaction state of a clickable control (Btn / Tab / Toggle / any future
    /// <see cref="UnityEngine.UI.Selectable"/>-backed control), derived from the uGUI Selectable
    /// state machine and — for toggle-family controls — the persistent <c>isOn</c> flag.
    /// </summary>
    /// <remarks>
    /// uGUI's navigation-<c>Selected</c> state maps to <see cref="Focused"/> when
    /// <c>UI.Navigation</c> is in Directional mode. In Pointer mode it folds away: to
    /// <see cref="Hover"/> while the pointer is still inside — a click selects the control, and
    /// uGUI reports that selection in place of Highlighted from then on — and otherwise to
    /// <see cref="Normal"/>, so a click doesn't leave a control stuck-highlighted (spec §3).
    /// <see cref="Selected"/> here is the resting baseline of an <c>isOn</c> control:
    /// emitted when the control is active and not currently Hover/Pressed/Disabled. A momentary
    /// <see cref="Btn"/> has no <c>isOn</c>, so it never emits <see cref="Selected"/>.
    /// </remarks>
    public enum InteractState
    {
        Normal,
        Hover,
        Pressed,
        Selected,
        Disabled,
        Focused,   // keyboard/gamepad navigation focus — visible only in Directional mode (spec §4)
    }
}
