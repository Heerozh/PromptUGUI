using UnityEngine;

namespace PromptUGUI.Controls.Internal
{
    /// <summary>
    /// A control that scrolls and takes a <c>&lt;Scrollbar&gt;</c> part element — today
    /// <c>&lt;ScrollList&gt;</c> and <c>&lt;Dropdown&gt;</c> (spec 2026-09-12-scrollbar-part-element
    /// §4.2 / §5.6). <c>ScreenInstantiator</c> routes an authored <c>&lt;Scrollbar&gt;</c> child here
    /// instead of to <c>ChildHostTransform</c>, the way it routes <c>&lt;Header&gt;</c> to a
    /// <c>&lt;Collapsible&gt;</c>; a host that adopted nothing builds its own default bar.
    /// </summary>
    internal interface IScrollbarHost
    {
        /// <summary>
        /// Where the bar's node lives: the transform carrying the host's <c>ScrollRect</c>. uGUI's
        /// <c>AutoHideAndExpandViewport</c> only drives the viewport when the bar is a DIRECT child
        /// of the ScrollRect's transform (<c>ScrollRect.allAreChildren</c>), so this is not a choice.
        /// </summary>
        public RectTransform ScrollbarHost { get; }

        /// <summary>
        /// Takes ownership of an authored bar: wires it to the ScrollRect, orients it, and thereafter
        /// re-applies its visibility / spacing whenever either side changes. A second bar is parked
        /// inactive with a warning — one host, one bar.
        /// </summary>
        public void AdoptScrollbar(Scrollbar bar);

        /// <summary>
        /// The bar's own attributes changed (thickness / spacing / overlay) — the host re-applies
        /// what lives on the ScrollRect side. Idempotent; called from the bar's apply pass so a
        /// ReSolve that reaches the bar after the host still lands.
        /// </summary>
        public void OnScrollbarChanged(Scrollbar bar);
    }
}
