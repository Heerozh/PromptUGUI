namespace PromptUGUI.Controls
{
    /// <summary>
    /// The broadcast interaction state of a clickable control (Btn / Tab / Toggle / any future
    /// <see cref="UnityEngine.UI.Selectable"/>-backed control), derived from the uGUI Selectable
    /// state machine and — for toggle-family controls — the persistent <c>isOn</c> flag.
    /// </summary>
    /// <remarks>
    /// uGUI's navigation-<c>Selected</c> state folds to <see cref="Normal"/> (keyboard focus is not
    /// "checked"). <see cref="Selected"/> here is the resting baseline of an <c>isOn</c> control:
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
    }
}
