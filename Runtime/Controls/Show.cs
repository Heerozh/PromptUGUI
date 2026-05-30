using System;
using PromptUGUI.Controls.Internal;

namespace PromptUGUI.Controls
{
    /// <summary>
    /// A <see cref="Trigger"/> whose own subtree is <b>visible only while the source <c>&lt;Btn&gt;</c>
    /// is in the <c>on=</c> state</b> (and hidden otherwise) — a state-conditional view switch.
    /// Give several <c>&lt;Show&gt;</c> siblings different <c>on="state-*"</c> values to swap between
    /// alternative subtrees (e.g. swap an icon on press).
    /// </summary>
    /// <remarks>
    /// Coordination owner is the <see cref="PuiButton"/> (see <see cref="PuiButton.RegisterShow"/>):
    /// each Show only registers a state claim + a re-evaluation callback and never subscribes to
    /// <c>OnState</c> itself. Visibility uses <c>GameObject.SetActive</c> only (Strategy C — never
    /// destroyed), so a hidden alternative and its R3 subscriptions survive.
    /// <para>
    /// Normal fallback: the <c>state-normal</c> block also covers any state that has no explicit
    /// <c>&lt;Show&gt;</c> claim (e.g. Hover when no <c>state-hover</c> sibling exists). An explicit
    /// block for a state always wins for that state.
    /// </para>
    /// </remarks>
    public sealed class Show : Trigger
    {
        private InteractState _myState;
        private PuiButton _pui;

        protected override void InitTriggerSubscription()
        {
            _myState = _spec.Kind switch
            {
                TriggerKind.StateNormal => InteractState.Normal,
                TriggerKind.StateHover => InteractState.Hover,
                TriggerKind.StatePressed => InteractState.Pressed,
                TriggerKind.StateSelected => InteractState.Selected,
                TriggerKind.StateDisabled => InteractState.Disabled,
                _ => throw new InvalidOperationException(
                    "<Show> only accepts state-* events (state-normal / state-hover / " +
                    $"state-pressed / state-selected / state-disabled), got 'on=\"{OnRaw()}\"'."),
            };

            _pui = TriggerSourceResolver.FindStateSource(this, _spec.SourceId);
            _pui.RegisterShow(_myState, ReevaluateVisibility);
        }

        // Best-effort echo of the author's literal on= value for the error message.
        private string OnRaw() => _spec.Kind switch
        {
            TriggerKind.Open => "open",
            TriggerKind.Loop => "loop",
            TriggerKind.Manual => "manual",
            TriggerKind.Click => _spec.SourceId == null ? "click" : "click@" + _spec.SourceId,
            TriggerKind.HoverEnter => _spec.SourceId == null ? "hover-enter" : "hover-enter@" + _spec.SourceId,
            TriggerKind.HoverExit => _spec.SourceId == null ? "hover-exit" : "hover-exit@" + _spec.SourceId,
            TriggerKind.Press => _spec.SourceId == null ? "press" : "press@" + _spec.SourceId,
            _ => _spec.Kind.ToString(),
        };

        // active when: the source is in my state, OR I am the Normal block and the source's current
        // state has no explicit Show claim (Normal fallback). If neither an exact nor a Normal block
        // exists for the current state, that group shows nothing.
        private void ReevaluateVisibility()
        {
            var current = _pui.Current;
            var active = current == _myState
                         || (_myState == InteractState.Normal && !_pui.IsShowStateClaimed(current));
            GameObject.SetActive(active);
        }
    }
}
