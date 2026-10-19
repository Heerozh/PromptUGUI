using UnityEngine;

namespace PromptUGUI.Application
{
    public static partial class UI
    {
        /// <summary>
        /// True in the frame PromptUGUI spent an Escape / gamepad Start / gamepad B press: it closed a popup
        /// (<c>&lt;Popover&gt;</c>, <c>&lt;TabMenu&gt;</c>, a suggestion list) or an open <c>&lt;Dropdown&gt;</c> list,
        /// a dialog answered it, a routed Modal went back, a blocking tutorial step swallowed it, or — Escape / B
        /// only — a text field being edited cancels its edit with it.
        /// <para>Game code that reads its own back key (an Input System action, <c>Input.GetKeyDown</c>) sees
        /// the same press — check this first and skip it. With the Input System the UI decides in the input
        /// update, before any <c>Update</c>; with only the legacy Input Manager it decides in an
        /// <c>Update</c>, so read it in <c>LateUpdate</c> there.</para>
        /// </summary>
        public static bool EscapeConsumedThisFrame => EscapeChain.ConsumedFrame == Time.frameCount;
    }
}
