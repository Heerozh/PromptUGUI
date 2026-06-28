namespace PromptUGUI.Controls
{
    /// <summary>
    /// The broadcast interaction state of a clickable control (Btn / Tab / Toggle / any future
    /// <see cref="UnityEngine.UI.Selectable"/>-backed control), derived from the uGUI Selectable
    /// state machine and — for toggle-family controls — the persistent <c>isOn</c> flag.
    /// </summary>
    /// <remarks>
    /// uGUI's navigation-<c>Selected</c> state maps to <see cref="Focused"/> when
    /// <c>UI.Navigation</c> is in Directional mode; in Pointer mode it folds to <see cref="Normal"/>
    /// so a mouse click doesn't leave a control stuck-highlighted (spec §3).
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
