namespace PromptUGUI.Controls
{
    /// <summary>Why a <see cref="Popover"/> closed (spec 2026-10-19 §4.4), as <see cref="Popover.OnClosed"/> reports it.</summary>
    public enum PopoverCloseReason
    {
        /// <summary>Your code: <c>Close()</c>, <c>Open()</c> on another anchor, the popover made non-interactable.</summary>
        Code,

        /// <summary>Its <c>on="click@…"</c> trigger was clicked again.</summary>
        Trigger,

        /// <summary>Esc, or the gamepad's Start / B.</summary>
        Escape,

        /// <summary>A press outside it (<c>dismiss="pass"</c>), or on its catcher (<c>dismiss="block"</c>).</summary>
        OutsidePress,

        /// <summary>A modal, a routed page or modal, or the Loading overlay appeared over it.</summary>
        Covered,

        /// <summary>Its anchor went away: destroyed, deactivated, disabled, scrolled out of view, or its
        /// list row was recycled for another item.</summary>
        AnchorLost,

        /// <summary>Another, unrelated floating panel opened (a popover, a menu, a suggestion list).</summary>
        Replaced,

        /// <summary>Its Screen began closing, an ancestor deactivated it, or it was disposed.</summary>
        HostGone,
    }
}
