using System;
using R3;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A control with a persistent on/off state — <c>&lt;Toggle&gt;</c> and <c>&lt;Tab&gt;</c>. It is
    /// what <c>on="checked"</c> / <c>on="unchecked"</c> listen to (spec
    /// 2026-08-31-hug-reveal-flip-checked-design §4).
    ///
    /// <para><b>Why this is not <c>state-selected</c>.</b> The <c>state-*</c> family is uGUI's
    /// transient interaction machine: Hover and Pressed override Selected while they last, so a
    /// <c>&lt;Show on="state-selected"&gt;</c> blinks out the moment the pointer touches the control.
    /// <c>checked</c> asks a different question — "is it on?" — which hovering does not change.</para>
    /// </summary>
    internal interface IToggleSource
    {
        public bool IsOn { get; }

        public Observable<bool> OnValueChanged { get; }

        /// <summary>
        /// Registers a <c>&lt;Show&gt;</c>'s re-evaluation callback. Called once immediately (so the
        /// block establishes itself at open) and again on every change of <see cref="IsOn"/>.
        /// </summary>
        public void RegisterCheckedShow(bool wantOn, Action reevaluate);
    }
}
