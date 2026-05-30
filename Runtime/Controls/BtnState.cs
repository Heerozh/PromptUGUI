namespace PromptUGUI.Controls
{
    /// <summary>
    /// The broadcast interaction state of a <see cref="Btn"/>, derived from the underlying
    /// uGUI <see cref="UnityEngine.UI.Selectable"/> state machine. Unlike Selectable — which
    /// only drives a single <c>targetGraphic</c> — a Btn pushes this state through an R3 stream
    /// so descendants (and C#) can react to press / hover / disabled.
    /// </summary>
    /// <remarks>
    /// Selectable's <c>Selected</c> state is folded into <see cref="Normal"/>: a momentary
    /// button must not keep a sticky highlight after a touch tap.
    /// </remarks>
    public enum BtnState
    {
        Normal,
        Hover,
        Pressed,
        Disabled,
    }
}
