namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// The "born frame" gate that decides when a control's state visual is established <em>instantly</em>
    /// versus animated.
    /// </summary>
    /// <remarks>
    /// uGUI paints a <see cref="UnityEngine.UI.Selectable"/>'s state instantly only at
    /// <c>OnEnable</c> (<c>DoStateTransition(currentSelectionState, instant: true)</c>). A programmatic
    /// state change a moment later — the Play-mode <c>interactable</c> setter
    /// (<c>OnSetProperty → DoStateTransition(instant: false)</c>), or a modal <c>Configure</c> hook
    /// disabling a button right after <c>Open</c> — fades in from the already-painted <em>enabled</em>
    /// look, a one-frame flash before it settles. uGUI assumes the initial state is authored in
    /// serialized data so it is present at <c>OnEnable</c>; our DSL establishes it imperatively
    /// (attribute apply + <c>Configure</c>) which runs after <c>OnEnable</c> but still before the first
    /// rendered frame.
    /// <para>
    /// So we mirror uGUI's "initial state == instant" rule, just widening the instant window from the
    /// single <c>OnEnable</c> tick to the whole build frame: any state change in the same frame the
    /// control was <see cref="Capture">born</see> (before its first rendered frame) is applied
    /// instantly; changes on a later frame fade as usual (preserving uGUI's intentional runtime fade).
    /// A modal's synchronous <c>Configure</c> runs in that same born frame, so its disables snap.
    /// </para>
    /// </remarks>
    internal static class BornFrame
    {
        /// <summary>Records the current frame as a control's birth frame (call from OnEnable / first init).</summary>
        internal static int Capture() => UnityEngine.Time.frameCount;

        /// <summary>True while we are still in the frame a control was born in (its first, not-yet-rendered frame).</summary>
        internal static bool IsCurrent(int bornFrame) => UnityEngine.Time.frameCount == bornFrame;
    }
}
